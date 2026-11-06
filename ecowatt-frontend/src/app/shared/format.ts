/** Formateo comun. Todo en es-AR: separador de miles con punto, decimales con coma. */

const ARS = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
});

const ARS_CENTS = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const KWH = new Intl.NumberFormat('es-AR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const KWH_FINE = new Intl.NumberFormat('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 });
const WATTS = new Intl.NumberFormat('es-AR', { maximumFractionDigits: 0 });
const DECIMAL = new Intl.NumberFormat('es-AR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** Pesos redondeados: lo que va en titulos y cifras grandes. */
export function money(value: number | null | undefined): string {
  return ARS.format(value ?? 0);
}

/** Pesos con centavos: para montos chicos donde redondear a cero miente. */
export function moneyExact(value: number | null | undefined): string {
  return ARS_CENTS.format(value ?? 0);
}

/** Elige la precision segun la magnitud: $0,42 no puede mostrarse como $0. */
export function moneySmart(value: number | null | undefined): string {
  const v = value ?? 0;
  return Math.abs(v) < 100 ? ARS_CENTS.format(v) : ARS.format(v);
}

export function kwh(value: number | null | undefined): string {
  const v = value ?? 0;
  return `${(Math.abs(v) < 1 ? KWH_FINE : KWH).format(v)} kWh`;
}

export function watts(value: number | null | undefined): string {
  if (value === null || value === undefined) return '--';
  return `${(Math.abs(value) < 10 ? DECIMAL : WATTS).format(value)} W`;
}

export function percent(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined) return '--';
  return `${value.toFixed(digits).replace('.', ',')} %`;
}

/** "14:00" para los buckets horarios. */
export function hourLabel(iso: string): string {
  const d = new Date(iso);
  return `${String(d.getHours()).padStart(2, '0')}:00`;
}

/** "13/09" para los buckets diarios. */
export function dayLabel(iso: string): string {
  const d = new Date(iso);
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}`;
}

export function dateTimeLabel(iso: string): string {
  return new Date(iso).toLocaleString('es-AR', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

const MONTHS = [
  'enero',
  'febrero',
  'marzo',
  'abril',
  'mayo',
  'junio',
  'julio',
  'agosto',
  'septiembre',
  'octubre',
  'noviembre',
  'diciembre',
];

export function monthName(month: number): string {
  return MONTHS[Math.min(Math.max(month, 1), 12) - 1];
}

/** Fecha local de hoy como yyyy-MM-dd, sin pasar por UTC (toISOString corre el dia). */
export function todayIso(): string {
  const now = new Date();
  return [
    now.getFullYear(),
    String(now.getMonth() + 1).padStart(2, '0'),
    String(now.getDate()).padStart(2, '0'),
  ].join('-');
}

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];

/** "2,2 MB", "915 GB". Unidades de 1024, como las muestra Linux. */
export function bytes(value: number | null | undefined): string {
  if (value === null || value === undefined) return '--';
  let v = value;
  let unit = 0;
  while (Math.abs(v) >= 1024 && unit < BYTE_UNITS.length - 1) {
    v /= 1024;
    unit++;
  }
  const digits = unit === 0 || v >= 100 ? 0 : 1;
  return `${new Intl.NumberFormat('es-AR', { maximumFractionDigits: digits }).format(v)} ${BYTE_UNITS[unit]}`;
}

/** Una duracion en su unidad mas grande que importe: "45 s", "12 min", "5 h 20 min", "2 d 3 h". */
export function duration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '--';
  const s = Math.max(0, Math.round(seconds));
  if (s < 60) return `${s} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  if (h < 48) return m % 60 ? `${h} h ${m % 60} min` : `${h} h`;
  const d = Math.floor(h / 24);
  return h % 24 ? `${d} d ${h % 24} h` : `${d} d`;
}

/**
 * "hace 12 s" respecto de otro instante. El de referencia lo pone quien llama (la hora del
 * servidor en la pantalla de estado), asi un reloj de la PC corrido no inventa demoras.
 */
export function ago(iso: string | null | undefined, referenceIso: string): string {
  if (!iso) return 'nunca';
  return `hace ${duration((Date.parse(referenceIso) - Date.parse(iso)) / 1000)}`;
}
