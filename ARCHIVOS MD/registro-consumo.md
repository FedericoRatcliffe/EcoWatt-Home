# Registro de consumo

Bitacora de lo que se va midiendo en casa con el hardware real: experimentos, hallazgos y
correcciones. Cada entrada con fecha. Los numeros salen de la base (`energy_readings`), en
hora de Buenos Aires.

Para pasar watts a plata se usa el precio marginal del kWh al momento de la entrada
(~$353 en octubre 2026). Un watt fijo todo el mes son 0,72 kWh.

## Que mide cada equipo

Relevado el 30/10/2026. "Siempre" es lo que queda enchufado de base; "a veces" son cosas que
entran y salen, asi que un salto en esas zapatillas puede ser solo un cargador.

| Equipo | Siempre | A veces |
|---|---|---|
| Medidor de tablero (EM2) | La casa entera | |
| Zapatilla Fede | La PC y la tele, que se usa como monitor de la PC | Cargador de la notebook, cargador del celu |
| Zapatilla Andy 1 | Impresora, DVR de las camaras con su monitor, estabilizador de tension (alimenta el monitor y el cargador), cargador de notebook, 2 lamparas, tele y home theater | |
| Zapatilla Paula | PC, monitor/tele, home theater y 1 lampara | |
| Heladera (desde el 03/11/2026) | La heladera sola, enchufada directo a la pared | |
| ~~Zapatilla Fede Habitacion~~ (hasta el 03/11/2026) | La tele de la habitacion | Cargadores u otra cosa que haga falta |

- **El enchufe de la habitacion paso a la heladera el 03/11/2026 a las 19:44.** La habitacion
  gastaba 0 W en standby y ~0,07 kWh las noches de uso: ya estaba medida. Se le cambio el
  topic a `plug-heladera`, se dio de alta "Heladera" con el rele bloqueado y "Zapatilla Fede
  Habitacion" quedo desactivada, con su historial guardado.

- **Zapatilla Andy 1** es una zapatilla de dos tomas con otra zapatilla enchufada en una de
  ellas: todo lo que cuelga de las dos pasa por el mismo enchufe medido, y el techo de
  2.200 W de los enchufes de 10A aplica a la suma.
- **Los monitores de estudio Adam Audio T8V van directo a la pared**, fuera de cualquier
  enchufe medido (desde el 30/10/2026).

Lo que no pasa por ningun enchufe medido (router, luces, cocina, los T8V, y la heladera hasta
el 03/11) aparece como "no identificado": la diferencia entre el medidor y la suma de los
enchufes.

---

## 29/10/2026 - Arranque de cero con el hardware real

Se borro el historial simulado y quedaron midiendo los cinco equipos desde las 21:00.

- **PowerDelta en 110 en los cinco equipos.** Es el umbral de "publicar ante un cambio de
  10 W". Con carga estable no se notaba, pero la PC oscila entre 73 y 116 W y su zapatilla
  publicaba cada 1 o 2 segundos (1.893 lecturas en 3,4 h contra ~430 de las demas). Los kWh
  estaban bien porque salen del contador, pero multiplicaba las filas. Se paso a
  `PowerDelta 0` en los cinco el 30/10 a las 00:43; desde ahi todos reportan cada 30 s.
- **Alerta falsa de "Vas arriba del ciclo anterior".** El ciclo nuevo arranco el 30/10 y el
  anterior habia quedado con 3,4 h medidas, o sea casi solo cargos fijos. Se corrigio en el
  codigo: la proyeccion pide 24 h medidas y la comparacion, el 90% del ciclo anterior.

## 30/10/2026 - Consumo base de la noche

**Condiciones:** de 03:02 a 08:34. La PC quedo prendida con la tele apagada. En la Zapatilla
Fede Habitacion quedaron cargando el celu y la notebook, con la tele prendida un rato. En las
demas, nadie usando nada. Sin huecos en la telemetria.

**Tramo tranquilo, de 04:30 a 06:15:**

