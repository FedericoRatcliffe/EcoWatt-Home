# EcoWatt Casa: potencial comercial

Analisis del 08/10/2026 sobre si el proyecto se puede vender, a quien y con que modelo.

Respuesta corta: **tiene potencial, pero no asi como esta.** Hoy es una muy buena herramienta
para una casa. Para venderla falta producto alrededor, y antes de construirlo conviene
confirmar que alguien la pague.

## Que tiene de valioso

Medir consumo ya lo regalan Tuya/Smart Life, eWeLink, Shelly o Home Assistant. Lo distinto es
otra cosa: **traduce el consumo a la factura argentina real.** Tramos, ciclo de lectura real,
cargos fijos, impuestos, aviso de cruce de tramo y el objetivo en pesos. Esas apps dicen
"consumiste 5 kWh". Esta dice "a este ritmo pagas $78.000, y para llegar a $60.000 baja a
3,5 kWh por dia". Con las subas de tarifa recientes, ese dolor es real.

## Clientes posibles, del mas facil al mas dificil

| Segmento | Por que le sirve | Dificultad |
|---|---|---|
| **Comercios y pymes** (almacenes, panaderias, heladerias, consultorios, gimnasios) | Facturas mas altas y equipos que consumen fijo (heladeras, hornos, aires). Pagan por ahorrar. | Media. Las tarifas comerciales son distintas y algunas cobran potencia contratada. |
| **Alquileres temporarios y propietarios** | Saber cuanto consumio cada huesped o inquilino, y cobrarselo. | Media |
| **Instaladores electricos y vendedores de paneles solares** | Lo ofrecen como valor agregado. Para solar es ideal: mostrar el ahorro real. | Media. Venden ellos y se suman muchos clientes de una. |
| **Hogares** | El dolor es real. | Alta. Muy sensibles al precio, rechazan las suscripciones y el medidor de tablero necesita electricista. |
| **Cooperativas electricas** (hay cientos en el pais) | Ofrecerselo a sus socios con su marca. Ya esta hecho el parser de una. | Alta. Ventas lentas e institucionales, pero cada contrato es grande. |

No arrancaria por hogares. Arrancaria por **comercios** o por **instaladores**.

## Modelos de negocio

1. **Auditoria energetica como servicio** (el mas rapido para empezar). Se instalan los equipos
   30 a 60 dias, se entrega un informe con donde se va la plata y que cambiar, y se retiran
   los equipos. Se cobra el servicio. No necesita nube, ni varios clientes en el sistema, ni
   soporte permanente: casi se puede hacer con lo que ya existe. Y sirve para medir cuanto
   ahorra la gente de verdad, que es el mejor argumento de venta.
2. **Kit + suscripcion mensual.** El cliente paga el hardware y la instalacion una vez, mas un
   abono mensual por la plataforma.
3. **Licencia para instaladores o cooperativas.** Ellos ponen el hardware y la relacion con el
   cliente; se cobra por instalacion activa.

**Cobrar los enchufes?** Si, el hardware lo paga el cliente. Regalarlo para recuperarlo con la
suscripcion necesita capital y deja expuesto a que cancelen. Se puede revender con un margen
chico o que lo compre el instalador. Donde se gana es en el software y el servicio, porque se
repiten todos los meses.

**Es rentable?** Depende de cuanto ahorra el cliente. Los estudios sobre mostrar el consumo en
tiempo real suelen hablar de ahorros del 5 al 15%. Sobre una factura de $80.000 son $4.000 a
$12.000 por mes, y un abono tiene que quedar claramente por debajo de eso. En un hogar el
margen es finito. En un comercio que paga $500.000 de luz, la cuenta cierra mucho mejor.

## Que le falta para venderse

- **Varios clientes y acceso remoto.** Hoy es una sola casa en la red local y sin login. Haria
  falta alojamiento en la nube, usuarios y una casa por cliente.
- **Instalacion sin conocimientos tecnicos.** Hoy hay que configurar Tasmota y MQTT a mano. Un
  cliente no lo va a hacer.
- **Tarifas de otras distribuidoras.** El parser es de una cooperativa. Para otras zonas hace
  falta un catalogo de tarifas (EPE, Edenor, Edesur, EPEC, otras cooperativas) o la carga
  manual bien resuelta.
- **Avisos al celular.** Push, WhatsApp o mail. Hoy las alertas solo se ven si se abre el
  dashboard.
- **Informes** mensuales o en PDF, sobre todo para comercios.
- **Temas legales.** Facturar (monotributo), datos personales (Ley 25.326) y, si se vende el
  hardware, verificar la certificacion de seguridad electrica que se exige para
  comercializarlo. El medidor de tablero lo tiene que instalar un electricista matriculado:
  conviene trabajar con uno.

## Proximos pasos

Nada tecnico todavia. Primero:

1. Hablar con 5 a 10 comercios o instaladores conocidos y preguntar cuanto pagan de luz y si
   pagarian por esto.
2. Hacer 2 o 3 pilotos gratis o baratos con el modelo de auditoria y medir el ahorro.
3. Con esos numeros decidir el segmento, y recien ahi invertir en la nube, varios clientes y
   la instalacion simple.
