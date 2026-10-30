# Plan de mediciones

Como se hacen los experimentos de consumo y cuales quedan por hacer. Los resultados van a
[registro-consumo.md](registro-consumo.md); este archivo es la lista de lo que falta y el
metodo.

## Como se mide

La API guarda una lectura de cada equipo cada 30 s en la base, este o no abierta una sesion
con Claude. Claude no mira nada entre mensajes: analiza despues, consultando la franja
horaria. Por eso lo que importa es contar las condiciones, que no se deducen de los watts.

**Experimento largo (una noche, un dia):**

1. Antes de arrancar, avisar que queda prendido, que se apaga y a que hora.
2. Al terminar, avisar ("ya me desperte", "ya volvi").
3. Claude consulta la base en esa ventana y lo cruza con las condiciones.

No hace falta que sea la misma conversacion: el registro guarda el contexto.

**Experimento corto (separar que consume cada cosa):** lectura en vivo del enchufe cada 2 s
(`Status 8` por HTTP al Tasmota), un minuto por paso, cambiando una sola cosa por vez. Asi se
hizo el de los Adam Audio T8V.

**Ojo:** la PC de la Zapatilla Fede corre la API y el broker MQTT. Si se apaga o se suspende,
los enchufes no tienen a quien mandarle datos y la medicion se corta. Sus 32-45 W en reposo
son, en la practica, el costo del servidor.

## Pendientes

### Separar los 49 W fijos de la Zapatilla Andy 1

El standby mas grande de la casa: 49 W todo el mes son ~35 kWh, ~$12.500. En la zapatilla
estan impresora, DVR de las camaras, cargador de notebook, 2 lamparas, tele y home theater.

Experimento corto, ~10 min, con alguien en la pieza de Andy: desenchufar o apagar una cosa
por vez y medir cuanto baja. El DVR tiene que quedar prendido; el objetivo es saber cuanto
es el y cuanto el standby que se podria cortar.

### Standby de las teles y el home theater

Una noche con las teles apagadas desde el control (Fede Habitacion, Andy) y el home theater
en standby, para ver cuanto gastan "apagados".

### Dia de semana contra fin de semana

Un dia completo de cada uno, para ver como cambia la casa con todos en casa.

### Servidor en una Raspberry Pi

No es un experimento sino una decision: pasar la API a una Pi (~5 W) en vez de dejar la PC
prendida 24/7 (32-45 W, ~$8.000-11.000 por mes). Ademas libera a la PC para suspenderse.

### Tierra del adaptador de los T8V

Control de seguridad, a mano: revisar si el adaptador del enchufe europeo conecta la tierra.
