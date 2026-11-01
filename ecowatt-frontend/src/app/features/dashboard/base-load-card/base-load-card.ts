import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { BaseLoad, BaseLoadDevice } from '../../../core/models/api.models';
import { EcowattApi } from '../../../core/services/ecowatt-api';
import { kwh, money, percent, watts } from '../../../shared/format';
import { SimulationPreset } from '../simulator-card/simulator-card';

/**
 * Lo que la casa gasta sin que nadie use nada. Se carga una vez: el numero cambia de noche
 * en noche, no de minuto en minuto como el resto del dashboard.
 */
@Component({
  selector: 'app-base-load-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './base-load-card.html',
  styleUrl: './base-load-card.scss',
})
export class BaseLoadCard {
  private readonly api = inject(EcowattApi);

  protected readonly watts = watts;
  protected readonly money = money;
  protected readonly kwh = kwh;
  protected readonly percent = percent;

  /** Pide simular que se corta el consumo base de una fila. */
  readonly simulate = output<SimulationPreset>();

  protected readonly data = signal<BaseLoad | null>(null);
  protected readonly failed = signal(false);

  /** Anoche contra lo normal, en palabras. Solo cuando hay noches para saber que es normal. */
  protected readonly lastNight = computed(() => {
    const d = this.data();
    if (d?.lastNightWatts == null || d.typicalWatts == null) return null;

    const diff = d.lastNightWatts - d.typicalWatts;
    const text =
      Math.abs(diff) < 10
        ? 'como siempre'
        : diff > 0
          ? `${watts(diff)} mas que lo normal`
          : `${watts(-diff)} menos que lo normal`;

    return { value: d.lastNightWatts, text, up: diff >= 10 };
  });

  constructor() {
    this.api.getBaseLoad().subscribe({
      next: (d) => this.data.set(d),
      error: () => this.failed.set(true),
    });
  }

  protected hourText(hour: number): string {
    return `${String(hour).padStart(2, '0')}:00`;
  }

  protected simulateCut(row: BaseLoadDevice): void {
    this.simulate.emit({
      label: `Cortar el consumo base de ${row.name}`,
      watts: row.watts,
      fromHour: 0,
      toHour: 0,
    });
  }
}