| Equipo | Consumo | Por mes, si quedara asi |
|---|---|---|
| **Casa entera** | **148 W** (minimo 110) | **~107 kWh, ~$38.000** |
| Zapatilla Andy 1 | 49 W fijos | 35 kWh, ~$12.500 |
| Zapatilla Fede (la PC) | 32 W | 23 kWh, ~$8.300 |
| Zapatilla Paula | 7 W | 5 kWh, ~$1.800 |
| Zapatilla Fede Habitacion | 2 W | ~1 kWh |
| No identificado (heladera, router) | ~58 W | 42 kWh, ~$15.000 |

**Hallazgos:**

- **La base de la casa son ~107 kWh por mes sin usar nada**, casi tres cuartos de los 150 kWh
  del tramo. De eso, ~90 W pasan por los enchufes medidos y ~58 W son heladera y router.
- **La PC en reposo gasta ~32 W.** A las 03:02, recien apagada la tele, estaba en ~80 W; a
  las 03:04 bajo a 40 W y desde las 03:27 se quedo en 30-33 W, con picos aislados de 48 W
  (tareas de fondo). La Zapatilla Fede no tiene monitores de estudio conectados todavia: los
  ~80 W eran la PC en uso y los ~32 W la PC en reposo. Dejarla prendida 24/7 para la API
  cuesta unos $8.000 por mes; una Raspberry Pi gastaria unos $1.000.
- **Zapatilla Andy 1: 49 W fijos toda la noche, sin un solo cambio.** Algo queda prendido sin
  usarse. Es el standby mas grande de la casa, mas que la PC. A las 06:43 subio a ~128 W:
  alguien la empezo a usar. Con el relevamiento del 30/10 se explica: el DVR de las camaras
  anda 24/7 a proposito, y el resto (impresora, cargador de notebook siempre enchufado, tele
  y home theater) suma standby. Falta separar cuanto es cada uno.
- **Zapatilla Fede Habitacion:** 70-90 W con la tele hasta las 03:22, ~25 W hasta las 04:27
  (la notebook terminando de cargar), y despues ~2 W. Toda la noche fueron 0,085 kWh.
- **Zapatilla Paula:** 7 W fijos de standby toda la noche. Con PC, monitor/tele, home
  theater y lampara enchufados, es el standby de equipos apagados: la PC de Paula no queda
  prendida de noche.
- **06:40, pico de 1,38 kW durante 1,5 min fuera de los enchufes.** Es normal: la hermana
  se levanta a esa hora para ir al colegio y se prenden cosas (pava, microondas o similar).

## 30/10/2026 - Experimento: la Zapatilla Fede y los Adam Audio T8V

**Sospecha:** que la zapatilla no leyera el consumo de los dos monitores de estudio Adam Audio
T8V. Estaban enchufados en la misma zapatilla que la PC, con un adaptador porque el cable
original tiene enchufe europeo.

**Metodo:** lectura en vivo de la zapatilla cada 2 s (`Status 8` por HTTP al Tasmota), un
minuto por paso, sin esperar la telemetria de 30 s. Desde el paso 5 tambien el medidor de
tablero en paralelo.

| Paso | Zapatilla Fede | Corriente |
|---|---|---|
| 1. Monitores apagados, sin musica | 89 W | 0,480 A |
| 2. Monitores prendidos, en silencio (knob en 0 dB) | 86 W | 0,475 A |
| 3. Prendidos, musica a volumen normal | 91 W | 0,503 A |
| 4. Apagados, la misma musica sonando en la PC | 92 W | 0,487 A |
| 5. Prendidos, musica fuerte | 91 W | 0,509 A |
| 6. Control: monitores afuera, cargador de notebook cargando | 117 W | 0,647 A |
| 7. Control: cargador desenchufado | 98 W | 0,513 A |

**Resultado:**

- **La zapatilla mide bien.** El cargador de la notebook aparecio en el acto: unos +27 W al
  principio y menos hacia el final del minuto, a medida que la notebook se llenaba, con
  +0,13 A de corriente. Al desenchufarlo, el medidor de tablero tambien bajo unos 30 W.
- **Los T8V consumen muy poco en potencia real.** Ni prendidos ni con musica fuerte se
  distinguen del vaiven de la PC (+-3-5 W): entre los dos gastan del orden de 2-3 W o menos.
  El medidor de tablero tampoco mostro ningun salto. La corriente si sube ~0,02 A con los
  monitores prendidos, pero casi no se traduce en watts reales, que es lo que se factura.
