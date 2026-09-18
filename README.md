# EcoWatt Casa

Monitoreo del consumo eléctrico del hogar, en Venado Tuerto. Un medidor Athom EM2 en el tablero
mide el total de la casa y cuatro enchufes Athom miden aparato por aparato; todos con Tasmota,
publicando por MQTT. La API persiste en PostgreSQL y el dashboard Angular muestra kWh y costo en
pesos por día y por ciclo de facturación, con la tarifa por tramos de la cooperativa.

Stack: .NET 10 (Clean Architecture) · Angular 22 · PostgreSQL 17 · MQTT (broker embebido) · SignalR · ECharts.

---

## Arranque rápido (con el mock, sin hardware)

```bash
# 1. Base de datos
docker compose up -d postgres

# 2. API: migra, siembra, y levanta su propio broker MQTT en el puerto 1883
dotnet run --project EcoWattCasa.API            # http://localhost:5080

# 3. Los cinco equipos simulados: publican cada 10 s por MQTT
dotnet run --project EcoWattCasa.MockDevices

# 4. Frontend
cd ecowatt-frontend && npm start                # http://localhost:4200
```

No hace falta instalar Mosquitto: la API trae un **broker MQTT embebido** que escucha en
`0.0.0.0:1883` y arranca y para con ella. Los equipos se conectan ahí igual que a un broker
externo. Para usar uno propio, poné `"Mqtt:Embedded": false` y apuntá `Mqtt:Host` a su IP.

Para tener los gráficos con datos desde el primer minuto, generá historia antes de levantar el
resto (45 días cubre el ciclo actual y el anterior, así la comparación tiene con qué comparar):

```bash
dotnet run --project EcoWattCasa.MockDevices -- --backfill 45
```

Y cargá tus facturas reales desde **Configuración → Importar factura en PDF**: de ahí salen los
tramos, los impuestos y las fechas reales del ciclo de lectura.

Todo en Docker (incluido el mock):

```bash
docker compose --profile mock up -d --build     # frontend en http://localhost:8081
```

### Sin Docker

`docker compose` necesita WSL2 con una distribución instalada (`wsl --install`). Si no está, lo
único que hay que resolver aparte es la base: el broker ya viene adentro de la API.

**PostgreSQL local:** creá la base y el rol que espera `appsettings.json` (como superusuario
`postgres`):

```sql
CREATE ROLE ecowatt LOGIN PASSWORD 'ecowatt' CREATEDB;
CREATE DATABASE ecowatt OWNER ecowatt;
```

La API aplica las migraciones sola al arrancar.

---

## Estructura

| Proyecto | Qué tiene |
|---|---|
| `EcoWattCasa.Domain` | Entidades (`Device`, `EnergyReading`, `TariffConfig`), enums, interfaces de repositorio. Sin dependencias. |
| `EcoWattCasa.Application` | Casos de uso (`DashboardService`, `DeviceService`, `TariffService`, `EnergyIngestionService`), DTOs, contrato del JSON de Tasmota, cálculo de energía y costo. |
| `EcoWattCasa.Infrastructure` | EF Core + PostgreSQL, repositorios, listener MQTT (`BackgroundService`), publisher de comandos, migraciones, seed. |
| `EcoWattCasa.API` | Controladores REST, hub SignalR, CORS, OpenAPI (Scalar en `/scalar`). |
| `EcoWattCasa.MockDevices` | Consola: los cinco equipos simulados (calibrados contra las facturas reales, ~247 W promedio) + modo `--backfill`. El canal del medidor es la suma de los enchufes más una línea de base, así el consumo no identificado nunca da negativo. |
| `ecowatt-frontend` | Angular 22 standalone/zoneless con signals, ECharts, cliente SignalR. Sistema visual portado del dashboard de la Ticketera. |

---

## Sistema visual

El look sale del dashboard de la Ticketera (`TicketOnlineAngular`), para que las dos apps se
vean de la misma familia. Lo que se porta:

