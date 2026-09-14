import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { BarChart, LineChart } from 'echarts/charts';
import {
  DataZoomComponent,
  GridComponent,
  LegendComponent,
  MarkLineComponent,
  TooltipComponent,
} from 'echarts/components';
import * as echarts from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';

// Import modular: solo lo que se usa, para no meter el echarts entero en el bundle.
echarts.use([
  BarChart,
  LineChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  DataZoomComponent,
  MarkLineComponent,
  CanvasRenderer,
]);

/**
 * Envoltorio minimo de ECharts. Se hace a mano en lugar de usar un wrapper de terceros
 * para no quedar atado a que ese paquete soporte la version de Angular del proyecto.
 */
@Component({
  selector: 'app-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="chart-host" [style.height.px]="height()"></div>`,
  styles: `
    .chart-host {
      width: 100%;
    }
  `,
})
export class Chart {
  readonly option = input.required<echarts.EChartsCoreOption>();

  /** Alto total del contenedor, ya incluida la banda del eje X. */
  readonly height = input(280);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private instance?: echarts.ECharts;
  private observer?: ResizeObserver;

  constructor() {
    afterNextRender(() => {
      try {
        this.instance = echarts.init(this.host().nativeElement, undefined, { renderer: 'canvas' });
        this.instance.setOption(this.option());
      } catch (error) {
        // Un entorno sin canvas (jsdom, un navegador con el canvas bloqueado) no puede pintar:
        // se deja el hueco vacio en vez de tumbar toda la pagina. La vista de tabla sigue.
        console.warn('No se pudo inicializar el grafico', error);
        return;
      }

      // ResizeObserver no existe en todos los entornos.
      if (typeof ResizeObserver === 'function') {
        this.observer = new ResizeObserver(() => this.instance?.resize());
        this.observer.observe(this.host().nativeElement);
      }
    });

    // notMerge: las series cambian de largo al cambiar de rango; un merge dejaria puntos viejos.
    effect(() => {
      const option = this.option();
      this.instance?.setOption(option, { notMerge: true });
    });

    inject(DestroyRef).onDestroy(() => {
      this.observer?.disconnect();
      this.instance?.dispose();
    });
  }
}
