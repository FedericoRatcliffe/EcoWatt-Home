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
| Zapatilla Fede Habitacion | La tele de la habitacion | Cargadores u otra cosa que haga falta |
| Zapatilla Andy 1 | Impresora, DVR de las camaras, cargador de notebook, 2 lamparas, tele y home theater | |
| Zapatilla Paula | PC, monitor/tele, home theater y 1 lampara | |

- **Zapatilla Andy 1** es una zapatilla de dos tomas con otra zapatilla enchufada en una de
  ellas: todo lo que cuelga de las dos pasa por el mismo enchufe medido, y el techo de
  2.200 W de los enchufes de 10A aplica a la suma.
- **Los monitores de estudio Adam Audio T8V van directo a la pared**, fuera de cualquier
  enchufe medido (desde el 30/10/2026).

Lo que no pasa por ningun enchufe medido (heladera, router, luces, cocina, los T8V) aparece
como "no identificado": la diferencia entre el medidor y la suma de los enchufes.

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

## Pendientes

- [ ] Separar el standby de la Zapatilla Andy 1 (49 W fijos de noche): cuanto es el DVR,
  que tiene que quedar prendido, y cuanto el resto (impresora, cargador, tele, home theater).
- [x] Relevar que hay enchufado en la zapatilla de Andy 1 (30/10).
- [x] Relevar que hay enchufado en la Zapatilla Paula (30/10).
- [x] Experimento: verificar que la Zapatilla Fede lea el consumo de los Adam Audio T8V
  (30/10: lee bien, los monitores consumen muy poco).
- [ ] Revisar si el adaptador de los T8V conecta la tierra.
