import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { EChartsCoreOption } from 'echarts/core';
import { Device, DeviceHistory } from '../../core/models/api.models';
import { EcowattApi } from '../../core/services/ecowatt-api';
import { Realtime } from '../../core/services/realtime';
import { Chart } from '../../shared/chart';
import { ChartThemeWatcher } from '../../shared/chart-theme';
import { dayLabel, hourLabel, kwh, money, moneySmart, percent, watts } from '../../shared/format';

interface RangeOption {
  label: string;
  hours: number;
}

const RANGES: RangeOption[] = [
  { label: '24 h', hours: 24 },
  { label: '7 dias', hours: 24 * 7 },
  { label: '30 dias', hours: 24 * 30 },
];

@Component({
  selector: 'app-device-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Chart],
  templateUrl: './device-detail.html',
  styleUrl: './device-detail.scss',
})
export class DeviceDetail {
  /** Viene del parametro de ruta con withComponentInputBinding(). */
  readonly id = input.required<string>();

  private readonly api = inject(EcowattApi);
  private readonly themeWatcher = inject(ChartThemeWatcher);
  protected readonly realtime = inject(Realtime);

  protected readonly money = money;
  protected readonly moneySmart = moneySmart;
  protected readonly kwh = kwh;
  protected readonly watts = watts;
  protected readonly percent = percent;
  protected readonly ranges = RANGES;

  protected readonly device = signal<Device | null>(null);
  protected readonly history = signal<DeviceHistory | null>(null);
  protected readonly hours = signal(24);
  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly showTable = signal(false);

  /** Potencia del hub si llego algo; si no, la ultima que devolvio la API. */
  protected readonly currentWatts = computed(() => {
    const live = this.realtime.wattsByDevice()[this.id()];
    return live ?? this.device()?.currentWatts ?? null;
  });

  protected readonly byHour = computed(() => this.hours() <= 48);

  protected readonly bucketLabel = computed(() => (this.byHour() ? hourLabel : dayLabel));

  /** Consumo por bucket: una sola serie, columnas finas con la punta redondeada. */
  protected readonly historyOption = computed<EChartsCoreOption>(() => {
    const t = this.themeWatcher.theme();
    const points = this.history()?.points ?? [];
    const label = this.bucketLabel();

    return {
      textStyle: { fontFamily: t.font },
      grid: { left: 4, right: 12, top: 16, bottom: 4, containLabel: true },
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
          return `<b>${label(p.bucketLocal)}</b><br/>${kwh(p.kwh)} &middot; ${moneySmart(p.cost)}<br/>${watts(p.avgWatts)} promedio &middot; pico ${watts(p.maxWatts)}`;
        },
      },
      xAxis: {
        type: 'category',
        data: points.map((p) => label(p.bucketLocal)),
        axisLine: { lineStyle: { color: t.baseline, width: 1 } },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
      },
      yAxis: {
        type: 'value',
        name: 'kWh',
        nameTextStyle: { color: t.muted, fontSize: 11, align: 'left' },
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
        splitLine: { lineStyle: { color: t.grid, width: 1, type: 'solid' } },
      },
      series: [
        {
          type: 'bar',
          data: points.map((p) => p.kwh),
          barMaxWidth: 24,
          // 2px de separacion entre barras vecinas, hechos con el hueco de la banda.
          barCategoryGap: '35%',
          itemStyle: { color: t.series1, borderRadius: [4, 4, 0, 0] },
        },
      ],
    };
  });

  constructor() {
    // input.required no esta disponible en el constructor: se lee en el primer effect implicito
    // del load, disparado por el propio router al bindear el parametro.
    queueMicrotask(() => this.load(false));
  }

  protected setRange(hours: number): void {
    this.hours.set(hours);
    this.load(true);
  }

  protected reload(): void {
    this.load(true);
  }

  protected setPower(on: boolean): void {
    this.notice.set(null);
    this.api.setPower(this.id(), on).subscribe({
      next: () => this.notice.set(`Comando ${on ? 'ON' : 'OFF'} enviado por MQTT.`),
      error: (err) =>
        this.notice.set(
          (err as { status?: number })?.status === 503
            ? 'No hay conexion con el broker MQTT, el comando no salio.'
            : 'No se pudo enviar el comando.',
        ),
    });
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

    this.api.getDevice(this.id()).subscribe({
      next: (d) => {
        this.device.set(d);
        this.error.set(null);
        done();
      },
      error: () => {
        this.error.set('No se encontro el dispositivo.');
        done();
      },
    });

    this.api.getDeviceHistory(this.id(), this.hours()).subscribe({
      next: (h) => {
        this.history.set(h);
        done();
      },
      error: () => {
        this.error.set('No se pudo traer el historial.');
        done();
      },
    });
  }
}
