import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Simulation } from '../../../core/models/api.models';
import { EcowattApi } from '../../../core/services/ecowatt-api';
import { kwh, money } from '../../../shared/format';

/** Un escenario armado: lo que llega del consumo base o de los atajos de la card. */
export interface SimulationPreset {
  label: string;
  /** Positivo = se deja de consumir; negativo = se suma. */
  watts: number;
  fromHour: number;
  /** Igual a fromHour = todo el dia. */
  toHour: number;
}

/**
 * Atajos para las decisiones que ya estan sobre la mesa. Los de cada enchufe no van aca:
 * salen del consumo base medido, con los watts reales.
 */
const SHORTCUTS: SimulationPreset[] = [
  // La PC que hace de servidor gasta 32-45 W en reposo; una Pi, ~5 W.
  { label: 'Servidor en una Raspberry Pi', watts: 35, fromHour: 0, toHour: 0 },
  { label: 'Estufa electrica 4 h por noche', watts: -2000, fromHour: 20, toHour: 0 },
];

/**
 * "Cuanto ahorro si...". La cuenta la hace la API reconstruyendo la factura entera con y sin
 * el cambio: con tarifa por tramos, watts por precio medio da cualquier cosa.
 */
@Component({
  selector: 'app-simulator-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './simulator-card.html',
  styleUrl: './simulator-card.scss',
})
export class SimulatorCard {
  private readonly api = inject(EcowattApi);

  protected readonly money = money;
  protected readonly kwh = kwh;
  protected readonly shortcuts = SHORTCUTS;
  protected readonly hours = Array.from({ length: 24 }, (_, h) => h);

  /** Escenario que manda otra card (el consumo base). Cada uno nuevo se calcula solo. */
  readonly preset = input<SimulationPreset | null>(null);

  protected readonly label = signal<string | null>(null);
  protected readonly cut = signal(true);
  protected readonly watts = signal(50);
  protected readonly fromHour = signal(0);
  protected readonly toHour = signal(0);

  protected readonly result = signal<Simulation | null>(null);
  protected readonly running = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly hoursPerDay = computed(() => {
    const span = (((this.toHour() - this.fromHour()) % 24) + 24) % 24;
    return span === 0 ? 24 : span;
  });

  /** El resultado en palabras: lo que importa es cuanto cambia la factura, no los kWh. */
  protected readonly summary = computed(() => {
    const r = this.result();
    if (!r) return null;

    const saves = r.saving >= 0;
    const tierChange =
      r.scenarioMarginalPrice !== r.baselineMarginalPrice
        ? `Ademas cambia de tramo: el kWh extra pasa de ${money(r.baselineMarginalPrice)} a ${money(r.scenarioMarginalPrice)}.`
        : null;

    return {
      saves,
      amount: money(Math.abs(r.saving)),
      yearly: money(Math.abs(r.yearlySaving)),
      tierChange,
    };
  });

  constructor() {
    effect(() => {
      const p = this.preset();
      if (p) this.apply(p);
    });
  }

  protected hourText(hour: number): string {
    return `${String(hour).padStart(2, '0')}:00`;
  }

  protected apply(p: SimulationPreset): void {
    this.label.set(p.label);
    this.cut.set(p.watts >= 0);
    this.watts.set(Math.round(Math.abs(p.watts)));
    this.fromHour.set(p.fromHour);
    this.toHour.set(p.toHour);
    this.run();
  }

  /** Tocar el formulario a mano deja de ser el escenario con nombre. */
  protected edit(update: () => void): void {
    update();
    this.label.set(null);
  }

  protected run(): void {
    const w = Number(this.watts());
    if (!Number.isFinite(w) || w <= 0) {
      this.error.set('Pone cuantos watts.');
      return;
    }

    this.running.set(true);
    this.error.set(null);

    this.api
      .simulate({ watts: this.cut() ? w : -w, fromHour: this.fromHour(), toHour: this.toHour() })
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.running.set(false);
        },
        error: (err) => {
          this.error.set(err?.error?.error ?? 'No se pudo simular.');
          this.running.set(false);
        },
      });
  }
}