- **Tokens SCSS** en `src/styles/_variables.scss`: azules de marca (`$blue-600 #0088d4`),
  superficies claras sobre `$bg-page #eef3f8`, textos `$text-strong/soft/faint`, bordes
  translúcidos derivados del azul, radios, sombras con tinte azul y duraciones de transición.
  Se resuelven con `stylePreprocessorOptions.includePaths: ["src/styles"]`, así cualquier
  componente hace `@use 'variables' as *`.
- **Manrope** (variable 200..800) desde Google Fonts, con `preconnect`.
- **Patrones**: cards con borde azul translúcido que levantan en hover, cifras en peso 800 con
  tracking negativo, botones con `$grad-primary`, headers de tabla en mayúsculas sobre lavado
  azul, y el bloque de `prefers-reduced-motion` de WCAG 2.3.3.

Dos cosas que **no** se copiaron, a propósito:

- **Modo oscuro:** el dashboard de la Ticketera es solo claro, así que este también. Los tokens
  de la variante oscura que había antes se sacaron en vez de dejarlos muertos.
- **ApexCharts:** la Ticketera lo usa; acá se queda ECharts, que ya está andando y rinde mejor
  con series largas. Lo que sí se igualó es el aspecto: color de serie `$blue-600` (pasa los
  seis chequeos de la paleta de dataviz sobre blanco), Manrope en ejes y tooltips, y tooltips
  con el radio y la sombra del sistema.

---

## Endpoints

| Método | Ruta | Qué devuelve |
|---|---|---|
| GET | `/api/devices` | Dispositivos con su potencia actual |
| POST | `/api/devices` | Registra un dispositivo |
| PUT | `/api/devices/{id}` | Edita un dispositivo |
| DELETE | `/api/devices/{id}` | Borra el dispositivo y sus lecturas |
| GET | `/api/devices/{id}/readings?from=&to=&maxPoints=` | Lecturas crudas |
| GET | `/api/devices/{id}/history?hours=24` | Historial agregado con costo (≤48 h por hora, más por día) |
| POST | `/api/devices/{id}/power` | `{ "on": true }` → publica `cmnd/{topic}/POWER`. `409` si la guarda del relé lo rechaza, `503` si el broker no está |
| GET | `/api/devices/{id}/relay-history` | Intentos de conmutación, incluidos los rechazados |
| GET | `/api/dashboard/daily?date=` | Consumo y costo del día por dispositivo + curva horaria |
| GET | `/api/dashboard/period?cycle=&offset=` | La factura del período reconstruida, con desglose por dispositivo |
| GET | `/api/dashboard/cycle` | Dónde está anclado el ciclo y de qué dato salió |
| GET | `/api/tariff` · `/api/tariff/history` | Cuadro tarifario vigente / serie completa |
| PUT | `/api/tariff` | Carga un cuadro nuevo con su vigencia |
| POST | `/api/tariff/import` | Sube el PDF de la factura y carga tarifas + comprobante |
| GET | `/api/tariff/bills` | Facturas reales ya importadas |
| GET | `/api/alerts` | Alertas vigentes: cruce de tramo, dispositivos mudos, proyección alta |
| — | `/hubs/energy` | Hub SignalR: eventos `readingReceived`, `deviceRegistered`, `relayStateChanged` |

---

## Decisiones que conviene conocer

**La energía sale del contador del medidor, no de integrar watts.** Tasmota publica
`ENERGY.Total` (kWh acumulados del propio POW R2). La energía de cada hora es la diferencia
entre la primera y la última lectura de ese contador: es energía real y no se desvía si se
pierden mensajes MQTT. Los deltas horarios son aditivos, así que el día y el mes se suman a
partir de ellos (y un contador reseteado ensucia una hora, no el mes entero). La integración `watts/1000 × horas` queda como respaldo, para el ESP32 con
SCT-013 que no lleva acumulado, o si el contador se resetea.

**Los timestamps los pone el servidor.** Un equipo que se reinicia pierde la hora, y un reloj
corrido rompe el orden de las series. El campo `Time` de Tasmota se ignora a propósito.

