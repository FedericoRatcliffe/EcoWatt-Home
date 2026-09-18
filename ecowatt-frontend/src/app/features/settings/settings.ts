import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  BillImportResult,
  Device,
  DeviceInput,
  DeviceRole,
  DeviceType,
  ImportedBill,
  TariffInput,
  TariffSchedule,
} from '../../core/models/api.models';
import { EcowattApi } from '../../core/services/ecowatt-api';
import { money, moneyExact, percent, todayIso, watts } from '../../shared/format';

const EMPTY_DEVICE: DeviceInput = {
  name: '',
  mqttTopic: '',
  location: '',
  nominalWatts: 0,
  type: 'AthomPlugV3',
  // Aparato por defecto, nunca medidor: uno marcado por error como medidor de tablero
  // corrompe el total de la casa, uno marcado de mas como aparato solo sobra en el desglose.
  role: 'Appliance',
  channelIndex: 0,
  relayLocked: false,
  minRelayIntervalSeconds: 60,
  isActive: true,
};

/** Fila editable del formulario de tarifa. */
interface BlockRow {
  order: number;
  label: string;
  upToKwh: number | null;
  pricePerKwh: number;
}

interface ChargeRow {
  name: string;
  /** Porcentaje tal como se escribe en la UI (21, no 0.21). */
  value: number;
}

