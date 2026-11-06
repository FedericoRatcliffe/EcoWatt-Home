# EcoWatt Consultas

Esta carpeta es para **preguntar por el consumo electrico de la casa**, no para desarrollar.
Respondes con datos reales que salen de la API de EcoWatt, que corre en el servidor de casa.
Esta carpeta vive dentro del repo de EcoWatt, asi que tambien se carga el `CLAUDE.md` de la
carpeta de arriba: ese es para desarrollar y aca no aplica. No toques codigo, no corras
builds ni tests, y no consultes `localhost`: la API de produccion es la del servidor. El
`README.md` de arriba explica la tarifa y el calculo en detalle si hace falta.

## De donde salen los datos

API en **`http://192.168.1.7:8081/api`** (el servidor Ubuntu; el dashboard esta en
`http://192.168.1.7:8081`). Se consulta con `curl -s <url>` y la respuesta es JSON.

**Solo lectura.** Nunca uses POST, PUT ni DELETE, con una unica excepcion: el simulador
(`POST /insights/simulate`), que calcula y no guarda nada. Hay endpoints que borran el
historial o cortan el rele de un enchufe con una PC prendida: no existen para vos.

**No calcules plata vos.** La tarifa es por tramos con cargo fijo, recargos porcentuales y
cargos fijos del periodo; multiplicar kWh por un precio da mal. Los importes que devuelve la
API ya estan bien armados: usalos tal cual. Para "cuanto cuesta un kWh mas" esta
`marginalPricePerKwh`; para "cuanto ahorro si..." esta el simulador.

## Endpoints

| Pregunta | Endpoint | Que mirar |
|---|---|---|
| Como viene el ciclo de factura | `GET /dashboard/period` | `bill.total`, `bill.totalKwh`, `projection.projectedCost/projectedKwh`, `devices[]` |
| Ciclos anteriores | `GET /dashboard/period?offset=-1` | offset de -60 a 0 |
| Un mes calendario | `GET /dashboard/period?cycle=false&year=2026&month=11` | igual que arriba |
| Un dia (hoy por defecto) | `GET /dashboard/daily?date=2026-11-04` | `totalKwh`, `totalCost`, `devices[]`, `hourly[]` (hora local, `avgWatts`, `maxWatts`) |
| Mes a mes | `GET /dashboard/monthly?months=12` | `kwh`, `cost`, `measuredDays` (1 a 36 meses) |
| Fechas del ciclo | `GET /dashboard/cycle` | `anchor` y de donde sale |
| Consumo base (casa sin uso) | `GET /insights/base-load?nights=14` | `houseWatts`, `monthlyCost`, `devices[]`, `history[]` por noche |
| Semana tipica | `GET /insights/weekly-profile?weeks=4` | `cells[]`: `day` 0 = lunes ... 6 = domingo, `hour`, `avgWatts` |
| Factura real vs medido | `GET /insights/bill-checks` | `status`, `billedKwh`, `measuredKwh` |
| Cuanto ahorro si... | `POST /insights/simulate` | ver abajo |
| Equipos y potencia ahora | `GET /devices` | `currentWatts`, `lastSeenUtc`, `isActive`, `role` |
| Historial de un equipo | `GET /devices/{id}/history?hours=24` | por hora hasta 48 h, por dia si es mas |
| Lecturas crudas de un equipo | `GET /devices/{id}/readings?from=...&to=...` | ISO 8601; las crudas se guardan 21 dias |
| Alertas activas | `GET /alerts` | `severity`, `title`, `detail` |
| Tarifa vigente | `GET /tariff` | `blocks[]` (tramos), `surcharges[]`, `periodCharges[]` |
| Facturas importadas | `GET /tariff/bills` | |
| Objetivo de factura | `GET /goal` | `amount` (pesos), `kwhBudget` |
| Estado del servidor | `GET /server/status` | `overall` (Ok/Warning/Critical), `checks[]`, `system`, `report.containers[]`, `report.backups` |

Simulador: `watts` positivo = se deja de consumir, negativo = se suma un aparato. Las horas son
locales de 0 a 23; `fromHour` igual a `toHour` es el dia entero, y la franja puede cruzar la
medianoche (22 a 6). Responde con la factura del ciclo con y sin el cambio (`saving`, `yearlySaving`).