**Todo se guarda en UTC (`timestamptz`) y se agrega en hora de Buenos Aires.** Las ventanas
de día y de mes se calculan con `TimeZoneInfo`, así "el consumo de hoy" corta a la medianoche
local. El bucket horario se hace en SQL con `date_trunc('hour', ts AT TIME ZONE 'UTC')`, en
vez de LINQ, porque `date_trunc` sobre `timestamptz` depende del `TimeZone` de la sesión de
Postgres y eso corre los buckets sin avisar.

**La tarifa es por tramos, no un precio plano.** El cuadro tarifario modela la factura real de
la Cooperativa de Venado Tuerto (categoría *Res Sin Sub VT*): cargo fijo **por día**, cargos
variables por tramo de consumo (0–75, 75–150, 150–300 kWh), recargos porcentuales sobre el
importe básico y cargos de monto fijo por período. En la factura 09/2026 eso da:

| Concepto | Cálculo | Importe |
|---|---|---|
| Cargo fijo | 29 días × $122,03 | $3.539,01 |
| Tramos 1–3 | 75 + 75 + 25 kWh | $47.014,00 |
| **Importe básico** | | **$50.553,01** |
| IVA 21% + Ley 10014 6% + Cap.Rem 2,85% + Cap.Inv 12,15% | 42% del básico | $21.232,31 |
| Alumbrado público + Ley 12692 | montos fijos | $7.209,81 |
| **Total** | | **$78.995,13** |

Los cuatro recargos porcentuales dan exactamente 42,00% del básico en las cinco facturas
verificadas. El precio medio real es **$451/kWh** y el marginal (tramo 3) **$503/kWh**.

**El costo por dispositivo es su parte del costo de energía, prorrateado por kWh.** Con tramos,
el mismo kWh vale $243 o $354 según dónde caiga, así que no existe "lo que cuesta la heladera"
sin convención. La que usa el dashboard: el costo de energía del período (tramos + sus
impuestos) se reparte entre los dispositivos en proporción a sus kWh, y **la suma de las cards
más los cargos fijos da exactamente el total de la factura**. Los cargos fijos no se le imputan
a ningún aparato: se pagan aunque no consumas nada. Aparte, el dashboard muestra el **precio
marginal** — lo que cuesta el próximo kWh — que es el número accionable para decidir qué apagar.

**El costo de cada día se asigna cronológicamente.** Los primeros kWh del ciclo caen en el tramo
barato y los últimos en el caro, así que el gráfico de costo por día muestra el escalón real al
cambiar de tramo. La suma de los días da el costo de energía del período, no una aproximación.

**Las tarifas son un historial, no un valor.** Guardar un cuadro nuevo inserta una fila con su
`valid_from`; los períodos ya costeados conservan su precio. Un período con aumento a mitad de
camino se costea día por día, y los kWh de cada tramo se reparten entre los precios en
proporción a los días — que es exactamente lo que hace la factura.

**El ciclo de facturación no es el mes calendario ni cae siempre el mismo día.** En las facturas
reales el medidor se leyó el 05/03, 04/04, 04/05, 02/06, 02/07 y 31/07: la fecha se corre y el
ciclo dura 29 o 30 días. Por eso el ciclo se ancla en la última lectura real (la que trae la
factura importada) y avanza de a 30 días; cada factura nueva vuelve a anclar, así que el desvío
no se acumula. El dashboard tiene las dos vistas, ciclo y mes calendario.

**Importar el PDF de la factura carga la tarifa sola.** `POST /api/tariff/import` lee el PDF de
la cooperativa, guarda el comprobante y deduce los cuadros tarifarios con su fecha de vigencia
exacta (que sale del reparto de días entre precios). Después vuelve a armar la factura con lo
que guardó y reporta cuánto difiere del total impreso: sobre las cinco facturas de prueba la
diferencia máxima fue **$0,67 sobre $82.180** (0,0008%), que es el redondeo a pesos enteros que
hace la cooperativa en dos conceptos.

