#!/bin/sh
# Backup diario de la base de EcoWatt en el servidor. Lo corre cron a las 3:30 (la linea la
# instala deploy.ps1) y deploy.ps1 antes de cada actualizacion. La PC de Windows se trae las
# copias con pull-backups.ps1, asi hay una fuera del disco del servidor.
set -eu
DIR="$HOME/backups"
STAMP=$(date +%F)
TMP="$DIR/.ecowatt-$STAMP.tmp"
mkdir -p "$DIR"
docker exec ecowatt-postgres pg_dump -U ecowatt -d ecowatt -Fc > "$TMP"
# Un dump cortado no pisa al bueno del mismo dia: si pg_restore no lo puede leer, falla aca.
docker exec -i ecowatt-postgres pg_restore --list < "$TMP" > /dev/null
mv "$TMP" "$DIR/ecowatt-$STAMP.dump"
find "$DIR" -name "ecowatt-*.dump" -mtime +30 -delete
echo "$(date "+%F %T") ok $(du -h "$DIR/ecowatt-$STAMP.dump" | cut -f1)"
