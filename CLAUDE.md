# CLAUDE.md

Guia para trabajar en este repo. `README.md` es la referencia de dominio (tarifa por tramos,
ciclo de facturacion, rollup horario, alertas, paso al hardware real): **leelo antes de tocar
calculo de energia o de costo** en vez de reconstruir las reglas desde el codigo. Este archivo
cubre lo operativo y las convenciones que no se deducen del README.

## Arranque

```bash
docker compose up -d postgres                              # unica dependencia externa
dotnet run --project EcoWattCasa.API                       # http://localhost:5080 + broker MQTT en :1883
dotnet run --project EcoWattCasa.MockDevices               # telemetria simulada cada 10 s
cd ecowatt-frontend && npm start                           # http://localhost:4200 (proxy a :5080)
```

El broker MQTT viene embebido en la API (`Mqtt:Embedded`), no hace falta Mosquitto. La API
corre `Database.Migrate()` y el seed al arrancar. Sin datos los graficos estan vacios:
`dotnet run --project EcoWattCasa.MockDevices -- --backfill 45` escribe 45 dias directo en
Postgres (cubre el ciclo actual y el anterior, que es lo que la comparacion necesita).

## Comandos

```bash
dotnet build                                               # solucion entera (EcoWattCasa.slnx)
dotnet test EcoWattCasa.Tests                              # 246 tests, ~250 ms, sin base ni red
cd ecowatt-frontend && npx ng test --watch=false            # smoke del dashboard (vitest + jsdom)
docker compose --profile mock up -d --build                 # todo en Docker, frontend en :8081

dotnet ef migrations add <Nombre> -p EcoWattCasa.Infrastructure -s EcoWattCasa.Infrastructure -o Persistence/Migrations
```

La migracion usa Infrastructure como startup project (hay un `DesignTimeDbContextFactory`), no
la API. Scalar queda en `/scalar` solo en Development.

## Arquitectura

Clean Architecture, .NET 10. La direccion de dependencias es la regla que no se negocia:

```
Domain  <-  Application  <-  Infrastructure  <-  API
                         <-------------------------
```

- **Domain** — entidades, enums, value objects, interfaces de repositorio. **Cero paquetes
  NuGet**; si algo necesita una dependencia, no va aca.
- **Application** — casos de uso (`DashboardService`, `DeviceService`, `TariffService`,
  `EnergyIngestionService`, `BillImportService`, `AlertService`), DTOs, y la matematica del
  dominio en `Common/` y `Billing/`. No conoce EF, MQTT ni HTTP: `EnergyIngestionService`
  recibe topic + JSON crudo, no un mensaje de MQTTnet.
- **Infrastructure** — EF Core/Npgsql, repositorios, MQTT (broker embebido + listener +
  publisher), rollup horario, extractor de PDF, migraciones, seed.
- **API** — controladores delgados (`=>` a un servicio), hub SignalR, CORS, manejo de errores.
- **MockDevices** — consola: los cinco equipos Athom simulados (el medidor de tablero
  multicanal y los cuatro enchufes) y el modo `--backfill`. El canal del medidor es la suma de
  los enchufes mas una linea de base, asi el consumo no identificado nunca da negativo.

Donde vive la logica que importa:

| Archivo | Que resuelve |
|---|---|
| `Application/Common/BillCalculator.cs` | Reconstruye la factura: tramos, cargo fijo diario, recargos %, cargos fijos, precio marginal y medio |
| `Application/Common/EnergyMath.cs` | kWh de un bucket: delta del contador, con la integracion de watts como respaldo |
| `Application/Common/BillingCycles.cs` | El ciclo real anclado en la ultima lectura de factura, y las ventanas UTC |
| `Application/Alerts/AlertRules.cs` | Las tres reglas de alerta, funcion pura del contexto |
| `Application/Billing/BillParser.cs` | PDF de la cooperativa -> importes y tramos |
| `Infrastructure/Repositories/EnergyHourlySql.cs` | El SQL de agregacion y el rollup, a mano |
| `Application/Common/HouseConsumption.cs` | Total de la casa (sale del medidor, no de la suma) y consumo no identificado |
| `Application/Mqtt/EnergyValue.cs` | Un campo de ENERGY que puede venir escalar o como array por canal |
| `Domain/Entities/RelayGuard.cs` | Si un ON/OFF se puede ejecutar: sin rele, bloqueado, o demasiado seguido |

## Convenciones

**Codigo y comentarios en castellano sin tildes.** Todo el repo es ASCII: comentarios, XML doc,
mensajes de log, excepciones y hasta el texto de la UI (`<h1>Configuracion</h1>`). El unico
archivo con acentos es `README.md`. Mantenelo asi — no "arregles" la ortografia del codigo.

**Los comentarios explican por que, no que.** El estilo del repo es un XML doc en cada tipo
publico que justifica la decision, y comentarios inline solo donde algo parece raro a proposito
(el orden de registro de los hosted services, `date_trunc` a mano, el timestamp del servidor).
Si agregas codigo, segui el mismo criterio; si no hay nada que justificar, no pongas comentario.