**El delta del contador se toma en orden temporal, no como MAX-MIN.** Si el medidor volvió a
cero dentro de la ventana (equipo reflasheado, `EnergyReset`, o el mock reiniciado), el delta
sale negativo y ese bucket cae a la integración en vez de facturar el salto como consumo. Un
segundo control descarta deltas que superen lo que físicamente cabe en la ventana (30 kW).

**El "promedio" de la curva de la casa son watts de la casa, no promedio entre dispositivos.**
Se suman las potencias medias de cada dispositivo por hora; al plegar a día se divide por la
cantidad de horas. La curva de la casa no muestra pico: el máximo del total no se puede derivar
de los máximos por dispositivo, porque los picos no tienen por qué coincidir en el tiempo. En la
vista de un dispositivo el pico sí es exacto.

**El consumo viejo vive consolidado por hora, no lectura por lectura.** Con tres enchufes a
`TelePeriod 10` entran 25.920 filas por día: en un año la tabla llega a ~9,5 M y consultar un
mes obliga a escanear cientos de miles de filas cada vez que el dashboard refresca. Un servicio
en segundo plano consolida cada hora cerrada en `energy_hourly` —una fila por hora y
dispositivo, ~26.000 al año— y después borra las lecturas crudas anteriores a la retención.

La lectura combina las dos fuentes: las horas ya consolidadas salen de `energy_hourly` y las que
el rollup todavía no procesó (típicamente la hora en curso) se agregan al vuelo desde las
crudas. El orden importa: **consolidar no pisa horas ya hechas, y el borrado solo toca lecturas
cuya hora ya quedó guardada**, así que un rollup que falla no se lleva datos puestos.

Medido sobre 45 días de datos simulados: la vista mensual pasó de **77 ms escaneando 117.876
filas** a **39 ms**, de los cuales el rollup aporta 0,25 ms para 1.908 horas. Lo que queda es el
scan de las crudas del rango — ahora acotado por la retención en vez de crecer sin techo. Si
alguna vez molesta, la perilla es bajar `Rollup:RawRetentionDays`.

Verificado comparando las dos fuentes hora por hora: de 1.263 horas donde todavía conviven,
**1.260 coinciden exacto**; las 3 restantes son la única hora que cruza el corte de retención,
donde el rollup tiene las 60 muestras completas y quedan 6 crudas sueltas. La lectura usa el
rollup, así que el valor es el correcto.

**Las alertas se calculan al pedirlas, no se guardan.** Son una lectura del estado actual, no
un historial: así no hay que resolver acuses de recibo ni alertas rancias. Hay tres reglas:

- **Cruce de tramo.** Es la que justifica la función: con tarifa por tramos, pasar los 150 kWh
  sube el kWh marginal de ~$376 a ~$503. La alerta proyecta el consumo del ciclo, estima la
  fecha del cruce y dice entre qué precios salta. No avisa de cruzar el último tope cargado
  (300 kWh): ninguna factura llegó a ese tramo, así que no hay precio para el kWh 301 y avisar
  sería inventarlo.
- **Dispositivo mudo.** Un enchufe que deja de reportar no se nota mirando el dashboard —los
  totales simplemente quedan bajos—, así que el silencio es una alerta explícita. Si callaron
  *todos*, el problema no es de un aparato: sale una sola alerta crítica apuntando al broker.
- **Proyección por encima del ciclo anterior**, con umbral configurable.

Las reglas son una función pura del estado (`AlertRules`): reciben el contexto ya armado, sin
base ni reloj, y por eso cada caso tiene su test.

**Un topic desconocido se auto-registra**, siempre como enchufe. Si llega telemetría de
`tele/plug-nuevo/SENSOR` sin dispositivo en la base, se crea solo (y se avisa por SignalR):
enchufar un equipo nuevo lo hace aparecer en el dashboard, y después se le corrige el nombre
desde Configuración.

