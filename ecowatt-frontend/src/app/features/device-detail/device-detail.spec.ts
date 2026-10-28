import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { Realtime } from '../../core/services/realtime';
import { DeviceDetail } from './device-detail';

const DEVICE_ID = 'dev-1';

/** Doble del hub: el test no abre WebSockets. */
const realtimeStub = {
  status: signal('conectado'),
  wattsByDevice: signal<Record<string, number>>({}),
  relayByDevice: signal<Record<string, { deviceId: string; on: boolean; atUtc: string }>>({}),
  lastReading: signal(null),
  newDeviceCount: signal(0),
  start: async () => {},
  stop: async () => {},
};

const plug = {
  id: DEVICE_ID,
  name: 'Lavarropas',
  mqttTopic: 'plug-lavarropas',
  location: 'Lavadero',
  nominalWatts: 450,
  type: 'AthomPlugV3',
  role: 'Appliance',
  channelIndex: 0,
  isActive: true,
  createdAt: '2026-09-01T00:00:00Z',
  currentWatts: 1,
  lastSeenUtc: '2026-09-18T12:00:00Z',
  hasRelay: true,
  relayLocked: false,
  canToggleRelay: true,
  minRelayIntervalSeconds: 60,
  relayOn: false,
  relayStateAt: '2026-09-18T13:15:37Z',
};

const meter = {
  ...plug,
  id: 'meter-1',
  name: 'Medidor de tablero',
  mqttTopic: 'em2-tablero',
  type: 'AthomEm2',
  role: 'HouseMeter',
  hasRelay: false,
  canToggleRelay: false,
  relayOn: null,
  relayStateAt: null,
};

const history = {
  deviceId: DEVICE_ID,
  deviceName: 'Lavarropas',
  fromUtc: '2026-09-17T13:00:00Z',
  toUtc: '2026-09-18T13:00:00Z',
  totalKwh: 0.9,
  totalCost: 320.5,
  points: [],
};

/** Un intento ejecutado y uno frenado por la guarda. */
const relayHistory = [
  {
    deviceId: DEVICE_ID,
    deviceName: 'Lavarropas',
    requestedOn: true,
    source: 'Dashboard',
    outcome: 'BlockedTooSoon',
    wasSent: false,
    reason: 'Lavarropas se conmuto hace 0 s y tiene un minimo de 60 s entre cambios. Faltan 60 s.',
    createdAt: '2026-09-18T13:15:40Z',
  },
  {
    deviceId: DEVICE_ID,
    deviceName: 'Lavarropas',
    requestedOn: false,
    source: 'Dashboard',
    outcome: 'Sent',
    wasSent: true,
    reason: null,
    createdAt: '2026-09-18T13:15:37Z',
  },
];

/**
 * jsdom no tiene canvas, así que ECharts revienta al pintar. Se muestra la tabla en vez del
 * gráfico, que además es la versión de la ficha que se puede leer como texto.
 */
function showTableInsteadOfChart(fixture: { componentInstance: unknown }): void {
  (fixture.componentInstance as { showTable: { set(v: boolean): void } }).showTable.set(true);
}

/** Molde de lectura cruda: sólo importan el instante y los watts. */
const lectura = {
  deviceId: DEVICE_ID,
  deviceName: 'Lavarropas',
  timestampUtc: '2026-10-28T18:00:00Z',
  watts: 0,
  voltage: 228,
  amperage: 0,
  totalKwh: 0.1,
  todayKwh: 0.05,
  powerFactor: 0.9,
};

function visibleText(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/[  ]/g, ' ');
}

