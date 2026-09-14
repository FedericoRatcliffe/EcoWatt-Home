import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { Realtime } from '../../core/services/realtime';
import { Dashboard } from './dashboard';

/** Doble del hub: el test no abre WebSockets. */
const realtimeStub = {
  status: signal('conectado'),
  wattsByDevice: signal<Record<string, number>>({ 'dev-1': 275.5 }),
  lastReading: signal(null),
  newDeviceCount: signal(0),
  start: async () => {},
  stop: async () => {},
};

const daily = {
  date: '2026-09-13',
  totalKwh: 3.16,
  totalCost: 1096.5,
  marginalPricePerKwh: 346.1,
  devices: [],
  hourly: [
    { bucketLocal: '2026-09-13T00:00:00-03:00', kwh: 0.14, cost: 48.45, avgWatts: 140, maxWatts: 450 },
    { bucketLocal: '2026-09-13T01:00:00-03:00', kwh: 0.12, cost: 41.53, avgWatts: 120, maxWatts: 300 },
  ],
};

/** Cifras tomadas de la factura real 09/2026, para que el test hable el idioma del dominio. */
const period = {
  label: 'Ciclo 02/07 - 31/07',
  from: '2026-07-02',
  to: '2026-07-31',
  isBillingCycle: true,
  bill: {
    days: 29,
    totalKwh: 175,
    fixedCharge: 3539.01,
    variableCharge: 47014,
    basicAmount: 50553.01,
    blocks: [
      { label: 'Hasta 75 kWh', kwh: 75, pricePerKwh: 243.73, amount: 18279.75 },
      { label: 'Hasta 150 kWh', kwh: 75, pricePerKwh: 265.01, amount: 19875.75 },
      { label: 'Hasta 300 kWh', kwh: 25, pricePerKwh: 354.34, amount: 8858.5 },
    ],
    surcharges: [
      { name: 'IVA 21% Energia', rate: 0.21, amount: 10616.13 },
      { name: 'Cap.Inv.Bienes de Uso', rate: 0.1215, amount: 6142.19 },
    ],
    periodCharges: [
      { name: 'Tasa de Alum. Pub.', rate: null, amount: 6979 },
      { name: 'Ley Pcial. 12692', rate: null, amount: 230.81 },
    ],
    totalSurcharges: 21232.31,
    total: 78995.13,
    marginalPricePerKwh: 503.16,
    averagePricePerKwh: 451.4,
    energyCost: 66759.88,
    fixedCost: 12235.25,
  },
  previousPeriodCost: 83752.12,
  previousPeriodCostToDate: 83752.12,
  comparisonIsPartial: false,
  changePercentVsPreviousPeriod: -5.68,
  projection: null,
  devices: [
    {
      deviceId: 'dev-1',
      name: 'PC + monitores',
      location: 'Escritorio',
      kwh: 50,
      cost: 19074.25,
      currentWatts: 0,
      sharePercent: 56,
    },
    {
      deviceId: 'dev-2',
      name: 'Heladera',
      location: 'Cocina',
      kwh: 33,
      cost: 12589,
      currentWatts: 3,
      sharePercent: 33.1,
    },
  ],
  daily: [{ bucketLocal: '2026-07-02T00:00:00-03:00', kwh: 6, cost: 2075, avgWatts: 250, maxWatts: 900 }],
};

/**
 * Intl separa el simbolo de moneda con espacio duro (U+00A0) y a veces fino (U+202F).
 * Se normalizan para poder afirmar sobre el texto con espacios comunes.
 */
function visibleText(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/[  ]/g, ' ');
}

describe('Dashboard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: Realtime, useValue: realtimeStub },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  function flushAll(): void {
    http.expectOne((r) => r.url === '/api/dashboard/cycle').flush({
      anchor: '2026-07-31',
      cycleDays: 30,
      fromBill: true,
      source: 'fecha de lectura de la ultima factura importada',
    });
    http.expectOne((r) => r.url === '/api/dashboard/daily').flush(daily);
    http.expectOne((r) => r.url === '/api/dashboard/period').flush(period);
  }

  it('muestra la factura del ciclo, su desglose y las cards por dispositivo', async () => {
    const fixture = TestBed.createComponent(Dashboard);
    flushAll();

    // Se abre el detalle y las tablas: jsdom no tiene canvas, asi que ECharts no puede pintar.
    const component = fixture.componentInstance as unknown as {
      showDeviceTable: { set(v: boolean): void };
      showHourlyTable: { set(v: boolean): void };
      showDailyTable: { set(v: boolean): void };
      showBillDetail: { set(v: boolean): void };
    };
    component.showDeviceTable.set(true);
    component.showHourlyTable.set(true);
    component.showDailyTable.set(true);
    component.showBillDetail.set(true);

    await fixture.whenStable();
    const text = visibleText(fixture.nativeElement as HTMLElement);

    // Cifra principal: el total de la factura, no un kWh por un precio plano.
    expect(text).toContain('Ciclo 02/07 - 31/07');
    expect(text).toContain('$ 78.995');
    expect(text).toContain('5,7 % menos que el periodo anterior');

    // El costo se parte en lo que depende del consumo y lo que no.
    expect(text).toContain('Energia');
    expect(text).toContain('$ 66.760');
    expect(text).toContain('Cargos fijos');
    expect(text).toContain('$ 12.235');

    // Con tarifa por tramos lo que importa es el precio del proximo kWh.
    expect(text).toContain('Cada kWh extra');
    expect(text).toContain('$ 503');

    // Desglose con los mismos renglones que el papel.
    expect(text).toContain('Hasta 150 kWh');
    expect(text).toContain('IVA 21% Energia');
    expect(text).toContain('Tasa de Alum. Pub.');
    expect(text).toContain('Importe basico');

    // Cards por dispositivo, con la potencia en vivo del hub pisando la de la API.
    expect(text).toContain('PC + monitores');
    expect(text).toContain('276 W');
    expect(text).toContain('Heladera');
  });

  it('avisa cuando la API no responde', async () => {
    const fixture = TestBed.createComponent(Dashboard);

    http.expectOne((r) => r.url === '/api/dashboard/cycle').error(new ProgressEvent('error'), { status: 0 });
    http.expectOne((r) => r.url === '/api/dashboard/daily').error(new ProgressEvent('error'), { status: 0 });
    http.expectOne((r) => r.url === '/api/dashboard/period').error(new ProgressEvent('error'), { status: 0 });

    await fixture.whenStable();
    const text = visibleText(fixture.nativeElement as HTMLElement);

    expect(text).toContain('No se puede contactar la API');
  });
});