Se registra como aparato aunque el payload venga de un medidor de tablero, y eso es
deliberado: uno marcado por error como medidor **corrompe el total de la casa**, mientras que
uno marcado de más como aparato sólo sobra en el desglose. El rol se corrige a mano.

---

## El hardware

Cinco equipos Athom, todos con Tasmota de fábrica y hablando MQTT. No hay más de un formato de
payload en el sistema: **todo es Tasmota**.

| Equipo | Qué es | Dónde va | Relé |
|---|---|---|---|
| 1× **Athom EM2** "2 CH Energy Meter" | ESP32‑C3 de riel DIN, 1 canal de tensión y 2 de corriente | Tablero principal, sobre la acometida | No |
| 4× **Athom Plug V3** (PG05V3‑AU16A‑TAS) | Enchufe con medición y relé, ficha AU compatible con IRAM 2073 | Heladera, PC, lavarropas, uno libre | Sí |

Instalación monofásica 220 V / 50 Hz. El tablero principal y el sub‑tablero están en lugares
distintos, así que **no se mide por circuito**: se mide el total de la casa y algunos aparatos.

### Por qué el medidor de tablero

La tarifa es por tramos sobre el consumo de **toda la casa**. Midiendo sólo enchufes se ve
alrededor de la mitad del consumo, y una alerta de "vas a cruzar los 150 kWh" calculada sobre
media casa no sirve para nada. El EM2 aporta el total real; los enchufes explican de dónde sale.

El EM2 se compró con **una sola pinza CT**, así que su segundo canal reporta cero. El sistema
lo soporta igual: cada dispositivo tiene un `ChannelIndex` configurable.

### Pendiente: el cuarto enchufe

`plug-libre` está dado de alta, mide y aparece en el dashboard, pero todavía no tiene nada
conectado: figura en "Sin asignar" y reporta 0 W. Queda así **a propósito**, hasta decidir qué
conviene medir con él.

Para asignarlo no hace falta tocar código: en Configuración se le cambia el nombre, la
ubicación y la potencia nominal. El `Topic` de Tasmota puede quedar como está — lo que se ve en
pantalla es el nombre, no el topic.

Candidatos razonables, por lo que aportarían al desglose: termotanque eléctrico si lo hubiera,
aire acondicionado, microondas, o el televisor. Conviene elegir algo que hoy esté cayendo dentro
del consumo no identificado y que valga la pena separar.

### Consumo no identificado

Como el medidor mide la acometida, su lectura **ya incluye** a los enchufes. El total de la
casa sale del medidor, nunca de la suma de dispositivos, y la diferencia se muestra como
**consumo no identificado**: luces, termotanque, lo que esté enchufado en cualquier otro lado.
Es lo que hace que el desglose sume exactamente el total.

Si los enchufes llegaran a medir más que el tablero, el dashboard lo avisa: es físicamente
imposible, y significa que hay un canal mal configurado o un enchufe colgado de un circuito que
el medidor no ve.

---

## Puesta en marcha de cada equipo

Los equipos vienen con Tasmota instalado. Lo único que hay que hacer es darles red y decirles
a qué broker hablar.

### 1. Conectarlo a la WiFi

Al encenderlo por primera vez levanta un access point propio llamado **`tasmota-XXXX`**.

1. Conectarse a esa red desde el celular o la notebook.
2. Se abre sola la pantalla de configuración (si no, ir a `192.168.4.1`).
3. Cargar el SSID y la contraseña de la WiFi de casa y guardar.
4. El equipo se reinicia y se conecta. Su nueva IP aparece en el router.

> La WiFi de casa tiene que ser **2,4 GHz**: el ESP32‑C3 no ve las redes de 5 GHz. Si el router
> publica una sola red con las dos bandas, puede hacer falta separarlas temporalmente.

### 2. Apuntarlo al broker

Desde la consola de Tasmota (**Consola** en su página web), una sola línea:

```
Backlog MqttHost 192.168.0.27; MqttPort 1883; Topic plug-heladera; TelePeriod 30
```

