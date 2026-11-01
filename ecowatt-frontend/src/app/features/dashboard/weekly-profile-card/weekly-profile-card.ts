import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import type { EChartsCoreOption } from 'echarts/core';
import { ProfileCell, WeeklyProfile } from '../../../core/models/api.models';
import { EcowattApi } from '../../../core/services/ecowatt-api';
import { Chart } from '../../../shared/chart';
import { ChartThemeWatcher } from '../../../shared/chart-theme';
import { kwh, percent, watts } from '../../../shared/format';

const DAYS = ['Lunes', 'Martes', 'Miercoles', 'Jueves', 'Viernes', 'Sabado', 'Domingo'];

/** Cuantas semanas entran al promedio. Cuatro alcanzan para que un dia raro no domine. */
const WEEKS = 4;

/**
 * La semana tipica de la casa, dia por hora. Responde de un vistazo cuando se consume y como
 * cambia el fin de semana, sin dedicarle un experimento.
 */
@Component({
  selector: 'app-weekly-profile-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Chart],
  templateUrl: './weekly-profile-card.html',
})
export class WeeklyProfileCard {
  private readonly api = inject(EcowattApi);
  private readonly themeWatcher = inject(ChartThemeWatcher);

  protected readonly kwh = kwh;
  protected readonly watts = watts;
  protected readonly days = DAYS;
  protected readonly weeks = WEEKS;

  protected readonly data = signal<WeeklyProfile | null>(null);
  protected readonly failed = signal(false);
  protected readonly showTable = signal(false);

  protected readonly hasData = computed(() => (this.data()?.cells ?? []).some((c) => c.samples > 0));

  /** Fin de semana contra dias habiles, en palabras. */
  protected readonly weekendText = computed(() => {
    const d = this.data();
    if (d?.weekdayKwh == null || d.weekendKwh == null || d.weekdayKwh === 0) return null;

    const change = ((d.weekendKwh - d.weekdayKwh) / d.weekdayKwh) * 100;
    if (Math.abs(change) < 3) return 'el fin de semana la casa consume casi lo mismo';
    return change > 0
      ? `el fin de semana la casa consume ${percent(change, 0)} mas`
      : `el fin de semana la casa consume ${percent(-change, 0)} menos`;
  });

  /** Para la tabla: por dia, cuanto se consume y a que hora pica. */
  protected readonly rows = computed(() => {
    const d = this.data();
    if (!d) return [];

    return DAYS.map((name, day) => {
      const cells = d.cells.filter((c) => c.day === day && c.samples > 0);
      const peak = cells.reduce<ProfileCell | null>((max, c) => (!max || c.avgWatts > max.avgWatts ? c : max), null);
      const low = cells.reduce<ProfileCell | null>((min, c) => (!min || c.avgWatts < min.avgWatts ? c : min), null);
      return { name, kwh: d.dayKwh[day], peak, low };
    });
  });

  protected readonly option = computed<EChartsCoreOption>(() => {
    const t = this.themeWatcher.theme();
    const cells = (this.data()?.cells ?? []).filter((c) => c.samples > 0);
    const max = cells.reduce((m, c) => Math.max(m, c.avgWatts), 0);

    return {
      textStyle: { fontFamily: t.font },
      grid: { left: 4, right: 16, top: 8, bottom: 56, containLabel: true },
      tooltip: {
        trigger: 'item',
        backgroundColor: t.surface,
        borderColor: t.baseline,
        borderWidth: 1,
        borderRadius: 10,
        extraCssText: 'box-shadow: 0 6px 20px rgba(0, 40, 80, 0.12);',
        textStyle: { color: t.textPrimary, fontSize: 12, fontFamily: t.font },
        formatter: (p: { value: [number, number, number, number] }) => {
          const [hour, day, w, samples] = p.value;
          const n = samples === 1 ? '1 semana' : `${samples} semanas`;
          return `<b>${DAYS[day]} ${hourText(hour)}</b><br/>${watts(w)} promedio (${n})`;
        },
      },
      xAxis: {
        type: 'category',
        data: Array.from({ length: 24 }, (_, h) => hourText(h)),
        splitArea: { show: false },
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11, interval: 2 },
      },
      yAxis: {
        type: 'category',
        // El eje de categorias dibuja el primero abajo: se invierte para que el lunes quede arriba.
        inverse: true,
        data: DAYS.map((d) => d.slice(0, 3)),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.textSecondary, fontSize: 12 },
      },
      visualMap: {
        min: 0,
        max: Math.max(max, 1),
        calculable: false,
        orient: 'horizontal',
        left: 'center',
        bottom: 0,
        itemWidth: 12,
        itemHeight: 160,
        text: [watts(max), '0 W'],
        textStyle: { color: t.muted, fontSize: 11 },
        inRange: { color: t.sequential },
      },
      series: [
        {
          type: 'heatmap',
          data: cells.map((c) => [c.hour, c.day, c.avgWatts, c.samples]),
          // 2px de superficie entre celdas: se lee como grilla y no como mancha.
          itemStyle: { borderColor: t.surface, borderWidth: 2, borderRadius: 3 },
          emphasis: { itemStyle: { borderColor: t.textPrimary, borderWidth: 1 } },
        },
      ],
    };
  });

  protected readonly hourText = hourText;

  constructor() {
    this.api.getWeeklyProfile(WEEKS).subscribe({
      next: (d) => this.data.set(d),
      error: () => this.failed.set(true),
    });
  }
}

function hourText(hour: number): string {
  return `${String(hour).padStart(2, '0')}:00`;
}
