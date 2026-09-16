import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import type { EChartsCoreOption } from 'echarts/core';
import {
  Alert,
  CycleInfo,
  DailyDashboard,
  DeviceConsumption,
  PeriodDashboard,
} from '../../core/models/api.models';
import { EcowattApi } from '../../core/services/ecowatt-api';
import { Realtime } from '../../core/services/realtime';
import { Chart } from '../../shared/chart';
import { ChartThemeWatcher } from '../../shared/chart-theme';
import {
  dayLabel,
  hourLabel,
  kwh,
  money,
  moneySmart,
  percent,
  todayIso,
  watts,
} from '../../shared/format';

/** Cada cuanto se refrescan los agregados. Las lecturas en vivo llegan por SignalR. */
const REFRESH_MS = 60_000;

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Chart],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(EcowattApi);
  private readonly themeWatcher = inject(ChartThemeWatcher);
  protected readonly realtime = inject(Realtime);

  protected readonly money = money;
  protected readonly moneySmart = moneySmart;
  protected readonly kwh = kwh;
  protected readonly watts = watts;
  protected readonly percent = percent;
  protected readonly hourText = hourLabel;
  protected readonly dayText = dayLabel;

  protected readonly date = signal(todayIso());

  /** true = ciclo de facturacion real; false = mes calendario. */
  protected readonly useCycle = signal(true);

  /** 0 = periodo actual, -1 = el anterior. */
  protected readonly offset = signal(0);

  protected readonly daily = signal<DailyDashboard | null>(null);
  protected readonly period = signal<PeriodDashboard | null>(null);
  protected readonly cycleInfo = signal<CycleInfo | null>(null);
  protected readonly alerts = signal<Alert[]>([]);

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly showDeviceTable = signal(false);
  protected readonly showHourlyTable = signal(false);
  protected readonly showDailyTable = signal(false);
  protected readonly showBillDetail = signal(false);

  /**
   * Las cards mezclan el costo del periodo (que viene de la API) con la potencia en vivo
   * (que llega por el hub): el costo se recalcula cada minuto, los watts al instante.
   */
  protected readonly cards = computed<DeviceConsumption[]>(() => {
    const live = this.realtime.wattsByDevice();
    return (this.period()?.devices ?? []).map((d) => ({
      ...d,
      currentWatts: live[d.deviceId] ?? d.currentWatts,
    }));
  });

  protected readonly totalWattsNow = computed(() =>
    this.cards().reduce((sum, c) => sum + (c.currentWatts ?? 0), 0),
  );

  protected readonly isToday = computed(() => this.date() === todayIso());

  protected readonly bill = computed(() => this.period()?.bill ?? null);

  /** El color de estado siempre va acompañado de la palabra: nunca informa solo. */
  protected severityLabel(severity: Alert['severity']): string {
    return severity === 'Critical' ? 'Critico' : severity === 'Warning' ? 'Atencion' : 'Info';
  }

  protected severityClass(severity: Alert['severity']): string {
    return severity.toLowerCase();
  }

  /** Direccion del cambio contra el periodo anterior: flecha + texto, nunca solo color. */
  protected readonly trend = computed(() => {
    const p = this.period();
    const change = p?.changePercentVsPreviousPeriod;

    if (!p || change === null || change === undefined) {
      return { kind: 'igual' as const, arrow: '', text: 'sin periodo anterior para comparar' };
    }

    if (Math.abs(change) < 0.5) {
      return { kind: 'igual' as const, arrow: '=', text: 'igual que el periodo anterior' };
    }

    // Mientras el ciclo esta abierto la comparacion es contra el anterior a la misma altura:
    // medio ciclo contra uno entero siempre daria "bajaste 40%", que no significa nada.
    const reference = p.comparisonIsPartial ? 'que el ciclo anterior a esta altura' : 'que el periodo anterior';

    return change < 0
      ? { kind: 'baja' as const, arrow: '↓', text: `${percent(Math.abs(change))} menos ${reference}` }
      : { kind: 'sube' as const, arrow: '↑', text: `${percent(change)} mas ${reference}` };
  });

  /**
   * Precio medio a mostrar. Con el ciclo abierto, el del periodo esta inflado: reparte los
   * cargos fijos del mes entero sobre los kWh que van hasta ahora. Se muestra el proyectado.
   */
  protected readonly averagePrice = computed(() => {
    const projection = this.period()?.projection;
    return projection
      ? { value: projection.averagePricePerKwh, label: 'medio proyectado' }
      : { value: this.bill()?.averagePricePerKwh ?? 0, label: 'medio' };
  });

  /** Costo por dispositivo en el periodo: una sola serie, un color, valor al final de la barra. */
  protected readonly deviceCostOption = computed<EChartsCoreOption>(() => {
    const t = this.themeWatcher.theme();
    // El eje de categorias dibuja el primer item abajo: se invierte para que el mayor quede arriba.
    const devices = [...(this.period()?.devices ?? [])].reverse();

    return {
      textStyle: { fontFamily: t.font },
      grid: { left: 4, right: 92, top: 8, bottom: 4, containLabel: true },
      tooltip: {
        trigger: 'item',
        backgroundColor: t.surface,
        borderColor: t.baseline,
        borderWidth: 1,
        borderRadius: 10,
        extraCssText: 'box-shadow: 0 6px 20px rgba(0, 40, 80, 0.12);',
        textStyle: { color: t.textPrimary, fontSize: 12, fontFamily: t.font },
        formatter: (p: { dataIndex: number }) => {
          const d = devices[p.dataIndex];
          return `<b>${d.name}</b><br/>${money(d.cost)} &middot; ${kwh(d.kwh)}<br/>${percent(d.sharePercent)} del consumo medido`;
        },
      },
      xAxis: {
        type: 'value',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11, formatter: (v: number) => money(v) },
        splitLine: { lineStyle: { color: t.grid, width: 1, type: 'solid' } },
      },
      yAxis: {
        type: 'category',
        data: devices.map((d) => d.name),
        axisLine: { lineStyle: { color: t.baseline, width: 1 } },
        axisTick: { show: false },
        axisLabel: { color: t.textSecondary, fontSize: 12 },
      },
      series: [
        {
          type: 'bar',
          data: devices.map((d) => d.cost),
          barMaxWidth: 24,
          // Punta redondeada de 4px del lado del dato, cuadrada contra la linea base.
          itemStyle: { color: t.series1, borderRadius: [0, 4, 4, 0] },
          label: {
            show: true,
            position: 'right',
            distance: 8,
            color: t.textSecondary,
            fontSize: 12,
            formatter: (p: { value: number }) => money(p.value),
          },
        },
      ],
    };
  });

  /** Costo por dia del periodo: se ve el escalon cuando la casa pasa de tramo. */
  protected readonly dailyCostOption = computed<EChartsCoreOption>(() => {
    const t = this.themeWatcher.theme();
    const points = this.period()?.daily ?? [];

    return {
      textStyle: { fontFamily: t.font },
      grid: { left: 4, right: 16, top: 16, bottom: 4, containLabel: true },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow', shadowStyle: { color: t.series1Soft } },
        backgroundColor: t.surface,
        borderColor: t.baseline,
        borderWidth: 1,
        borderRadius: 10,
        extraCssText: 'box-shadow: 0 6px 20px rgba(0, 40, 80, 0.12);',
        textStyle: { color: t.textPrimary, fontSize: 12, fontFamily: t.font },
        formatter: (params: Array<{ dataIndex: number }>) => {
          const p = points[params[0]?.dataIndex ?? 0];
          if (!p) return '';
          return `<b>${dayLabel(p.bucketLocal)}</b><br/>${moneySmart(p.cost)} &middot; ${kwh(p.kwh)}<br/>${watts(p.avgWatts)} promedio de la casa`;
        },
      },
      xAxis: {
        type: 'category',
        data: points.map((p) => dayLabel(p.bucketLocal)),
        axisLine: { lineStyle: { color: t.baseline, width: 1 } },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
      },
      yAxis: {
        type: 'value',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11, formatter: (v: number) => money(v) },
        splitLine: { lineStyle: { color: t.grid, width: 1, type: 'solid' } },
      },
      series: [
        {
          type: 'bar',
          data: points.map((p) => p.cost),
          barMaxWidth: 24,
          barCategoryGap: '35%',
          itemStyle: { color: t.series1, borderRadius: [4, 4, 0, 0] },
        },
      ],
    };
  });

  /** Consumo de la casa por hora del dia elegido. */
  protected readonly hourlyOption = computed<EChartsCoreOption>(() => {
    const t = this.themeWatcher.theme();
    const points = this.daily()?.hourly ?? [];

    return {
      textStyle: { fontFamily: t.font },
      // top holgado: el rotulo 'kWh' del eje vive arriba del area de ploteo.
      grid: { left: 4, right: 16, top: 30, bottom: 4, containLabel: true },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'line', lineStyle: { color: t.baseline, width: 1 } },
        backgroundColor: t.surface,
        borderColor: t.baseline,
        borderWidth: 1,
        borderRadius: 10,
        extraCssText: 'box-shadow: 0 6px 20px rgba(0, 40, 80, 0.12);',
        textStyle: { color: t.textPrimary, fontSize: 12, fontFamily: t.font },
        formatter: (params: Array<{ dataIndex: number }>) => {
          const p = points[params[0]?.dataIndex ?? 0];
          if (!p) return '';
          // No se muestra pico: el maximo de la casa no se puede derivar de los maximos
          // por dispositivo (los picos no tienen por que coincidir en el tiempo).
          return `<b>${hourLabel(p.bucketLocal)}</b><br/>${kwh(p.kwh)} &middot; ${moneySmart(p.cost)}<br/>${watts(p.avgWatts)} promedio de la casa`;
        },
      },
      xAxis: {
        type: 'category',
        boundaryGap: false,
        data: points.map((p) => hourLabel(p.bucketLocal)),
        axisLine: { lineStyle: { color: t.baseline, width: 1 } },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
      },
      yAxis: {
        type: 'value',
        name: 'kWh',
        // nameGap separa el rotulo del tope del grafico; sin esto queda cortado por el borde.
        nameGap: 12,
        nameTextStyle: { color: t.muted, fontSize: 11, align: 'left' },
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
        splitLine: { lineStyle: { color: t.grid, width: 1, type: 'solid' } },
      },
      series: [
        {
          type: 'line',
          data: points.map((p) => p.kwh),
          lineStyle: { width: 2, color: t.series1, cap: 'round', join: 'round' },
          itemStyle: { color: t.series1, borderColor: t.surface, borderWidth: 2 },
          symbol: 'circle',
          symbolSize: 8,
          showSymbol: points.length <= 32,
          areaStyle: { color: t.series1Soft },
        },
      ],
    };
  });

  constructor() {
    void this.realtime.start();
    // Dato informativo: si falla, el dashboard igual sirve, solo no muestra el pie de ciclo.
    this.api.getCycleInfo().subscribe({
      next: (c) => this.cycleInfo.set(c),
      error: () => this.cycleInfo.set(null),
    });
    this.load(false);

    const timer = setInterval(() => this.load(true), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected changeDate(value: string): void {
    if (!value) return;
    this.date.set(value);
    this.load(true);
  }

  protected setMode(useCycle: boolean): void {
    if (this.useCycle() === useCycle) return;
    this.useCycle.set(useCycle);
    this.offset.set(0);
    this.load(true);
  }

  protected shiftPeriod(delta: number): void {
    this.offset.update((o) => Math.min(0, o + delta));
    this.load(true);
  }

  protected goToToday(): void {
    this.date.set(todayIso());
    this.offset.set(0);
    this.load(true);
  }

  protected reload(): void {
    this.load(true);
  }

  private load(isRefresh: boolean): void {
    if (isRefresh) this.refreshing.set(true);
    else this.loading.set(true);

    let pending = 2;
    const done = () => {
      if (--pending === 0) {
        this.loading.set(false);
        this.refreshing.set(false);
      }
    };

    // Las alertas no bloquean el render: si fallan, el dashboard igual sirve.
    this.api.getAlerts().subscribe({
      next: (a) => this.alerts.set(a),
      error: () => this.alerts.set([]),
    });

    this.api.getDaily(this.date()).subscribe({
      next: (d) => {
        this.daily.set(d);
        this.error.set(null);
        done();
      },
      error: (err) => {
        this.error.set(this.describe(err));
        done();
      },
    });

    this.api.getPeriod(this.useCycle(), this.offset()).subscribe({
      next: (p) => {
        this.period.set(p);
        done();
      },
      error: (err) => {
        this.error.set(this.describe(err));
        done();
      },
    });
  }

  private describe(err: unknown): string {
    const e = err as { status?: number; error?: { error?: string } };
    if (e?.status === 0) {
      return 'No se puede contactar la API. Revisa que este corriendo en http://localhost:5080.';
    }
    if (e?.status === 404) {
      return 'No hay ninguna tarifa cargada. Importa una factura desde Configuracion.';
    }
    return e?.error?.error ?? 'Error inesperado al consultar la API.';
  }
}