- **`MqttHost`** — la IP de la PC donde corre la API. Verificala con `ipconfig` y fijala por
  DHCP en el router, porque si cambia los equipos dejan de reportar.
- **`Topic`** — el nombre del dispositivo, sin los prefijos `tele/`, `stat/` ni `cmnd/`. Tiene
  que coincidir con el que figura en Configuración. Los que siembra la base son
  `em2-tablero`, `plug-heladera`, `plug-pc`, `plug-lavarropas` y `plug-libre`.
- **`TelePeriod`** — cada cuántos segundos publica la telemetría. Viene de fábrica en **300**
  (5 minutos), demasiado espaciado para ver un electrodoméstico prenderse.

**Sobre TelePeriod: 30 s, no 10.** El mínimo que acepta Tasmota es 10 s, pero con 5 equipos son
43.200 filas por día — unas 900.000 en la ventana de retención de 21 días. Con 30 s son 14.400
por día y no se pierde nada importante: la resolución de los gráficos es horaria y el consumo se
calcula por diferencia del contador acumulado, no integrando la potencia, así que muestrear más
seguido no mejora la precisión de los kWh. Bajalo a 10 sólo si querés ver el pico de arranque de
un motor.

### 3. Sólo en el EM2: partir la energía por canal

```
Backlog EnergyCols 2; SetOption129 1
```

**`SetOption129 1`** es el que importa: hace que Tasmota publique la energía **por canal**, como
arrays (`"Power":[612,0]`), en vez de un único número con los canales sumados. Sin esto no hay
forma de distinguir los dos canales de corriente. `EnergyCols 2` sólo acomoda la tabla de la
página web del equipo, no cambia el MQTT.

El parser acepta las dos formas por campo, así que un payload mezclado —que es el esperado del
EM2, con `Voltage` escalar y `Power` como array— se lee bien.

### 4. Verificar

En el dashboard el equipo aparece solo en cuanto llega su primer mensaje. Si no aparece:

```bash
# Escuchar todo lo que entra al broker
mosquitto_sub -h 192.168.0.27 -t '#' -v
```

El broker embebido escucha en todas las interfaces, así que los equipos de la LAN llegan sin
configuración extra. Lo que sí hay que abrir es el **puerto 1883 en el firewall de Windows**
para la red privada.

### 5. Cortar el mock

```bash
docker compose stop mock      # o Ctrl+C si corre suelto
```

---

## El relé

Los cuatro enchufes tienen relé; el EM2 no. Un ON/OFF pasa por tres controles antes de llegar
al equipo, y **todo intento queda registrado**, se haya ejecutado o no.

| Control | Qué frena | Respuesta |
|---|---|---|
| El modelo no tiene relé | Cualquier comando al EM2 | `409 BlockedNoRelay` |
| `RelayLocked` | Nada puede apagarlo: ni el dashboard ni una automatización | `409 BlockedLocked` |
| `MinRelayIntervalSeconds` | Dos conmutaciones demasiado seguidas | `409 BlockedTooSoon` |

**La heladera viene con el relé bloqueado y un mínimo de 10 minutos.** Un corte por error
arruina la comida, y el ciclado corto castiga al compresor. Se desbloquea desde Configuración,
a mano y a sabiendas.

Un rechazo es `409`, no `400` ni `500`: el pedido está bien formado, pero el estado del
dispositivo no admite conmutarlo ahora. El motivo viaja en texto y se muestra tal cual.

El estado que se ve en pantalla es el que **confirmó el equipo** por `stat/<topic>/POWER`, no
el que se le pidió. Así, si alguien aprieta el botón físico del enchufe o si un comando se
pierde, la pantalla muestra la realidad.

---

## MQTT

