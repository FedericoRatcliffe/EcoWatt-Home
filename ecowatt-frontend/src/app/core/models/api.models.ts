/** Espejo de los DTOs de EcoWattCasa.Application. */

/** Modelo de hardware: el medidor de riel DIN o el enchufe con relé. */
export type DeviceType = 'AthomEm2' | 'AthomPlugV3';

/** Qué mide: toda la casa desde el tablero, o un aparato puntual. */
export type DeviceRole = 'HouseMeter' | 'Appliance';

export interface Device {
  id: string;
  name: string;
  /** Nombre de topic de Tasmota, sin prefijos: "plug-heladera". */
  mqttTopic: string;
  location: string;
  nominalWatts: number;
  type: DeviceType;
  role: DeviceRole;
  /** Canal de energía del payload. Los enchufes tienen uno solo; el EM2, dos. */
  channelIndex: number;
  isActive: boolean;
  createdAt: string;
  /** Potencia de la ultima lectura reciente. null si no reporta hace mas de una hora. */
  currentWatts: number | null;
  lastSeenUtc: string | null;
  /** El modelo tiene relé. El EM2 es sólo medición. */
  hasRelay: boolean;
  relayLocked: boolean;
  /** Si el botón de encendido va habilitado. No incluye la ventana de tiempo mínimo. */
  canToggleRelay: boolean;
  minRelayIntervalSeconds: number;
  /** Último estado conocido del relé. null = todavía no llegó ninguno. */
  relayOn: boolean | null;
  relayStateAt: string | null;
}

/** Estado del relé confirmado por el propio equipo en stat/{topic}/POWER. */
export interface RelayState {
  deviceId: string;
  on: boolean;
  atUtc: string;
}

/** Un intento de conmutar un relé, ejecutado o rechazado. */
export interface RelayCommand {
  deviceId: string;
  deviceName: string;
  requestedOn: boolean;
  /** Dashboard | Automation | System. */
  source: string;
  /** Sent | BlockedLocked | BlockedTooSoon | BlockedNoRelay | Failed. */
  outcome: string;
  wasSent: boolean;
  reason: string | null;
  createdAt: string;
}

export interface DeviceInput {
  name: string;
  mqttTopic: string;
  location: string;
  nominalWatts: number;
  type: DeviceType;
  role: DeviceRole;
  channelIndex: number;
  relayLocked: boolean;
  minRelayIntervalSeconds: number;
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
  /**
   * No es un dispositivo: es el consumo de la casa que no pasa por ningún enchufe medido.
   * Viene como una fila más para que el desglose sume el total, pero no tiene ficha propia.
   */
  isUnidentified: boolean;
}

/** Qué parte del consumo de la casa se mide enchufe por enchufe y qué parte no. */
export interface HouseSplit {
  houseKwh: number;
  measuredKwh: number;
  unidentifiedKwh: number;
  measuredSharePercent: number;
  /** Sin medidor de tablero el total es sólo la suma de los enchufes: mide de menos. */
  hasMeter: boolean;
  /** Los enchufes midieron más que el tablero: configuración mal puesta, no consumo. */
  measuredExceedsHouse: boolean;
  /** Potencia de toda la casa ahora. */
  currentWatts: number;
  /**
   * Qué dispositivos componen el total. Hacen falta para actualizar la potencia de la casa
   * con lo que llega por el hub: el medidor no tiene fila en el desglose.
   */
  meterDeviceIds: string[];
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
  house: HouseSplit;
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
  house: HouseSplit;
}

export type AlertSeverity = 'Info' | 'Warning' | 'Critical';

export interface Alert {
  /** Identificador estable de la regla: block-crossing, device-silent, telemetry-down... */
  code: string;
  severity: AlertSeverity;
  title: string;
  detail: string;
  deviceId: string | null;
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
