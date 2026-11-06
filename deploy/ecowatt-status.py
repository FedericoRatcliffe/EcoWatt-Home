#!/usr/bin/env python3
"""Junta lo que la API no ve desde su contenedor y lo deja en ~/ecowatt-status/status.json.

Lo corre cron cada minuto (la linea la instala deploy.ps1). La API monta esa carpeta de solo
lectura y la muestra en la pantalla "Servidor".

Por que un script y no darle a la API el socket de Docker: con el socket, quien encuentre una
falla en la API (que no tiene autenticacion) es root en el servidor. Asi la API solo lee un
archivo. Cada seccion se junta por separado: si una falla, va a "errors" y el resto sale igual.
"""
import datetime
import glob
import json
import os
import subprocess
import tempfile

HOME = os.path.expanduser("~")
OUT_DIR = os.path.join(HOME, "ecowatt-status")
OUT_FILE = os.path.join(OUT_DIR, "status.json")
BACKUP_DIR = os.path.join(HOME, "backups")
PROJECT = "ecowatt"  # el -p de docker compose en deploy.ps1
SMARTCTL = "/usr/sbin/smartctl"  # el unico comando con sudo sin contrasena (sudoers.d/smartctl)


def run(*args, timeout=20):
    return subprocess.run(args, capture_output=True, text=True, timeout=timeout)


def utc_iso(ts):
    return datetime.datetime.fromtimestamp(ts, datetime.timezone.utc).isoformat()


def parse_size(text):
    """'185.2MiB' -> bytes. docker stats usa unidades binarias."""
    units = {"B": 1, "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "TiB": 1024**4,
             "kB": 1000, "KB": 1000, "MB": 1000**2, "GB": 1000**3}
    text = text.strip()
    for unit in sorted(units, key=len, reverse=True):
        if text.endswith(unit):
            return int(float(text[: -len(unit)]) * units[unit])
    return None


def docker_time(value):
    """Docker da nanosegundos (y 0001-01-01 si nunca arranco); .NET lee hasta siete decimales."""
    if not value or value.startswith("0001"):
        return None
    base, _, fraction = value.rstrip("Z").partition(".")
    return base + ("." + fraction[:6] if fraction else "") + "Z"


def containers():
    names = run("docker", "ps", "-a", "--filter", f"label=com.docker.compose.project={PROJECT}",
                "--format", "{{.Names}}").stdout.split()
    if not names:
        return []

    inspected = json.loads(run("docker", "inspect", *names).stdout)

    # docker stats da el % sobre UN nucleo (llega a 200 % con dos). Se divide por la cantidad de
    # nucleos para que se lea igual que el CPU de la maquina en la pantalla.
    cores = os.cpu_count() or 1
    stats = {}
    running = [c["Name"].lstrip("/") for c in inspected if c["State"]["Status"] == "running"]
    if running:
        out = run("docker", "stats", "--no-stream", "--format", "{{json .}}", *running, timeout=30).stdout
        for line in out.splitlines():
            s = json.loads(line)
            stats[s["Name"]] = (
                round(float(s["CPUPerc"].rstrip("%")) / cores, 1),
                parse_size(s["MemUsage"].split("/")[0]),
            )

    result = []
    for c in inspected:
        name = c["Name"].lstrip("/")
        state = c["State"]
        cpu, mem = stats.get(name, (None, None))
        result.append({
            "name": name,
            "state": state["Status"],
            "health": (state.get("Health") or {}).get("Status"),
            "startedUtc": docker_time(state.get("StartedAt")),
            "restartCount": c.get("RestartCount", 0),
            "cpuPercent": cpu,
            "memoryBytes": mem,
            "image": c["Config"].get("Image"),
        })
    return sorted(result, key=lambda c: c["name"])


def root_disk():
    source = run("findmnt", "-n", "-o", "SOURCE", "/").stdout.strip()
    parent = run("lsblk", "-no", "PKNAME", source).stdout.strip()
    return "/dev/" + parent if parent else source


def disk():
    device = root_disk()
    # Sin check: smartctl usa el codigo de salida como mascara de bits y no-cero no es error.
    data = json.loads(run("sudo", "-n", SMARTCTL, "-j", "-H", "-A", device).stdout)
    attributes = {a["id"]: a["raw"]["value"] for a in data.get("ata_smart_attributes", {}).get("table", [])}
    return {
        "device": device,
        "passed": (data.get("smart_status") or {}).get("passed"),
        "temperatureC": (data.get("temperature") or {}).get("current"),
        "powerOnHours": (data.get("power_on_time") or {}).get("hours"),
        "reallocatedSectors": attributes.get(5),
        "pendingSectors": attributes.get(197),
        "uncorrectableSectors": attributes.get(198),
    }


def backups():
    dumps = sorted(glob.glob(os.path.join(BACKUP_DIR, "ecowatt-*.dump")), key=os.path.getmtime)
    last_line = None
    log = os.path.join(BACKUP_DIR, "backup.log")
    if os.path.exists(log):
        with open(log, encoding="utf-8", errors="replace") as f:
            lines = [l.strip() for l in f if l.strip()]
        last_line = lines[-1] if lines else None

    last = dumps[-1] if dumps else None
    return {
        "count": len(dumps),
        "lastFile": os.path.basename(last) if last else None,
        "lastUtc": utc_iso(os.path.getmtime(last)) if last else None,
        "lastSizeBytes": os.path.getsize(last) if last else None,
        "lastLogLine": last_line,
        # ecowatt-backup.sh termina con "<fecha> ok <tamano>"; si algo fallo, set -e corta antes
        # y la ultima linea es el error.
        "lastLogOk": (" ok " in f" {last_line} ") if last_line else None,
    }


def os_name():
    with open("/etc/os-release", encoding="utf-8") as f:
        for line in f:
            if line.startswith("PRETTY_NAME="):
                return line.split("=", 1)[1].strip().strip('"')
    return None


def deployed_commit():
    path = os.path.join(HOME, "ecowatt", ".deployed-commit")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        return f.read().strip() or None


def main():
    report = {
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "rebootRequired": os.path.exists("/var/run/reboot-required"),
        "containers": [],
        "errors": [],
    }

    sections = [
        ("osName", os_name, "el sistema operativo"),
        ("deployedCommit", deployed_commit, "el commit deployado"),
        ("containers", containers, "los contenedores"),
        ("disk", disk, "el SMART del disco"),
        ("backups", backups, "los backups"),
    ]
    for key, read, what in sections:
        try:
            report[key] = read()
        except Exception as e:  # una seccion rota no puede dejar sin reporte al resto
            report["errors"].append(f"No se pudo leer {what}: {type(e).__name__}: {e}")

    os.makedirs(OUT_DIR, exist_ok=True)
    # Temporal + rename: la API nunca lee un archivo a medio escribir. Va en el mismo
    # directorio para que el rename sea atomico, y la API monta el directorio y no el archivo
    # porque un bind mount de archivo se queda con el inodo viejo despues del rename.
    fd, tmp = tempfile.mkstemp(dir=OUT_DIR, prefix=".status-", suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    os.chmod(tmp, 0o644)
    os.replace(tmp, OUT_FILE)


if __name__ == "__main__":
    main()