describe('DeviceDetail', () => {
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

  /** Monta el componente y responde las tres llamadas que hace al cargar. */
  async function render(device: { id: string }, commands: object[] = relayHistory) {
    const fixture = TestBed.createComponent(DeviceDetail);
    fixture.componentRef.setInput('id', device.id);
    await fixture.whenStable();

    http.expectOne((r) => r.url === `/api/devices/${device.id}`).flush(device);
    http.expectOne((r) => r.url === `/api/devices/${device.id}/relay-history`).flush(commands);
    http.expectOne((r) => r.url === `/api/devices/${device.id}/history`).flush(history);

    await fixture.whenStable();
    return fixture;
  }

  it('muestra los intentos ejecutados y los que la guarda freno', async () => {
    const fixture = await render(plug);
    const text = visibleText(fixture.nativeElement as HTMLElement);

    expect(text).toContain('Historial del rele');
    expect(text).toContain('enviado');
    expect(text).toContain('demasiado seguido');

    // El motivo se muestra tal cual lo manda el backend: solo el sabe cuanto falta.
    expect(text).toContain('Faltan 60 s');
  });

  it('distingue en el marcado lo que salio de lo que no', async () => {
    // El estado no puede depender solo del color: la palabra ya esta, y la clase permite
    // diferenciarlos sin repetir el texto.
    const fixture = await render(plug);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('.relay-log li');

    expect(rows.length).toBe(2);
    expect(rows[0].classList.contains('blocked')).toBe(true);
    expect(rows[1].classList.contains('blocked')).toBe(false);
  });

  it('no ofrece historial de rele en el medidor de tablero', async () => {
    // El EM2 solo mide: una seccion vacia ahi seria ruido.
    const fixture = await render(meter, []);
    const text = visibleText(fixture.nativeElement as HTMLElement);

    expect(text).not.toContain('Historial del rele');
    expect(text).toContain('no tiene rele');
  });

  it('avisa cuando todavia no se intento conmutar', async () => {
    const fixture = await render(plug, []);

    expect(visibleText(fixture.nativeElement as HTMLElement)).toContain(
      'Todavia no se intento conmutar',
    );
  });

  it('muestra el pico medido en lugar del nominal declarado', async () => {
    // El nominal lo cargamos a mano y nadie lo va a mantener; el pico lo sabe el sistema.
    const fixture = TestBed.createComponent(DeviceDetail);
    fixture.componentRef.setInput('id', DEVICE_ID);
    showTableInsteadOfChart(fixture);
    await fixture.whenStable();

    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}`).flush(plug);
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/relay-history`).flush([]);
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/history`).flush({
      ...history,
      points: [
        { bucketLocal: '2026-10-28T14:00:00-03:00', kwh: 0.05, cost: 25, avgWatts: 50, maxWatts: 186 },
      ],
    });

    await fixture.whenStable();
    const text = visibleText(fixture.nativeElement as HTMLElement);

    expect(text).toContain('pico medido');
    expect(text).toContain('186 W');
    expect(text).not.toContain('nominal declarado');
  });

  it('en el rango de 1 h grafica las muestras crudas y lista que se prendio o apago', async () => {
    const fixture = await render(plug, []);
    showTableInsteadOfChart(fixture);

    (fixture.componentInstance as unknown as { setRange(h: number): void }).setRange(1);
    await fixture.whenStable();

    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}`).flush(plug);
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/relay-history`).flush([]);
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/history`).flush(history);

    // Las crudas sólo se piden en este rango: en 7 o 30 días serían decenas de miles de puntos.
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/readings`).flush([
      { ...lectura, timestampUtc: '2026-10-28T18:27:21Z', watts: 93 },
      { ...lectura, timestampUtc: '2026-10-28T18:27:51Z', watts: 7 },
      { ...lectura, timestampUtc: '2026-10-28T18:28:21Z', watts: 8 },
      { ...lectura, timestampUtc: '2026-10-28T18:29:51Z', watts: 74 },
    ]);

    await fixture.whenStable();
    const text = visibleText(fixture.nativeElement as HTMLElement);

    // El salto de 93 a 7 W es un apagado; el de 8 a 74 W, un encendido. El de 7 a 8 W no:
    // esa es la fluctuación normal que el umbral tiene que ignorar.
    expect(text).toContain('Que paso en la ultima hora');
    expect(text).toContain('-86 W');
    expect(text).toContain('+66 W');
    expect(text).not.toContain('+1,0 W');
  });

  it('el rechazo de la guarda aparece en pantalla y recarga el historial', async () => {
    const fixture = await render(plug, []);

    (fixture.componentInstance as unknown as { setPower(on: boolean): void }).setPower(false);

    // El backend responde 409 con el motivo: no es una falla, es la guarda funcionando.
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/power`).flush(
      { error: 'Heladera tiene el rele bloqueado a proposito.', outcome: 'BlockedLocked' },
      { status: 409, statusText: 'Conflict' },
    );

    // Y el intento rechazado quedo auditado, asi que se vuelve a pedir la lista.
    http.expectOne((r) => r.url === `/api/devices/${DEVICE_ID}/relay-history`).flush(relayHistory);

    await fixture.whenStable();
    const text = visibleText(fixture.nativeElement as HTMLElement);

    expect(text).toContain('bloqueado a proposito');
    expect(text).toContain('demasiado seguido');
  });
});