**C#:** `sealed` por default en servicios y DTOs; primary constructors para inyeccion
(`public sealed class DeviceService(IDeviceRepository devices, ...)`); `record` para DTOs y
value objects; `Nullable` e `ImplicitUsings` activos; `CancellationToken ct` como ultimo
parametro; `decimal` para pesos y `double` para kWh/watts.

**Angular 22 zoneless, standalone, signals.** No hay zone.js: el estado es `signal()` /
`computed()` y escribirlo ya dispara el render. `ChangeDetectionStrategy.OnPush` en todo
componente, `inject()` en vez de constructor, control flow `@if`/`@for` (nunca `*ngIf`,
`*ngFor`, ni importar `CommonModule`), rutas lazy con `loadComponent` y paths en castellano
(`dispositivo/:id`, `config`). Formateo siempre por los helpers de `shared/format.ts` (es-AR).

**Estilos:** tokens SCSS en `src/styles/_variables.scss`, resueltos via
`stylePreprocessorOptions.includePaths`, asi que cada componente abre con
`@use 'variables' as *`. Nunca hardcodees un color. La app es **solo modo claro** a proposito.

**URLs relativas en el frontend.** `/api/...` y `/hubs/energy` sin host: en dev los resuelve
`proxy.conf.json` y en Docker el nginx del frontend. No introduzcas una base URL.

## Trampas

- **El total de la casa sale del medidor de tablero, nunca de la suma de dispositivos.** Su
  lectura ya incluye a los enchufes: sumar todo cuenta dos veces lo que pasa por un enchufe
  medido. La diferencia se informa como "consumo no identificado". Sin medidor, el total vuelve
  a ser la suma de los enchufes. Todo eso vive en `HouseConsumption`.
- **`HouseConsumption.Appliances` y `Split` tienen que filtrar por `IsActive` igual.** Si uno
  le diera fila a un enchufe desactivado y el otro no lo contara como medido, sus kWh
  apareceran dos veces.
- **Un campo de ENERGY puede venir escalar o array en el mismo payload.** El EM2 tiene dos
  canales de corriente y uno de tension, asi que manda `Power` como array y `Voltage` escalar.
  La forma se decide por campo, no por dispositivo.
- **Todo intento de conmutar un rele se audita, tambien el rechazado.** Un rechazo es 409 y no
  extiende la ventana de tiempo minimo: solo la mueve un comando que efectivamente salio.
- **El estado del rele lo manda el equipo por `stat/<topic>/POWER`**, no se infiere del comando.
  Asi se ve el boton fisico del enchufe y los comandos que se perdieron.
- **Los timestamps los pone el servidor.** El campo `Time` de Tasmota se ignora a proposito
  (un equipo que se reinicia pierde la hora). No lo "arregles".
- **Todo se persiste en UTC (`timestamptz`) y se agrega en hora de Buenos Aires** via
  `HomeTimeZone`. Cualquier corte de dia, mes o ciclo pasa por ahi.
- **El bucket horario va en SQL a mano, no en LINQ.** `date_trunc` sobre `timestamptz` depende
  del `TimeZone` de la sesion de Postgres; el SQL de `EnergyHourlySql` hace
  `AT TIME ZONE 'UTC'` explicito. Si migras esa consulta a LINQ, los buckets se corren.
- **La lectura horaria combina dos fuentes:** `energy_hourly` para las horas ya consolidadas y
  las crudas para las que el rollup no proceso. El rollup no pisa horas hechas y la poda solo
  borra lecturas cuya hora ya quedo guardada — mantene ese orden.
- **Orden de los hosted services en `Infrastructure/DependencyInjection.cs`:** el broker
  embebido se registra antes del listener, porque el listener necesita el puerto escuchando.
- **Columnas en snake_case** por `UseSnakeCaseNamingConvention()`; no escribas `HasColumnName`.
- **Los enums viajan como texto** al frontend (`JsonStringEnumConverter`).
- **Las alertas se calculan al pedirlas**, no se persisten.
- **`DeviceValidationException` -> 400** lo traduce `ValidationExceptionHandler`; los
  controladores no llevan try/catch.

## Tests

xUnit, y son **puros**: ni base, ni broker, ni reloj. El caso de referencia son cinco facturas
reales transcriptas en `Billing/RealBills.cs`, y `BillGoldenTests` las reconstruye con un peso
de tolerancia. El parser se valida por doble entrada: lo que extrae de los fixtures
(`Fixtures/Bills/factura-AAAA-MM.txt`, texto literal de PdfPig, versionados y copiados al
output) contra los mismos numeros transcriptos a mano.

Si cambias `BillCalculator`, `EnergyMath`, `BillingCycles` o `AlertRules`, hay un test que
cubre el caso de borde — corre la suite antes de dar algo por bueno. Un cambio de dominio nuevo
merece su test en el proyecto que ya existe para eso, armando el contexto entero a mano.

## Fuera de alcance

Sin autenticacion (uso local en una LAN), sin multi-hogar, sin gas ni agua, sin predicciones.
El broker corre sin credenciales porque solo escucha en la red de casa.