| Topic | Dirección | Qué lleva |
|---|---|---|
| `tele/<topic>/SENSOR` | del equipo | Telemetría de energía. `ENERGY.Total` es la fuente de verdad del consumo; `ENERGY.Power` es potencia **real** (activa), no aparente |
| `stat/<topic>/POWER` | del equipo | Estado del relé confirmado: `ON` / `OFF` |
| `cmnd/<topic>/POWER` | al equipo | Comando de encendido |

`stat/<topic>/RESULT` trae el mismo cambio en JSON y se ignora a propósito, para no aplicar el
mismo estado dos veces.

El campo `Time` que manda Tasmota **se ignora**: un equipo que se reinicia pierde la hora. El
timestamp lo pone el servidor.

### Broker

La API trae un **broker MQTT embebido** y es el camino por defecto: no hace falta instalar
Mosquitto y hay una pieza menos que se puede caer.

Si preferís un Mosquitto aparte:

```bash
docker compose --profile mosquitto up -d
```

y en ese caso hay que poner `Mqtt:Embedded` en `false` **y** sacarle a la API el mapeo del
puerto `1883` en `docker-compose.yml`, porque los dos servicios lo publican y chocan.

---

## Base de datos

```
devices          (id, name, mqtt_topic UNIQUE, location, nominal_watts, type, role, channel_index,
                  relay_locked, min_relay_interval_seconds, relay_on, relay_state_at,
                  is_active, created_at)
relay_commands   (id, device_id FK, requested_on, source, outcome, reason, created_at)
energy_readings  (id, device_id FK, timestamp, watts, voltage, amperage,
                  total_kwh, today_kwh, power_factor, created_at)
energy_hourly    (device_id FK, hour_utc, first_total_kwh, last_total_kwh, avg_watts,
                  max_watts, sample_count, first_timestamp, last_timestamp, rolled_up_at)
                  PK (device_id, hour_utc)
tariff_schedules (id, valid_from UNIQUE, fixed_charge_per_day, source, created_at)
tariff_blocks    (id, tariff_schedule_id FK, order, label, up_to_kwh, price_per_kwh)
tariff_surcharges     (id, tariff_schedule_id FK, name, rate)      -- % sobre el básico
tariff_period_charges (id, tariff_schedule_id FK, name, amount)    -- monto fijo por período
imported_bills   (id, invoice_number UNIQUE, period, reading_from, reading_to, days, kwh,
                  meter_start, meter_end, basic_amount, total_taxes, total, imported_at)
```

Índices: `ix_energy_readings_device_timestamp` (historial), `ix_energy_readings_timestamp`
(agregados del dashboard), `ix_devices_mqtt_topic` (resolución del topic en cada mensaje).

Migraciones:

```bash
dotnet ef migrations add <Nombre> -p EcoWattCasa.Infrastructure -s EcoWattCasa.Infrastructure -o Persistence/Migrations
```

La API corre `Database.Migrate()` al arrancar, así que no hace falta aplicarlas a mano.

---

## Tests

```bash
dotnet test EcoWattCasa.Tests                    # 246 tests, sin base ni broker
cd ecowatt-frontend && npx ng test --watch=false  # smoke del dashboard (vitest + jsdom)
```

**El caso de referencia son tus cinco facturas reales.** `RealBills.cs` las tiene cargadas con
sus fechas de lectura, kWh, importe básico y total impreso, más los cuadros tarifarios
deducidos del reparto de días. `BillGoldenTests` reconstruye cada una y compara contra el papel
con **un peso de tolerancia** — lo que queda de diferencia no es error del cálculo, es que la
cooperativa redondea a pesos enteros Cap.Rem.L.B.T. y Cap.Inv.Bienes de Uso.