- **El adaptador de enchufe no tiene nada que ver con la medicion:** la corriente pasa igual
  por la zapatilla. Lo que si conviene revisar es si el adaptador conecta la tierra, por
  seguridad.
- Despues del experimento los monitores quedaron enchufados directo a la pared, fuera de
  cualquier enchufe medido: su consumo (minimo) cae en "no identificado".

## 30/10/2026 - El salto de las 14:48 en la Zapatilla Fede

Mientras se comia, la Zapatilla Fede estuvo en 44-47 W de 13:00 a 14:47: la PC en espera con
la pantalla apagada, no suspendida (se desperto con solo mover el mouse). Es ~13 W mas que el
reposo de la noche (32 W), cuando habia entrado en un estado mas profundo.

A las 14:48, al volver, se prendio la tele y se movio el mouse: la zapatilla salto a 125-162 W
y quedo estable en ~120 W (+77 W, tele mas PC despierta). El medidor de tablero subio lo mismo
(+95 W, con la heladera). La Zapatilla Fede Habitacion siguio en 0 W. Nada raro: es el uso
normal.

- **Idea:** que la PC se suspenda sola tras un rato sin uso ahorraria esos ~13 W en cada
  ausencia, salvo cuando tenga que quedar prendida para la API.

## 31/10/2026 - Segunda noche base

**Condiciones:** foto en vivo a las 03:30 con la PC y la tele de la Zapatilla Fede en uso
(casa ~216 W). A las ~03:30 se apago la tele de la PC y Fede se fue a la habitacion: la PC
quedo prendida y en la Zapatilla Fede Habitacion se uso la tele con el cargador de la
notebook. Datos hasta las 10:22, sin huecos (855 lecturas por equipo).

**La noche entera, de 03:30 a 09:15 (5,75 h, 690 lecturas por equipo):**

| Equipo | Promedio | Rango | Energia |
|---|---|---|---|
| **Casa entera** | **154 W** | 103-270 W | **0,890 kWh** |
| Zapatilla Andy 1 | 46 W | 43-50 W | 0,267 kWh |
| Zapatilla Fede (la PC) | 36 W | 29-97 W | 0,208 kWh |
| Zapatilla Fede Habitacion | 13 W | 0-76 W | 0,073 kWh |
| Zapatilla Paula | 7 W | 6-10 W | 0,041 kWh |
| No identificado (heladera, router, T8V) | 52 W | | ~0,30 kWh |

**Solo el standby, de 05:00 a 09:15** (ya sin la tele ni el cargador de la habitacion):

| Equipo | Consumo | Contra el 30/10 |
|---|---|---|
| **Casa entera** | **~136 W** (minimo 103) | 148 W |
| Zapatilla Andy 1 | 49 W hasta las 06:00, despues 44 W | 49 W |
| Zapatilla Fede (la PC) | 32-33 W | 32 W |
| Zapatilla Paula | 7 W | 7 W |
| Zapatilla Fede Habitacion | 1 W | 2 W |
| No identificado (heladera, router, T8V) | ~49 W, entre 25 y 67 | ~58 W |

**Hallazgos:**

- **La base se repite: ~136 W, unos 98 kWh por mes (~$34.600) sin usar nada.** La PC
  vuelve a los mismos 32 W y Paula a los mismos 7 W: las dos noches son comparables.
- **La noche entera, con la tele de la habitacion, fueron 0,89 kWh (~$315).** De eso la
  habitacion se llevo 0,073 kWh (~$26): la tele y el cargador casi no pesan.
- **La PC tarda en llegar al reposo profundo.** Apagada la tele, quedo en ~44 W (espera con
  la pantalla apagada) y recien a las 04:30 bajo a 30-33 W.
- **Zapatilla Andy 1 bajo de 49 a 44 W cerca de las 06:00 y se quedo ahi.** Algo de la
  zapatilla dejo de consumir 5 W sin que nadie la usara: lo mas probable es el cargador de la
  notebook terminando de cargar, o la impresora entrando en un reposo mas profundo. Lo va a
  aclarar el experimento por pasos.
- **Zapatilla Fede Habitacion: ~55-60 W de 03:30 a 04:30, bajando hasta ~5 W a las 05:00** y
  despues 1 W: la tele mas el cargador de la notebook, que fue bajando a medida que la
  notebook se llenaba. Igual que la noche del 30/10.
