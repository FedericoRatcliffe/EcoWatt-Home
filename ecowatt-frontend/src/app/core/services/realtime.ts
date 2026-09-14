import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Device, EnergyReading } from '../models/api.models';

export type RealtimeStatus = 'desconectado' | 'conectando' | 'conectado';

/**
 * Conexion SignalR al hub de energia. Mantiene la potencia actual de cada dispositivo en un
 * signal: escribir un signal ya dispara el render (la app es zoneless, no hay zone.js).
 */
@Injectable({ providedIn: 'root' })
export class Realtime {
  private connection?: HubConnection;

  readonly status = signal<RealtimeStatus>('desconectado');

  /** Ultima potencia conocida por deviceId, alimentada por el hub. */
  readonly wattsByDevice = signal<Record<string, number>>({});

  /** Ultima lectura recibida, para el ticker de actividad. */
  readonly lastReading = signal<EnergyReading | null>(null);

  /** Se incrementa cuando el backend auto-registra un dispositivo nuevo. */
  readonly newDeviceCount = signal(0);

  async start(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }

    this.status.set('conectando');

    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/energy')
      // Reintenta solo: la API puede reiniciarse mientras el dashboard sigue abierto.
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('readingReceived', (reading: EnergyReading) => {
      this.lastReading.set(reading);
      this.wattsByDevice.update((current) => ({ ...current, [reading.deviceId]: reading.watts }));
    });

    this.connection.on('deviceRegistered', (_device: Device) => {
      this.newDeviceCount.update((n) => n + 1);
    });

    this.connection.onreconnecting(() => this.status.set('conectando'));
    this.connection.onreconnected(() => this.status.set('conectado'));
    this.connection.onclose(() => this.status.set('desconectado'));

    try {
      await this.connection.start();
      this.status.set('conectado');
    } catch {
      this.status.set('desconectado');
      // Un reintento tardio alcanza: si la API todavia no levanto, el usuario ve el estado.
      setTimeout(() => void this.start(), 5000);
    }
  }

  async stop(): Promise<void> {
    await this.connection?.stop();
    this.connection = undefined;
    this.status.set('desconectado');
  }
}
