import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ServerStatus } from '../../core/models/api.models';
import { Server } from './server';

/** El servidor real el 06/11/2026, todo bien. */
const healthy: ServerStatus = {
  generatedUtc: '2026-11-06T16:15:30Z',
  overall: 'Ok',
  checks: [],
  system: {
    cpuPercent: 3.1,
    cpuCores: 2,
    load1: 0.08,
    load5: 0.05,
    load15: 0.07,
    memoryTotalBytes: 3_442_565_120,
    memoryUsedBytes: 999_473_152,
    memoryUsedPercent: 29,
    swapTotalBytes: 5_499_293_696,
    swapUsedBytes: 1_716_224,
    swapUsedPercent: 0,
    diskTotalBytes: 982_473_670_656,
    diskUsedBytes: 12_884_901_888,
    diskUsedPercent: 1.3,
    uptimeSeconds: 178_387,
  },
  report: {
    generatedUtc: '2026-11-06T16:15:02Z',
    osName: 'Ubuntu 26.04.1 LTS',
    deployedCommit: 'b331478',
    rebootRequired: false,
    containers: [
      {
        name: 'ecowatt-api',
        state: 'running',
        health: null,
        startedUtc: '2026-11-04T16:25:30Z',
        restartCount: 0,
        cpuPercent: 0.3,
        memoryBytes: 193_986_560,
        image: 'ecowatt-api',
      },
      {
        name: 'ecowatt-postgres',
        state: 'running',
        health: 'healthy',
        startedUtc: '2026-11-04T14:42:08Z',
        restartCount: 0,
        cpuPercent: 3,
        memoryBytes: 97_874_083,
        image: 'postgres:18-alpine',
      },
    ],
    disk: {
      device: '/dev/sda',
      passed: true,
      temperatureC: 32,
      powerOnHours: 11_283,
      reallocatedSectors: 0,
      pendingSectors: 0,
      uncorrectableSectors: 0,
    },
    backups: {
      count: 3,
      lastFile: 'ecowatt-2026-11-06.dump',
      lastUtc: '2026-11-06T06:30:03Z',
      lastSizeBytes: 2_302_308,
      lastLogLine: '2026-11-06 03:30:03 ok 2,2M',
      lastLogOk: true,
    },
    errors: [],
  },
  app: {
    apiUptimeSeconds: 172_800,
    apiMemoryBytes: 193_986_560,
    databaseSizeBytes: 58_000_000,
    rawReadingsEstimate: 290_000,
    lastRolledUpHourUtc: '2026-11-06T15:00:00Z',
    mqtt: { listenerConnected: true, connectedClients: 3, clientIds: ['ecowatt-api', 'medidor-tablero', 'plug-fede'] },
    devices: [
      { id: 'm', name: 'Medidor de tablero', lastSeenUtc: '2026-11-06T16:15:20Z', silent: false },
      { id: 'h', name: 'Heladera', lastSeenUtc: '2026-11-06T16:15:10Z', silent: false },
    ],
  },
};

function text(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/\s+/g, ' ');
}

describe('Server', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => vi.useRealTimers());

  it('con todo bien muestra el estado, el sistema, los contenedores, el disco y los backups', async () => {
    const fixture = TestBed.createComponent(Server);
    http.expectOne('/api/server/status').flush(healthy);
    await fixture.whenStable();

    const t = text(fixture.nativeElement as HTMLElement);
    expect(t).toContain('Todo bien');
    expect(t).toContain('Ubuntu 26.04.1 LTS');
    expect(t).toContain('b331478');
    expect(t).toContain('2 nucleos');
    expect(t).toContain('2 d 1 h'); // prendido hace 49 h
    expect(t).toContain('ecowatt-postgres');
    expect(t).toContain('corriendo, sano');
    expect(t).toContain('2,2 MB');
    expect(t).toContain('hace 9 h 45 min'); // ultimo backup, contra la hora del servidor
    expect(t).toContain('32 grados');
    expect(t).toContain('todos reportan');
  });

  it('un problema se dice con palabras, no solo con color', async () => {
    const fixture = TestBed.createComponent(Server);
    http.expectOne('/api/server/status').flush({
      ...healthy,
      overall: 'Critical',
      checks: [
        {
          code: 'container-down',
          severity: 'Critical',
          title: 'ecowatt-postgres no esta corriendo',
          detail: 'Estado: exited.',
          deviceId: null,
        },
        {
          code: 'reboot-required',
          severity: 'Info',
          title: 'Hay que reiniciar el servidor',
          detail: 'Una actualizacion automatica lo pide.',
          deviceId: null,
        },
      ],
    });
    await fixture.whenStable();

    const t = text(fixture.nativeElement as HTMLElement);
    expect(t).toContain('Hay un problema');
    expect(t).toContain('1 cosa para revisar'); // el Info no cuenta como problema
    expect(t).toContain('Critico');
    expect(t).toContain('ecowatt-postgres no esta corriendo');
    expect(t).toContain('Hay que reiniciar el servidor');
  });

  it('en desarrollo, sin /proc ni script, explica de donde salen los datos', async () => {
    const fixture = TestBed.createComponent(Server);
    http.expectOne('/api/server/status').flush({ ...healthy, system: null, report: null });
    await fixture.whenStable();

    const t = text(fixture.nativeElement as HTMLElement);
    expect(t).toContain('solo se ven con la API corriendo en el servidor Linux');
    expect(t).toContain('ecowatt-status.py');
  });

  it('se refresca solo, y si la API deja de contestar conserva lo ultimo que supo', async () => {
    const fixture = TestBed.createComponent(Server);
    http.expectOne('/api/server/status').flush(healthy);
    await fixture.whenStable();

    vi.advanceTimersByTime(5_000);
    http.expectOne('/api/server/status').flush('', { status: 502, statusText: 'Bad Gateway' });
    await fixture.whenStable();

    const t = text(fixture.nativeElement as HTMLElement);
    expect(t).toContain('La API no responde');
    expect(t).toContain('ecowatt-postgres');
  });
});