- **El no identificado oscila entre 25 y 67 W en ciclos:** es la heladera prendiendo y
  apagando el compresor sobre una base de ~25-30 W (router y demas).
- **09:30, pico de 1,37 kW fuera de los enchufes**, unos minutos: el desayuno (pava o
  microondas). Normal. Al mismo tiempo Andy 1 subio a ~89 W un rato: alguien la uso.

## 01/11 y 02/11/2026 - Dos noches mas y los 5 W de la Zapatilla Andy 1

**Noche del 31/10 al 01/11, tramo tranquilo de 02:45 a 06:15:** casa 149 W (0,52 kWh en
3,5 h). Andy 1 49 W, PC 33 W, Paula 10 W, Fede Habitacion 0 W, no identificado 57 W.
De 00:45 a 01:30 hubo ~100 W extra fuera de los enchufes, parejos (casa en 250-267 W):
algo enchufado a la pared, sin identificar.

**Noche del 01/11 al 02/11, tramo tranquilo de 01:00 a 06:00:**

| Equipo | Promedio | Energia en 5 h |
|---|---|---|
| **Casa entera** | **150 W** (minimo 114) | **0,75 kWh** |
| No identificado (heladera, router, T8V) | 60 W | ~0,30 kWh |
| Zapatilla Andy 1 | 49 W | 0,248 kWh |
| Zapatilla Fede (la PC) | 33 W | 0,165 kWh |
| Zapatilla Paula | 8 W | 0,038 kWh |
| Zapatilla Fede Habitacion | 0 W | 0,003 kWh |

**Hallazgos:**

- **Cuatro noches, la misma base: 136-150 W.** El orden no cambia: heladera y router, Andy 1,
  la PC, Paula. La tele de la habitacion en standby marca 0 W.
- **Los 5 W que Andy 1 pierde a la mañana son, casi seguro, el infrarrojo de las camaras.**
  Tres mañanas seguidas bajo de 49 a 44 W cerca de las 06:00, y no de golpe sino en rampa:
  el 02/11 fue 49 -> 44 W entre las 05:56 y las 06:06, justo con la salida del sol. El 31/10,
  apagado lo que estaba en uso a las 19:15, la zapatilla quedo en 43-44 W y subio sola a
  49 W en rampa hasta las 19:40, con la puesta del sol. Es lo que hacen las camaras con vision
  nocturna: prenden los LEDs infrarrojos al oscurecer. O sea que el standby real de Andy 1 es
  ~44 W de dia y ~49 W de noche, y esos 5 W no se pueden sacar sin perder las camaras de
  noche.
- **El 01/11 a la noche Andy 1 quedo en 55 W y no en 49 W** de 20:30 a 22:30: algo quedo
  prendido unas dos horas despues de dejar de usarla.

## 03/11/2026 - El monitor del DVR: 26 de los 49 W de Andy 1

En la Zapatilla Andy 1 tambien esta enchufado el monitor del DVR de las camaras, que no
figuraba en el relevamiento. Quedaba prendido todo el tiempo.

**Metodo:** lectura en vivo cada 2 s, como el experimento de los T8V, y la telemetria de 30 s
para ver el corte.

| Paso | Zapatilla Andy 1 | Corriente |
|---|---|---|
| 1. Todo como de noche (02/11 23:58) | 49 W fijos | 0,329 A |
| 2. Monitor del DVR apagado (cambio entre 23:59:27 y 23:59:57) | 23 W fijos | 0,158 A |

**Resultado:**

- **El monitor del DVR gastaba 26 W, mas de la mitad del standby de Andy 1.** Prendido 24/7
  son ~19 kWh por mes, ~$6.600 al precio marginal de octubre. El DVR sigue grabando con el
  monitor apagado: solo hace falta prenderlo para mirar las camaras.
- **Quedan 23 W:** el DVR, el infrarrojo de las camaras (~5 W, solo de noche) y el standby de
  impresora, cargador, tele y home theater. Se separaron a la tarde (abajo).

## 03/11/2026 - Experimento: que queda en los 17 W de Andy 1

