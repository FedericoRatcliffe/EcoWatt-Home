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
mkdir -p ~/bin && install -m 755 deploy/ecowatt-backup.sh ~/bin/ecowatt-backup.sh

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
  curl -sf -m 3 localhost:5080/api/devices > /dev/null && echo "== ok: API arriba con $commit" && exit 0
  sleep 3
done
echo '== la API no respondio en 2 minutos'
docker compose -p ecowatt logs --tail 40 api
exit 1
"@

# bash no acepta CRLF, y este archivo puede venir con CRLF segun como lo haya sacado git.
($remote -replace "`r", '') | ssh -o BatchMode=yes $Server 'bash -s'
if ($LASTEXITCODE -ne 0) { throw "El deploy fallo en el servidor (codigo $LASTEXITCODE)." }
