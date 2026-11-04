# Trae a esta PC los backups diarios de la base que genera el servidor (192.168.1.7).
# El servidor guarda 30 dias en su propio disco; esta copia existe por si ese disco se muere.
#
# Va copiado en Documents\EcoWatt-Backups\pull.ps1 (deja los dumps al lado suyo) y lo corre la
# tarea programada "EcoWatt Backup Pull", a las 13:00 y al iniciar sesion.
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $dir 'pull.log'
try {
    $remote = ssh -o BatchMode=yes -o ConnectTimeout=10 fratcliffe@192.168.1.7 'ls ~/backups/ecowatt-*.dump'
    if ($LASTEXITCODE -ne 0) { throw "ssh devolvio $LASTEXITCODE" }
    $new = 0
    foreach ($path in $remote) {
        $name = Split-Path -Leaf $path
        if (-not (Test-Path (Join-Path $dir $name))) {
            scp -q -o BatchMode=yes "fratcliffe@192.168.1.7:$path" (Join-Path $dir "$name.part")
            if ($LASTEXITCODE -ne 0) { throw "scp de $name devolvio $LASTEXITCODE" }
            Move-Item -Force (Join-Path $dir "$name.part") (Join-Path $dir $name)
            $new++
        }
    }
    # 180 dias locales: cada dump pesa un par de MB porque la API poda las lecturas crudas.
    Get-ChildItem $dir -Filter 'ecowatt-*.dump' | Where-Object LastWriteTime -lt (Get-Date).AddDays(-180) | Remove-Item
    "$(Get-Date -Format s) ok, $new nuevos" | Add-Content $log
} catch {
    "$(Get-Date -Format s) ERROR $($_.Exception.Message)" | Add-Content $log
    exit 1
}