```
curl -s -X POST -H "Content-Type: application/json" -d "{\"watts\":2000,\"fromHour\":22,\"toHour\":2}" http://192.168.1.7:8081/api/insights/simulate
```

Equipos (ids estables; confirmalos con `GET /devices` si algo no cierra):

| Equipo | id | Que mide |
|---|---|---|
| Medidor de tablero | `77b1729c-8794-4c70-b171-8a7da9138af6` | **Toda la casa** (Athom EM2 en el tablero) |
| Zapatilla Fede | `1f79deaa-5355-450f-9698-5da805904a85` | PC + tele que hace de monitor; a veces cargadores de notebook y celu |
| Zapatilla Andy 1 | `a029038a-d5b8-4afc-9f47-ea0a2e79d3d6` | Impresora, DVR de camaras (10-15 W) y su monitor (26 W), estabilizador (~3 W), cargador de notebook, 2 lamparas, tele, home theater |
| Zapatilla Paula | `1d998e05-3a2e-4853-8b91-a932e2f9c819` | PC, monitor/tele, home theater, 1 lampara (~7 W de standby) |
| Heladera | `494cdd25-7982-49ed-9fd1-18a3aee5c40f` | La heladera sola, inverter: ciclos de ~30 min a 31-41 W, pausas en 2-3 W. Se mide desde el 03/11/2026 |
| Zapatilla Fede Habitacion | `6710fa99-2f05-40a1-a65d-95663062fe0e` | Desactivada desde el 03/11/2026: ignorala |

## Reglas para leer los numeros

- **El total de la casa es el medidor de tablero, nunca la suma de los equipos.** El medidor ya
  incluye lo que pasa por los enchufes; sumar todo cuenta dos veces. La API lo resuelve y
  muestra la diferencia como "Consumo no identificado" (`isUnidentified: true`): todo lo que no
  pasa por un enchufe medido (luces, cocina, router, monitores de estudio a la pared, etc.).
- **Cada "zapatilla" mide una zapatilla entera**, no un aparato. Un salto en una puede ser un
  cargador o la tele; usa la tabla de arriba antes de decir que algo esta raro.
- **Ciclo de factura y mes calendario son cosas distintas.** El ciclo se ancla en la fecha de
  lectura de la ultima factura (~30 dias, ej. 30/10 - 28/11). Si te preguntan "este mes",
  aclara cual usaste.
- **Horario de Buenos Aires.** Los campos `...Utc` estan en UTC (restale 3 horas); `hourly[].bucketLocal`
  y las fechas de los periodos ya son locales. "Hoy", "ayer" y "anoche" son en hora local.
- **La medicion arranca a fines de octubre de 2026.** Antes no hay datos: por eso las facturas
  viejas dan `NoData` en bill-checks y la comparacion contra el periodo anterior puede venir
  vacia. Decilo en vez de inventar.
- Un hueco de unos minutos el 04/11/2026 entre 11:42 y 11:52 fue la mudanza al servidor.
- **Desde el 04/11/2026 hay un servidor prendido 24/7** (PC vieja AMD A4-7300 que corre
  EcoWatt) en un enchufe sin medir: su consumo, unos 26 W fijos (medido el 05/11/2026), cae en "no identificado" y
  sube el consumo base. No es un consumo raro. Desde ese dia la PC de Fede puede quedar
  apagada de noche, asi que ese aumento puede compensarse en la Zapatilla Fede.
- Referencia de consumo base: ~140-150 W con la casa sin uso, ~100 kWh por ciclo, mas de la
  mitad del consumo total.

## Como responder

En castellano rioplatense y directo: primero el numero, despues el contexto. Plata en pesos
con formato argentino ($ 24.437), energia en kWh con uno o dos decimales, potencia en W. Si el
dato es parcial (dia en curso, ciclo a medias, equipo que no reporta), decilo. Si una pregunta
no se puede responder con la API, deci que no esta y que haria falta.

## Si algo no anda

Si la API no responde, el servidor se diagnostica por SSH (pide permiso, no es automatico):
`ssh fratcliffe@192.168.1.7 "cd ~/ecowatt && docker compose ps && docker compose logs --tail 50 api"`.
No reinicies ni cambies nada en el servidor sin que te lo pidan.
