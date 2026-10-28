import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import type { EChartsCoreOption } from 'echarts/core';
import { Device, DeviceHistory, EnergyReading, RelayCommand } from '../../core/models/api.models';
import { EcowattApi } from '../../core/services/ecowatt-api';
import { Realtime } from '../../core/services/realtime';
import { Chart } from '../../shared/chart';
import { ChartThemeWatcher } from '../../shared/chart-theme';
import {
  dateTimeLabel,
  dayLabel,
  hourLabel,
  kwh,
  money,
  moneySmart,
  percent,
  watts,
} from '../../shared/format';

/** Cada cuánto se refrescan los agregados. Los watts en vivo llegan por SignalR. */
const REFRESH_MS = 60_000;

interface RangeOption {
  label: string;
  hours: number;
}

/**
 * Como se lee cada resultado de la guarda del rele. El enum del backend no le dice nada a
 * nadie, y el motivo en texto que lo acompana ya explica el caso puntual.
 */
const OUTCOME_LABELS: Record<string, string> = {
  Sent: 'enviado',
  BlockedLocked: 'bloqueado',
  BlockedTooSoon: 'demasiado seguido',
  BlockedNoRelay: 'sin rele',
  Failed: 'fallo',
};

/**
 * Salto de potencia a partir del cual se considera que algo se prendio o se apago, y no que
 * la carga fluctuo sola. Un televisor pasando de standby a encendido son ~70 W; la oscilacion
 * de una zapatilla en reposo, 1 o 2 W.
 */
const TRANSITION_WATTS = 15;

