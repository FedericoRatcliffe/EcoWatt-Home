# EcoWatt Casa: potencial comercial

Analisis del 08/10/2026, revisado el 30/10/2026 con fuentes sobre ahorro real, competencia y
tarifas. La revision corrige el ahorro esperado (era optimista) y separa con mas claridad que
se puede vender y que no.

Respuesta corta: **como software para hogares, casi no tiene potencial comercial. Como
herramienta para diagnosticar comercios y pymes, si, pero el negocio es el servicio, no el
software.** No es una startup escalable; puede ser un ingreso extra o un servicio chico, y
vale mucho como portfolio.

## Que tiene de valioso

Medir consumo ya lo regalan Tuya/Smart Life, eWeLink, Shelly o Home Assistant. Lo distinto es
otra cosa: **traduce el consumo a la factura argentina real.** Tramos, ciclo de lectura real,
cargos fijos, impuestos, aviso de cruce de tramo y el objetivo en pesos. Esas apps dicen
"consumiste 5 kWh". Esta dice "a este ritmo pagas $78.000, y para llegar a $60.000 baja a
3,5 kWh por dia".

El contexto empuja a favor: en 2026 rige el esquema de subsidios focalizados, el plus de
subsidio baja 2% por mes hasta desaparecer en diciembre de 2026, y los hogares sin subsidio
ya pagan casi el costo real. Cada vez mas gente va a mirar la factura con lupa.

## Lo que corrige esta revision

**El ahorro por mostrar el consumo es chico.** La version anterior hablaba de 5 a 15%. Los
estudios grandes dicen otra cosa:

- Nueve pilotos de EE.UU., Reino Unido e Irlanda (ACEEE, 2012): promedio **3,8%**, con unos
  pocos hogares que ahorran mucho y la mayoria casi nada.
- Meta-analisis con datos de Australia (McKerracher y Torriti): el efecto realista a escala
  es **3 a 5%**.
- El interes se cae: muchos hogares dejan de mirar la pantalla cuando ya saben cuanto gastan.

Sobre una factura de $80.000, 4% son **~$3.200 por mes**. El hardware (medidor de tablero,
enchufes y electricista) se paga en meses, y un abono tendria que costar menos que eso. En
un hogar la cuenta no cierra.

**El software solo es facil de copiar.** El calculo de tramos es trabajo cuidadoso, pero no es
secreto: cualquiera con las facturas lo replica. Y las distribuidoras estan instalando
medidores inteligentes con lectura por celular (San Juan apunta a 4.100 en industrias y
comercios para 2027; Edesur, CALF en Neuquen). Cuando el medidor de la distribuidora muestre
el consumo, el medidor propio pierde razon de ser en los clientes de esa zona.

## Donde si hay plata

En un comercio, la plata no esta en "consumir un poco menos", esta en **encontrar el error**:

- **Equipos que consumen de mas.** Una heladera exhibidora con el burlete roto o el
  compresor gastado, un aire que no corta, algo prendido toda la noche. Es lo que el
  proyecto ya hace bien: el consumo base nocturno y el no identificado.
- **Cargos que no dependen de cuanto consumis** *(a verificar con facturas comerciales reales)*:
  en las tarifas de mediana demanda suelen cobrarse la potencia contratada y un recargo por
  bajo factor de potencia. El sistema ya recibe y guarda el factor de potencia que manda
  Tasmota (`Factor`, `ReactivePower`), aunque todavia no lo muestra. Con eso y el pico real
  se puede decir "estas pagando potencia que no usas" o "te cobran un recargo que se elimina con un
  banco de capacitores". Ese ahorro no depende de que alguien cambie sus habitos.
- CAME y la Fundacion Bariloche estiman que las pymes dejan pasar ahorros de **10 a 25%**,
  pero ese numero viene de medidas de eficiencia (cambiar equipos, corregir instalaciones),
  no de mirar un grafico. El sistema sirve para encontrarlas y para demostrar el ahorro
  despues.

## Clientes posibles, revisado

| Segmento | Veredicto |
|---|---|
| **Comercios y pymes** con heladeras, hornos o aires | **El mejor.** Facturas altas, errores caros y el dueno decide rapido. |
| **Instaladores de paneles solares** | **Bueno.** Mostrar el ahorro real en pesos vende la proxima instalacion. Ojo: los inversores ya traen su app de generacion; el valor es cruzarla con la factura. |
| **Alquileres temporarios** | Nicho. Sirve si quieren cobrar la luz por estadia. |
| **Cooperativas electricas** | Contrato grande pero venta lenta, y muchas van a instalar sus propios medidores. Mejor como cliente del parser y los informes que del hardware. |
| **Hogares** | **No.** Ahorro de ~$3.000 por mes, rechazo a las suscripciones y el medidor necesita electricista. |