**Condiciones:** de 19:05 a 19:18, de dia (el infrarrojo de las camaras todavia apagado), con
el monitor del DVR apagado y nada en uso. Lectura en vivo cada 2 s, desenchufando una cosa
por vez, ~20 s por paso.

| Paso | Zapatilla Andy 1 | Corriente | Standby de lo que se saco |
|---|---|---|---|
| 0. Punto de partida | 17 W | 0,121 A | |
| 1. Impresora desenchufada | 17 W | 0,123 A | < 1 W |
| 2. Cargador de la notebook (bateria llena, notebook sin usar) | 16-17 W | 0,122 A | < 1 W |
| 3. Home theater | 13 W | 0,100 A | ~4 W |
| 4. Tele | 13 W | 0,103 A | < 1 W |
| 5. Las 2 lamparas, apagadas | 13 W | 0,100 A | 0 W |
| 6. Estabilizador de tension S4 Slim en NO, sin carga detras | 10 W | 0,088 A | ~3 W |

En la zapatilla tambien esta un estabilizador de tension (S4 Slim, de transformador), que no
figuraba en el relevamiento. De su salida cuelgan el monitor del DVR y el cargador de la
notebook; el DVR y la fuente de 12 V de las camaras van directo a la zapatilla.

**Resultado:**

- **El DVR con las camaras gasta 10 W de dia y ~15 W de noche** con el infrarrojo. Es lo
  unico que tiene que quedar prendido.
- **De los 49 W fijos de la noche, el reparto queda asi:** monitor del DVR 26 W, DVR y camaras
  ~15 W, home theater ~4 W, estabilizador ~3 W, y menos de 1 W entre impresora, cargador,
  tele y lamparas.
- **Lo que vale la pena cortar es el monitor del DVR** (~$6.600 por mes). Como el monitor
  cuelga del estabilizador, **la llave del estabilizador en NO corta las dos cosas juntas**
  (~29 W, ~$7.400 por mes) sin tocar las camaras; se prende solo para mirarlas. El home
  theater suma ~3 kWh por mes (~$1.000): se puede cortar con una zapatilla con llave si no se
  usa seguido. El resto no justifica tocarlo.
- Con el estabilizador en NO, el standby de Andy 1 pasa de 49 a ~19 W de noche, y la base de
  la casa de ~145 a ~115 W.

## 03/11 y 04/11/2026 - Primeras horas de la heladera

**Condiciones:** enchufada a las 19:44 del 03/11, de noche, sin abrirla casi. **La API no
recibio datos de 20:13 a 20:32 ni de 21:28 a 00:27**, de ningun equipo: son huecos de la
telemetria, no de la heladera. Quedan ~2 h de datos validos.

**Lo que se ve:**

- **Compresor inverter, de velocidad variable.** No prende y apaga a potencia fija: arranca
  en ~28 W y sube. El primer ciclo, despues del corte, fue largo y fuerte (19:55-20:13, de 28
  a 89 W) para recuperar el frio perdido. Despues, ciclos de ~30 min a 31-41 W y pausas de
  ~15 min en 2-3 W (la electronica).
- **Promedio medido: ~29 W, 0,138 kWh en el tiempo con datos.** Si se sostuviera serian
  ~0,7 kWh por dia, ~21 kWh por mes (~$7.300). Es poco para una heladera, pero coherente con
  un compresor inverter de noche y con la puerta cerrada.
- **Cierra con el no identificado:** antes oscilaba entre 25 y 67 W; la heladera en marcha
  suma ~35-40 W, asi que los ~25-30 W de piso eran router y demas.
- **Falta un dia entero** con uso normal (puerta abierta, cocina caliente al mediodia) y, si
  es no frost, el ciclo de descongelado, que mete una resistencia de 150-300 W unos 20-30 min
  una o dos veces por dia. Recien con eso hay un numero por mes confiable.

## 04/11/2026 - La noche sin el monitor del DVR y el descongelado de la heladera

**Condiciones:** primera noche con el monitor del DVR apagado (estabilizador en SI) y la
heladera medida. Sin huecos desde las 00:27 hasta las 10:54.

**Tramo de 01:00 a 06:00:**