const RANGES: RangeOption[] = [
  { label: '1 h', hours: 1 },
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
  protected readonly dateTime = dateTimeLabel;
  protected readonly ranges = RANGES;

  protected readonly device = signal<Device | null>(null);
  protected readonly history = signal<DeviceHistory | null>(null);
  protected readonly hours = signal(24);
  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly showTable = signal(false);

  /** Intentos de conmutar el rele, incluidos los rechazados. */
  protected readonly relayHistory = signal<RelayCommand[]>([]);

  /** Potencia del hub si llego algo; si no, la ultima que devolvio la API. */
  protected readonly currentWatts = computed(() => {
    const live = this.realtime.wattsByDevice()[this.id()];
    return live ?? this.device()?.currentWatts ?? null;
  });

  /**
   * Estado del relé, con el del hub por encima del que trajo la API.
   *
   * Es lo que confirmó el equipo, no lo que se le pidió: si el comando se perdió o si alguien
   * apretó el botón físico del enchufe, acá se ve la realidad.
   */
  protected readonly relayOn = computed<boolean | null>(() => {
    const live = this.realtime.relayByDevice()[this.id()];
    return live ? live.on : (this.device()?.relayOn ?? null);
  });

  /** Lecturas crudas. Solo se piden en el rango de 1 h, que es el unico que las dibuja. */
  protected readonly raw = signal<EnergyReading[]>([]);

  /**
   * En 1 h se grafican las muestras crudas en watts, no kWh por hora.
   *
   * Un grafico de kWh por hora con una sola hora de datos es una barra suelta que no dice
   * nada. Con las muestras se ve la forma real del consumo: cuando prendio algo, cuanto
   * escalo, cuando se apago.
   */
  protected readonly isRawMode = computed(() => this.hours() <= 1);

  /**
   * Pico efectivamente medido en el rango. Reemplaza al "nominal declarado", que es un numero
   * que cargamos a mano y que nadie va a mantener.
   *
   * Es el maximo de las MUESTRAS, no el pico real: entre una muestra y la siguiente pasan 30 s
   * y un arranque de milisegundos no queda registrado.
   */
  protected readonly peakWatts = computed<number | null>(() => {
    const valores = this.isRawMode()
      ? this.raw().map((r) => r.watts)
      : (this.history()?.points ?? []).map((p) => p.maxWatts);

    return valores.length > 0 ? Math.max(...valores) : null;
  });

  /**
   * Momentos en que algo se prendio o se apago, derivados de las muestras crudas.
   *
   * Es lo que no se ve en el grafico de barras: el grafico muestra cuanta energia, esto
   * muestra que paso y cuando.
   */
  protected readonly transitions = computed(() => {
    const rows = this.raw();
    const out: { at: string; delta: number; to: number }[] = [];

    for (let i = 1; i < rows.length; i++) {
      const delta = rows[i].watts - rows[i - 1].watts;
      if (Math.abs(delta) >= TRANSITION_WATTS) {
        out.push({ at: rows[i].timestampUtc, delta, to: rows[i].watts });
      }
    }

    return out.reverse();
  });

  protected readonly byHour = computed(() => this.hours() <= 48);

  protected readonly bucketLabel = computed(() => (this.byHour() ? hourLabel : dayLabel));

  /** Curva de potencia muestra a muestra, para el rango de 1 h. */
  protected readonly rawOption = computed<EChartsCoreOption>(() => {
    const t = this.themeWatcher.theme();
    const rows = this.raw();
    const hora = (iso: string) =>
      new Date(iso).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' });

    return {
      textStyle: { fontFamily: t.font },
      grid: { left: 4, right: 12, top: 16, bottom: 4, containLabel: true },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'line' },
        backgroundColor: t.surface,
        borderColor: t.baseline,
        borderWidth: 1,
        borderRadius: 10,
        extraCssText: 'box-shadow: 0 6px 20px rgba(0, 40, 80, 0.12);',
        textStyle: { color: t.textPrimary, fontSize: 12, fontFamily: t.font },
        formatter: (params: Array<{ dataIndex: number }>) => {
          const r = rows[params[0]?.dataIndex ?? 0];
          if (!r) return '';
          return `<b>${hora(r.timestampUtc)}</b><br/>${watts(r.watts)}<br/>${r.voltage} V &middot; ${r.amperage} A`;
        },
      },
      xAxis: {
        type: 'category',
        data: rows.map((r) => hora(r.timestampUtc)),
        axisLine: { lineStyle: { color: t.baseline, width: 1 } },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
      },
      yAxis: {
        type: 'value',
        name: 'W',
        nameTextStyle: { color: t.muted, fontSize: 11, align: 'left' },
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: t.muted, fontSize: 11 },
        splitLine: { lineStyle: { color: t.grid, width: 1, type: 'solid' } },
      },
      series: [
        {
          type: 'line',
          data: rows.map((r) => r.watts),
          // Escalonada y no suavizada: entre dos muestras no sabemos que paso, y una curva
          // redondeada inventaria una transicion gradual que puede no haber existido.
          step: 'end',
          symbol: 'none',
          lineStyle: { color: t.series1, width: 2 },
          areaStyle: { color: t.series1Soft },
        },
      ],
    };
  });

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

    // El consumo y el costo del período son agregados que se calculan en el backend, así que
    // no los mueve el hub: sin esto la ficha se queda congelada en lo que había al abrirla.
    const timer = setInterval(() => this.load(true), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected setRange(hours: number): void {
    this.hours.set(hours);
    this.load(true);
  }

  protected reload(): void {
    this.load(true);
  }

  /** Como se lee un resultado de la guarda. Si apareciera uno nuevo, se muestra tal cual. */
  protected outcomeLabel(outcome: string): string {
    return OUTCOME_LABELS[outcome] ?? outcome;
  }

  protected setPower(on: boolean): void {
    this.notice.set(null);
    this.api.setPower(this.id(), on).subscribe({
      next: () => {
        this.notice.set(`Comando ${on ? 'ON' : 'OFF'} enviado por MQTT.`);
        this.loadRelayHistory();
      },
      error: (err) => {
        const response = err as { status?: number; error?: { error?: string } };

        // 409 no es una falla: es la guarda del rele haciendo su trabajo. El motivo viene
        // del backend porque solo el sabe cuanto falta para la proxima conmutacion.
        if (response?.status === 409) {
          this.notice.set(response.error?.error ?? 'El rele no se puede conmutar ahora.');
          // El rechazo tambien quedo auditado: se recarga para que se vea en la lista.
          this.loadRelayHistory();
          return;
        }

        this.notice.set(
          response?.status === 503
            ? 'No hay conexion con el broker MQTT, el comando no salio.'
            : 'No se pudo enviar el comando.',
        );

        // Un fallo del broker tambien deja rastro; uno de red del navegador no.
        if (response?.status === 503) {
          this.loadRelayHistory();
        }
      },
    });
  }

  /**
   * El historial es informacion de apoyo: si no se puede traer, la pagina sigue sirviendo.
   * Por eso no entra en el contador de carga ni levanta el error de la pantalla.
   */
  private loadRelayHistory(): void {
    this.api.getRelayHistory(this.id()).subscribe({
      next: (rows) => this.relayHistory.set(rows),
      error: () => this.relayHistory.set([]),
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

    this.loadRelayHistory();

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

    this.loadRaw();
  }

  /**
   * Las muestras crudas de la ultima hora. Solo hacen falta en el rango de 1 h: en 7 o 30 dias
   * serian decenas de miles de puntos para dibujar una curva que nadie puede leer.
   */
  private loadRaw(): void {
    if (!this.isRawMode()) {
      this.raw.set([]);
      return;
    }

    const to = new Date();
    const from = new Date(to.getTime() - 60 * 60 * 1000);

    this.api.getDeviceReadings(this.id(), from, to, 2000).subscribe({
      next: (rows) => this.raw.set(rows),
      // Es informacion de apoyo: si falla, el resto de la ficha sigue sirviendo.
      error: () => this.raw.set([]),
    });
  }
}