@Component({
  selector: 'app-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly api = inject(EcowattApi);

  protected readonly money = money;
  protected readonly moneyExact = moneyExact;
  protected readonly percent = percent;
  protected readonly watts = watts;
  protected readonly deviceTypes: DeviceType[] = ['AthomPlugV3', 'AthomEm2'];
  protected readonly deviceRoles: DeviceRole[] = ['Appliance', 'HouseMeter'];

  protected readonly tariff = signal<TariffSchedule | null>(null);
  protected readonly tariffHistory = signal<TariffSchedule[]>([]);
  protected readonly bills = signal<ImportedBill[]>([]);
  protected readonly devices = signal<Device[]>([]);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  // ---------- Import de PDF ----------
  protected readonly importing = signal(false);
  protected readonly importResult = signal<BillImportResult | null>(null);

  // ---------- Formulario de tarifa ----------
  protected readonly validFrom = signal(todayIso());
  protected readonly fixedChargePerDay = signal(0);
  protected readonly blocks = signal<BlockRow[]>([]);
  protected readonly surcharges = signal<ChargeRow[]>([]);
  protected readonly periodCharges = signal<ChargeRow[]>([]);
  protected readonly savingTariff = signal(false);

  /** Suma de los recargos: en la tarifa de Venado Tuerto tiene que dar 42%. */
  protected readonly totalSurchargePercent = computed(() =>
    this.surcharges().reduce((sum, s) => sum + (s.value || 0), 0),
  );

  // ---------- Formulario de dispositivo ----------
  protected readonly form = signal<DeviceInput>({ ...EMPTY_DEVICE });
  protected readonly editingId = signal<string | null>(null);
  protected readonly savingDevice = signal(false);

  constructor() {
    this.loadAll();
  }

  // ---------- Tarifa ----------

  protected addBlock(): void {
    this.blocks.update((rows) => [
      ...rows,
      { order: rows.length + 1, label: `Tramo ${rows.length + 1}`, upToKwh: null, pricePerKwh: 0 },
    ]);
  }

  protected removeBlock(index: number): void {
    this.blocks.update((rows) => rows.filter((_, i) => i !== index).map((r, i) => ({ ...r, order: i + 1 })));
  }

  protected patchBlock(index: number, patch: Partial<BlockRow>): void {
    this.blocks.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  protected addSurcharge(): void {
    this.surcharges.update((rows) => [...rows, { name: '', value: 0 }]);
  }

  protected removeSurcharge(index: number): void {
    this.surcharges.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected patchSurcharge(index: number, patch: Partial<ChargeRow>): void {
    this.surcharges.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  protected addPeriodCharge(): void {
    this.periodCharges.update((rows) => [...rows, { name: '', value: 0 }]);
  }

  protected removePeriodCharge(index: number): void {
    this.periodCharges.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected patchPeriodCharge(index: number, patch: Partial<ChargeRow>): void {
    this.periodCharges.update((rows) => rows.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  protected saveTariff(): void {
    const blocks = this.blocks();
    if (blocks.length === 0) {
      this.error.set('La tarifa necesita al menos un tramo.');
      return;
    }

    if (blocks.some((b) => b.pricePerKwh <= 0)) {
      this.error.set('Todos los tramos necesitan un precio mayor a cero.');
      return;
    }

    const input: TariffInput = {
      validFrom: this.validFrom() || null,
      fixedChargePerDay: this.fixedChargePerDay(),
      source: 'Carga manual',
      blocks: blocks.map((b) => ({
        order: b.order,
        label: b.label,
        upToKwh: b.upToKwh,
        pricePerKwh: b.pricePerKwh,
      })),
      // En la UI se escribe 21 y se guarda 0,21.
      surcharges: this.surcharges()
        .filter((s) => s.name.trim())
        .map((s) => ({ name: s.name.trim(), rate: s.value / 100 })),
      periodCharges: this.periodCharges()
        .filter((c) => c.name.trim())
        .map((c) => ({ name: c.name.trim(), amount: c.value })),
    };

    this.savingTariff.set(true);
    this.error.set(null);

    this.api.saveTariff(input).subscribe({
      next: () => {
        this.notice.set('Tarifa guardada. Los periodos anteriores mantienen su precio.');
        this.savingTariff.set(false);
        this.loadAll();
      },
      error: (err) => {
        this.error.set(this.describe(err));
        this.savingTariff.set(false);
      },
    });
  }

  protected editSchedule(schedule: TariffSchedule): void {
    this.validFrom.set(schedule.validFrom);
    this.fixedChargePerDay.set(schedule.fixedChargePerDay);
    this.blocks.set(schedule.blocks.map((b) => ({ ...b })));
    this.surcharges.set(schedule.surcharges.map((s) => ({ name: s.name, value: s.rate * 100 })));
    this.periodCharges.set(schedule.periodCharges.map((c) => ({ name: c.name, value: c.amount })));
    this.notice.set(`Cargado el cuadro del ${schedule.validFrom} en el formulario.`);
  }

  protected deleteSchedule(schedule: TariffSchedule): void {
    if (!confirm(`Borrar el cuadro vigente desde ${schedule.validFrom}?`)) return;

    this.api.deleteTariff(schedule.id).subscribe({
      next: () => {
        this.notice.set('Cuadro borrado.');
        this.loadAll();
      },
      error: (err) => this.error.set(this.describe(err)),
    });
  }

  // ---------- Import ----------

  protected importPdf(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.importing.set(true);
    this.error.set(null);
    this.importResult.set(null);

    this.api.importBill(file).subscribe({
      next: (result) => {
        this.importResult.set(result);
        this.notice.set(
          `Factura ${result.bill.period} importada: ${result.schedulesCreated.length} cuadro(s) tarifario(s).`,
        );
        this.importing.set(false);
        input.value = '';
        this.loadAll();
      },
      error: (err) => {
        this.error.set(this.describe(err));
        this.importing.set(false);
        input.value = '';
      },
    });
  }

  protected deleteBill(bill: ImportedBill): void {
    if (!confirm(`Borrar la factura ${bill.period}? Las tarifas que cargo quedan.`)) return;

    this.api.deleteBill(bill.id).subscribe({
      next: () => {
        this.notice.set('Factura borrada.');
        this.loadAll();
      },
      error: (err) => this.error.set(this.describe(err)),
    });
  }

  // ---------- Dispositivos ----------

  /** Los nombres del enum no le dicen nada a nadie: se muestran por el modelo comprado. */
  protected typeLabel(type: DeviceType): string {
    return type === 'AthomEm2' ? 'Athom EM2 (medidor de riel DIN)' : 'Athom Plug V3 (enchufe con rele)';
  }

  protected roleLabel(role: DeviceRole): string {
    return role === 'HouseMeter' ? 'Toda la casa (tablero)' : 'Un aparato';
  }

  protected startNewDevice(): void {
    this.editingId.set(null);
    this.form.set({ ...EMPTY_DEVICE });
  }

  protected startEdit(device: Device): void {
    this.editingId.set(device.id);
    this.form.set({
      name: device.name,
      mqttTopic: device.mqttTopic,
      location: device.location,
      nominalWatts: device.nominalWatts,
      type: device.type,
      role: device.role,
      channelIndex: device.channelIndex,
      relayLocked: device.relayLocked,
      minRelayIntervalSeconds: device.minRelayIntervalSeconds,
      isActive: device.isActive,
    });
  }

  protected saveDevice(): void {
    const input = this.form();
    if (!input.name.trim() || !input.mqttTopic.trim()) {
      this.error.set('Nombre y topic MQTT son obligatorios.');
      return;
    }

    this.savingDevice.set(true);
    this.error.set(null);

    const id = this.editingId();
    const request = id ? this.api.updateDevice(id, input) : this.api.createDevice(input);

    request.subscribe({
      next: () => {
        this.notice.set(id ? 'Dispositivo actualizado.' : 'Dispositivo agregado.');
        this.savingDevice.set(false);
        this.startNewDevice();
        this.loadAll();
      },
      error: (err) => {
        this.error.set(this.describe(err));
        this.savingDevice.set(false);
      },
    });
  }

  protected deleteDevice(device: Device): void {
    if (!confirm(`Borrar "${device.name}" y todas sus lecturas? No se puede deshacer.`)) return;

    this.api.deleteDevice(device.id).subscribe({
      next: () => {
        this.notice.set(`"${device.name}" borrado.`);
        if (this.editingId() === device.id) this.startNewDevice();
        this.loadAll();
      },
      error: (err) => this.error.set(this.describe(err)),
    });
  }

  protected patchForm(patch: Partial<DeviceInput>): void {
    this.form.update((current) => ({ ...current, ...patch }));
  }

  private loadAll(): void {
    this.api.getTariff().subscribe({
      next: (t) => {
        this.tariff.set(t);
        // El formulario arranca precargado con la tarifa vigente: casi siempre se edita un precio.
        if (this.blocks().length === 0) this.editSchedule(t);
        this.notice.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        // 404 = no hay tarifa todavia; no es un error que haya que gritar.
        if ((err as { status?: number })?.status !== 404) this.error.set(this.describe(err));
        this.loading.set(false);
      },
    });

    this.api.getTariffHistory().subscribe({ next: (h) => this.tariffHistory.set(h) });
    this.api.getBills().subscribe({ next: (b) => this.bills.set(b) });
    this.api.getDevices().subscribe({ next: (d) => this.devices.set(d) });
  }

  private describe(err: unknown): string {
    const e = err as { status?: number; error?: { detail?: string; title?: string; error?: string } };
    if (e?.status === 0) return 'No se puede contactar la API en http://localhost:5080.';
    return e?.error?.error ?? e?.error?.detail ?? e?.error?.title ?? 'No se pudo guardar.';
  }
}