| Equipo | Promedio | Energia en 5 h |
|---|---|---|
| **Casa entera** | **173 W** (minimo 85) | **0,864 kWh** |
| Heladera | 53 W (con el descongelado adentro) | 0,264 kWh |
| No identificado (router, T8V, resto) | 47 W | |
| Zapatilla Fede (la PC) | 40 W (32 W desde las 02:00) | 0,199 kWh |
| Zapatilla Andy 1 | **25 W fijos** | 0,128 kWh |
| Zapatilla Paula | 8 W | 0,038 kWh |

**Hallazgos:**

- **El ahorro del monitor se confirma: Andy 1 paso de 49 a 25 W fijos toda la noche** (-24 W,
  ~17 kWh y ~$6.100 por mes). De dia, 17 W.
- **La heladera es no frost y anoche descongelo de 03:40 a 04:55: 125 W con factor de
  potencia 1,0** (una resistencia pura, el compresor apagado). 75 min, ~0,155 kWh por
  descongelado. Al terminar, el compresor arranco mas fuerte (40-47 W) para recuperar el frio.
- **Fuera del descongelado, la heladera sigue el patron de ciclos inverter:** ~30 min a 31-41 W
  y pausas en 1-3 W. En las 10 h continuas de 00:30 a 10:30 gasto 0,412 kWh (41 W de
  promedio), con un descongelado adentro.
- **Estimacion provisoria: ~0,8 kWh por dia, ~23 kWh por mes (~$8.200)** si descongela una vez
  por dia; ~30 kWh si lo hace cada ~10 h. Hace falta ver cuando viene el proximo para saber la
  frecuencia.
- **La base sin uso queda en ~120 W** (PC 32 + Andy 25 + Paula 7 + heladera ~26 de promedio
  sin descongelar + ~28 de router y resto), contra ~145 W antes del monitor. El minimo de la
  noche fue 85 W, con la heladera en pausa.
- **El no identificado en reposo bajo a ~25-30 W:** sin la heladera es el router, los T8V y lo
  que quede en standby fuera de los enchufes.

## 04/11/2026 - EcoWatt pasa a un servidor casero

**Desde las 11:52 la API, la base y el dashboard corren en una PC vieja dedicada** (AMD A4-7300,
Ubuntu Server, IP 192.168.1.7), prendida 24/7. Hasta ahora corrian en la PC de escritorio, la
de la Zapatilla Fede. El detalle tecnico esta en el README, "Dejarlo andando".

- **Hueco en los datos de 11:42 a 11:52** por la mudanza. La energia de esos 10 minutos no se
  perdio: los contadores acumulados de los Tasmota la registraron y entra en la hora de 11 a 12
  (0,05 kWh en el medidor). Lo que falta es la curva de watts de ese rato.
- **El servidor esta en un enchufe sin medir:** su consumo cae en "no identificado" y sube el
  consumo base. Estimado en 30-40 W fijos (~22-29 kWh y ~$7.700-10.300 por ciclo al precio
  marginal de hoy); todavia no esta medido.
- **A cambio, la PC de escritorio ya no tiene por que quedar prendida de noche.** Hasta ahora
  sumaba ~32 W en reposo en la Zapatilla Fede solo para que la API siguiera viva. Si se apaga
  de noche, el aumento del servidor queda casi compensado.
- La mediana de 14 noches del consumo base va a ir absorbiendo el cambio de a poco: una suba de
  ~30 W en "no identificado" desde esta noche es el servidor, no un aparato nuevo.

## Pendientes

El metodo y el detalle de cada experimento estan en [plan-de-mediciones.md](plan-de-mediciones.md).

- [x] Separar el standby de la Zapatilla Andy 1 (49 W fijos de noche): 03/11, el monitor del
  DVR eran 26 W; el DVR con las camaras, 10-15 W; el home theater, ~4 W; el estabilizador,
  ~3 W.
- [x] Relevar que hay enchufado en la zapatilla de Andy 1 (30/10).
- [x] Relevar que hay enchufado en la Zapatilla Paula (30/10).
- [x] Experimento: verificar que la Zapatilla Fede lea el consumo de los Adam Audio T8V
  (30/10: lee bien, los monitores consumen muy poco).
- [ ] Revisar si el adaptador de los T8V conecta la tierra.
- [ ] Medir el consumo real del servidor casero: comparar el "no identificado" de las noches
  antes y despues del 04/11, o enchufarlo un rato en una zapatilla medida.
