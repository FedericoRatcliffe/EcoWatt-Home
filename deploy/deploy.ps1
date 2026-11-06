# Actualiza el servidor de casa (192.168.1.7) con el ultimo commit del repo.
#
#   powershell -ExecutionPolicy Bypass -File deploy\deploy.ps1
#
# Deploya HEAD, no lo que hay sin commitear. Arma las imagenes nuevas al lado de las que estan
# corriendo y recien al final cambia los contenedores, asi el corte de datos es de segundos y no
# de los ~10 minutos que tarda el build en ese procesador. Antes de tocar nada hace un backup.
param([string]$Server = 'fratcliffe@192.168.1.7')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (git -C $repo status --porcelain) {
    Write-Warning 'Hay cambios sin commitear: se deploya el ultimo commit, no lo que ves en disco.'
}
$commit = (git -C $repo rev-parse --short HEAD).Trim()

# Por archivo y no por pipe: PowerShell 5 pasa los pipes entre ejecutables como texto y rompe el
# tar. autocrlf=false porque git archive aplica la conversion de fin de linea de Windows, y el
# servidor es Linux.
$tar = Join-Path $env:TEMP "ecowatt-$commit.tar"
git -C $repo -c core.autocrlf=false archive --format=tar -o $tar HEAD
scp -q -o BatchMode=yes $tar "${Server}:/tmp/ecowatt-deploy.tar"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar el paquete al servidor.' }
Remove-Item $tar

$remote = @"
set -eu
rm -rf ~/ecowatt.new && mkdir ~/ecowatt.new
tar -x -C ~/ecowatt.new -f /tmp/ecowatt-deploy.tar && rm /tmp/ecowatt-deploy.tar
cd ~/ecowatt.new
echo $commit > .deployed-commit
cp deploy/docker-compose.server.yml docker-compose.override.yml
mkdir -p ~/bin ~/ecowatt-status
install -m 755 deploy/ecowatt-backup.sh ~/bin/ecowatt-backup.sh
install -m 755 deploy/ecowatt-status.py ~/bin/ecowatt-status.py

# Las dos lineas de cron las escribe el deploy: se sacan las que haya y se ponen de nuevo, asi
# correrlo dos veces no las duplica. El || true es porque grep sin nada que devolver sale con 1,
# y con set -e eso cortaria el bloque y dejaria el crontab vacio.
{ crontab -l 2>/dev/null | grep -v -e ecowatt-backup.sh -e ecowatt-status.py || true
  echo '30 3 * * * `$HOME/bin/ecowatt-backup.sh >> `$HOME/backups/backup.log 2>&1'
  echo '* * * * * `$HOME/bin/ecowatt-status.py > /dev/null 2>&1'
} | crontab -

echo '== backup'
~/bin/ecowatt-backup.sh

# -p fijo: el nombre del proyecto sale de la carpeta, y en ecowatt.new las imagenes y el volumen
# de la base quedarian con otro nombre.
echo '== build'
docker compose -p ecowatt build --quiet

echo '== cambio'
cd ~ && rm -rf ecowatt.prev && mv ecowatt ecowatt.prev && mv ecowatt.new ecowatt
cd ~/ecowatt && docker compose -p ecowatt up -d --remove-orphans
docker image prune -f > /dev/null

for i in `$(seq 1 40); do
  curl -sf -m 3 localhost:5080/api/devices > /dev/null && ~/bin/ecowatt-status.py && echo "== ok: API arriba con $commit" && exit 0
  sleep 3
done
echo '== la API no respondio en 2 minutos'
docker compose -p ecowatt logs --tail 40 api
exit 1
"@

# Por archivo y no por pipe a `bash -s`: PowerShell 5 le antepone un BOM al texto que le pasa a
# un ejecutable, y pegado al `set -eu` lo vuelve un comando desconocido; el script seguiria de
# largo ante cualquier error. Sin CRLF porque este .ps1 puede venir con CRLF segun git.
$script = Join-Path $env:TEMP "ecowatt-deploy-$commit.sh"
[IO.File]::WriteAllText($script, ($remote -replace "`r", ''), (New-Object System.Text.UTF8Encoding $false))
scp -q -o BatchMode=yes $script "${Server}:/tmp/ecowatt-deploy.sh"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo copiar el script al servidor.' }
Remove-Item $script
ssh -o BatchMode=yes $Server 'bash /tmp/ecowatt-deploy.sh; rc=$?; rm -f /tmp/ecowatt-deploy.sh; exit $rc'
if ($LASTEXITCODE -ne 0) { throw "El deploy fallo en el servidor (codigo $LASTEXITCODE)." }