## Modelos de negocio, en orden

1. **Auditoria energetica para comercios** (el unico que se puede empezar ya). Se instalan
   los equipos 30 a 60 dias, se entrega un informe con donde se va la plata y que cambiar, y
   se retiran. El hardware se reutiliza en la auditoria siguiente. Se cobra el servicio,
   idealmente atado al ahorro encontrado. No hace falta nube ni varios clientes en el
   sistema: alcanza con levantar una instancia por auditoria.
2. **Herramienta para instaladores.** Ellos tienen la relacion con el cliente y el
   electricista; se cobra por instalacion activa.
3. **Kit + abono mensual.** Solo para comercios, y solo si las auditorias demuestran que el
   seguimiento continuo encuentra problemas nuevos (una heladera que empieza a fallar). Si
   no, el cliente no tiene por que seguir pagando.

**Cobrar los enchufes?** Si. En la auditoria son herramienta propia que se reutiliza; en un
kit, el cliente paga el hardware.

## Riesgos

- **Es un servicio, no un producto.** La auditoria escala con horas de trabajo y viajes, no
  con clientes. Sirve como ingreso, no como empresa que crece sola.
- **Vender es el cuello de botella**, no el codigo. Con un trabajo de tiempo completo, el
  limite es cuantos comercios se pueden visitar.
- **Las distribuidoras.** Si instalan medidores con app, el medidor de tablero sobra; lo que
  queda es el desglose por equipo y la interpretacion, que es el informe.
- **Responsabilidad.** Instalar en el tablero de un comercio necesita electricista
  matriculado, y un consejo equivocado (un equipo que se corta) es un problema con el
  cliente.

## Que le falta, segun el modelo

Para la **auditoria** casi nada: un informe en PDF al final del periodo, el manejo de una
tarifa comercial (potencia contratada y factor de potencia, si aplica) y un proceso para
instalar y retirar rapido.

Para el **kit con abono** falta lo de la version anterior: nube, usuarios y una casa por
cliente, instalacion sin configurar Tasmota a mano, catalogo de tarifas de otras
distribuidoras, avisos al celular, y los temas legales (monotributo, Ley 25.326 de datos
personales, certificacion electrica si se vende hardware).

## Proximos pasos

Nada tecnico todavia:

1. Conseguir 3 o 4 **facturas de comercios conocidos** y ver que cobran: tarifa, potencia
   contratada, recargo por factor de potencia. Eso dice si el angulo de los cargos existe.
2. Preguntarles cuanto pagan y si pagarian por un diagnostico de un mes.
3. Hacer **una auditoria piloto gratis** con el sistema como esta y medir cuanto ahorro se
   encontro en pesos. Ese numero decide todo lo demas.
4. Con 2 o 3 pilotos, decidir si sigue como servicio, si se habla con instaladores, o si se
   queda como proyecto personal y portfolio, que tambien es un buen resultado.

## Fuentes

- Ahorro por feedback: [ACEEE, informe B122](https://aceee.org/research-report/b122);
  [McKerracher y Torriti, meta-analisis con datos australianos](https://centaur.reading.ac.uk/28499);
  [BuildingGreen, sobre la caida del interes](https://www.buildinggreen.com/node/10418).
- Tarifas y subsidios 2026: [iProfesional](https://www.iprofesional.com/energia/439549-tarifas-gas-luz-2026-aumentos-y-menos-subsidios-para-bajar-gasto);
  [Infobae](https://www.infobae.com/economia/2025/12/31/aumentan-las-tarifas-de-luz-gas-y-agua-en-el-inicio-de-2026-cual-sera-el-impacto-en-todo-el-pais/).
- Medidores de distribuidoras: [San Juan, Tiempo de San Juan](https://www.tiempodesanjuan.com/economia/san-juan-modernizara-su-red-electrica-con-la-instalacion-de-4-000-medidores-inteligentes-para-la-industria_1782139517/amp);
  [Edesur, El Inversor Energetico](https://www.elinversorenergetico.com/edesur-quiere-colocar-medidores-inteligentes-pero-hay-dudas-sobre-sus-alcances);
  [CALF Neuquen](https://www.noticiasnqn.com.ar/noticias/2023/05/18/276968-instalaron-1000-medidores-inteligentes-de-energa-en-neuqun).
- Ahorro en pymes: [CAME, "Hacia la transformacion energetica"](https://editores-srl.com.ar/print/8252).