| Archivo | Qué cubre |
|---|---|
| `Billing/BillGoldenTests` | Las 5 facturas reproducen su total, su importe básico y su desglose por tramos |
| `Billing/BillAllocationTests` | Los dos repartos cierran: cards + cargos fijos = factura, y suma de días = costo de energía |
| `Billing/BillParserTests` | Las 5 facturas leídas del PDF real: columnas invertidas, importes pegados a su etiqueta, impuestos porcentuales vs. montos fijos, y el círculo completo PDF → cuadros → factura recalculada |
| `Billing/TariffScheduleFactoryTests` | Casos de borde de la deducción de cuadros: días que no cierran, factura sin detalle, importe básico en cero |
| `Common/EnergyMathTests` | Contador reseteado, delta imposible, sin acumulado, una sola muestra |
| `Common/BillingCyclesTests` | Ciclo anclado en la última lectura, avance de 30 días, ventanas UTC de un día local |
| `Alerts/AlertRulesTests` | Cruce de tramo con los precios reales y su fecha estimada, dispositivo mudo vs. broker caído, umbrales configurables |

La suite se verificó con **mutation testing**: revertir el reparto de tramos a kWh decimales
tumba 6 tests (las tres facturas con aumento a mitad de período) y sacar la guarda del contador
reseteado tumba el suyo. No son tests que pasen por casualidad.

El smoke test del frontend monta el dashboard con la API mockeada y verifica el desglose de la
factura, el reparto energía/cargos fijos, el precio marginal y el formato en es-AR. Los gráficos
no se ejercitan ahí: jsdom no tiene canvas, así que el test fuerza la vista de tabla.

### Fixtures del parser

`Fixtures/Bills/factura-AAAA-MM.txt` es la salida literal de PdfPig sobre cada una de las cinco
facturas. Van versionadas para que los tests no dependan de tener los PDF en disco.

El parser se valida por **doble entrada**: lo que extrae de cada PDF se compara contra los
mismos números transcriptos a mano en `RealBills.cs`. Dos caminos independientes — el extractor
y mi lectura del papel — tienen que coincidir. Además hay dos controles de consistencia que no
dependen de la transcripción:

- Los impuestos leídos suman el subtotal impreso en cada factura.
- El precio nuevo de una factura es el viejo de la siguiente (la serie encadena mes a mes).

Para regenerar un fixture si cambia el formato del PDF, extraé el texto con
`UglyToad.PdfPig` + `ContentOrderTextExtractor` y guardalo tal cual.

---

## Configuración

`EcoWattCasa.API/appsettings.json`, o variables de entorno con doble guion bajo
(`Mqtt__Host`, `ConnectionStrings__Postgres`):

| Clave | Default | Para qué |
|---|---|---|
| `ConnectionStrings:Postgres` | `Host=localhost;...;Database=ecowatt` | Base |
| `Mqtt:Enabled` | `true` | `false` levanta la API sin listener MQTT |
| `Mqtt:Embedded` | `true` | La API levanta su propio broker; `false` usa uno externo |
| `Mqtt:Host` / `Mqtt:Port` | `localhost` / `1883` | Broker |
| `Mqtt:Username` / `Password` | vacío | Si se cargan, el broker embebido las exige |
| `Mqtt:TelemetryTopicFilter` | `tele/+/SENSOR` | Qué escucha |
| `EcoWatt:TimeZone` | `America/Argentina/Buenos_Aires` | Corte de día y de ciclo |
| `Rollup:Enabled` | `true` | Consolidación horaria y poda de lecturas crudas |
| `Rollup:IntervalMinutes` | `15` | Cada cuánto consolidar |
| `Rollup:RawRetentionDays` | `21` | Días de lecturas crudas que se conservan |
| `Alerts:Enabled` | `true` | Cálculo de alertas |
| `Alerts:SilenceMinutes` | `10` | Minutos sin reportar para considerar mudo un dispositivo |
| `Alerts:OverrunRatio` | `0.15` | Cuánto debe superar la proyección al ciclo anterior para avisar |
| `Cors:AllowedOrigins` | `http://localhost:4200` | Origen del frontend |

---

## Lo que no está en esta versión

Autenticación (uso local), multi-hogar, gas y agua, predicciones. El broker corre sin
autenticación porque escucha en la LAN: si alguna vez se expone fuera de la red de casa,
hay que poner `password_file` y `allow_anonymous false` en `docker/mosquitto/config/mosquitto.conf`.
