/** Espejo de los DTOs de EcoWattCasa.Application. */

export type DeviceType = 'SonoffPowR2' | 'Esp32Sct013' | 'Simulated';

export interface Device {
  id: string;
  name: string;
  /** Nombre de topic de Tasmota, sin prefijos: "sonoff-pc". */
  mqttTopic: string;
  location: string;
  nominalWatts: number;
  type: DeviceType;
  isActive: boolean;
  createdAt: string;
  /** Potencia de la ultima lectura reciente. null si no reporta hace mas de una hora. */
  currentWatts: number | null;
  lastSeenUtc: string | null;
}

export interface DeviceInput {
  name: string;
  mqttTopic: string;
  location: string;
  nominalWatts: number;
  type: DeviceType;
  isActive?: boolean;
}

export interface EnergyReading {
  deviceId: string;
  deviceName: string;
  timestampUtc: string;
  watts: number;
  voltage: number;
  amperage: number;
  totalKwh: number | null;
  todayKwh: number | null;
  powerFactor: number | null;
}

export interface HistoryPoint {
  /** Inicio del bucket, ya en hora de la casa. */
  bucketLocal: string;
  kwh: number;
  cost: number;
  avgWatts: number;
  maxWatts: number;
}

export interface DeviceConsumption {
  deviceId: string;
  name: string;
  location: string;
  kwh: number;
  /** Parte del costo de energia que le toca segun su proporcion de kWh. */
  cost: number;
  currentWatts: number | null;
  sharePercent: number;
}

// ---------- Factura ----------

export interface BillBlock {
  label: string;
  kwh: number;
  pricePerKwh: number;
  amount: number;
}

export interface BillCharge {
  name: string;
  /** Fraccion sobre el importe basico. null para los cargos de monto fijo. */
  rate: number | null;
  amount: number;
}

export interface Bill {
  days: number;
  totalKwh: number;
  fixedCharge: number;
  variableCharge: number;
  basicAmount: number;
  blocks: BillBlock[];
  surcharges: BillCharge[];
  periodCharges: BillCharge[];
  totalSurcharges: number;
  total: number;
  /** Lo que cuesta el proximo kWh, con impuestos. Sube al pasar de tramo. */
  marginalPricePerKwh: number;
  averagePricePerKwh: number;
  /** Lo que depende del consumo: se reparte entre los dispositivos. */
  energyCost: number;
  /** Cargo fijo + alumbrado + renovables: se paga aunque no se consuma nada. */
  fixedCost: number;
}

export interface Projection {
  elapsedDays: number;
  totalDays: number;
  projectedKwh: number;
  projectedCost: number;
  /** Precio medio al cierre: el del período en curso está inflado por los cargos fijos. */
  averagePricePerKwh: number;
}

export interface DailyDashboard {
  date: string;
  totalKwh: number;
  totalCost: number;
  marginalPricePerKwh: number;
  devices: DeviceConsumption[];
  hourly: HistoryPoint[];
}

export interface PeriodDashboard {
  label: string;
  from: string;
  to: string;
  isBillingCycle: boolean;
  bill: Bill;
  /** Total del período anterior, completo. */
  previousPeriodCost: number;
  /** Lo que iba el período anterior a esta misma altura: contra esto se compara. */
  previousPeriodCostToDate: number;
  comparisonIsPartial: boolean;
  changePercentVsPreviousPeriod: number | null;
  projection: Projection | null;
  devices: DeviceConsumption[];
  daily: HistoryPoint[];
}

export interface CycleInfo {
  anchor: string;
  cycleDays: number;
  fromBill: boolean;
  source: string;
}

export interface DeviceHistory {
  deviceId: string;
  deviceName: string;
  fromUtc: string;
  toUtc: string;
  totalKwh: number;
  totalCost: number;
  points: HistoryPoint[];
}

// ---------- Tarifa ----------

export interface TariffBlock {
  order: number;
  label: string;
  upToKwh: number | null;
  pricePerKwh: number;
}

export interface TariffSurcharge {
  name: string;
  rate: number;
}

export interface TariffPeriodCharge {
  name: string;
  amount: number;
}

export interface TariffSchedule {
  id: string;
  validFrom: string;
  fixedChargePerDay: number;
  source: string;
  createdAt: string;
  blocks: TariffBlock[];
  surcharges: TariffSurcharge[];
  periodCharges: TariffPeriodCharge[];
  totalSurchargeRate: number;
  marginalPricePerKwh: number;
}

export interface TariffInput {
  validFrom?: string | null;
  fixedChargePerDay: number;
  source: string;
  blocks: { order: number; label: string; upToKwh: number | null; pricePerKwh: number }[];
  surcharges: { name: string; rate: number }[];
  periodCharges: { name: string; amount: number }[];
}

export interface ImportedBill {
  id: string;
  invoiceNumber: string;
  period: string;
  readingFrom: string;
  readingTo: string;
  days: number;
  kwh: number;
  basicAmount: number;
  totalTaxes: number;
  total: number;
  averagePricePerKwh: number;
  importedAt: string;
}

export interface BillImportResult {
  bill: ImportedBill;
  schedulesCreated: TariffSchedule[];
  /** Total que da el sistema con lo que leyo del PDF. Deberia coincidir con el impreso. */
  recalculatedTotal: number;
  differenceVsPrinted: number;
  warnings: string[];
}
