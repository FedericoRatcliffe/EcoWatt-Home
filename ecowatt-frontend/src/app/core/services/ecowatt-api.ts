import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BillImportResult,
  CycleInfo,
  DailyDashboard,
  Device,
  DeviceHistory,
  DeviceInput,
  EnergyReading,
  ImportedBill,
  PeriodDashboard,
  TariffInput,
  TariffSchedule,
} from '../models/api.models';

/**
 * Cliente HTTP de la API. Las rutas son relativas: en desarrollo las resuelve el proxy de
 * ng serve y en Docker el nginx del frontend, asi que no hay URL de backend hardcodeada.
 */
@Injectable({ providedIn: 'root' })
export class EcowattApi {
  private readonly http = inject(HttpClient);

  getDevices(): Observable<Device[]> {
    return this.http.get<Device[]>('/api/devices');
  }

  getDevice(id: string): Observable<Device> {
    return this.http.get<Device>(`/api/devices/${id}`);
  }

  createDevice(input: DeviceInput): Observable<Device> {
    return this.http.post<Device>('/api/devices', input);
  }

  updateDevice(id: string, input: DeviceInput): Observable<Device> {
    return this.http.put<Device>(`/api/devices/${id}`, { isActive: true, ...input });
  }

  deleteDevice(id: string): Observable<void> {
    return this.http.delete<void>(`/api/devices/${id}`);
  }

  /** Historial agregado con costo. hours = 24 | 168 | 720. */
  getDeviceHistory(id: string, hours: number): Observable<DeviceHistory> {
    return this.http.get<DeviceHistory>(`/api/devices/${id}/history`, {
      params: new HttpParams().set('hours', hours),
    });
  }

  /** Lecturas crudas, para ver la potencia muestra por muestra. */
  getDeviceReadings(id: string, from: Date, to: Date, maxPoints = 2000): Observable<EnergyReading[]> {
    return this.http.get<EnergyReading[]>(`/api/devices/${id}/readings`, {
      params: new HttpParams()
        .set('from', from.toISOString())
        .set('to', to.toISOString())
        .set('maxPoints', maxPoints),
    });
  }

  setPower(id: string, on: boolean): Observable<unknown> {
    return this.http.post(`/api/devices/${id}/power`, { on });
  }

  getDaily(date?: string): Observable<DailyDashboard> {
    const params = date ? new HttpParams().set('date', date) : undefined;
    return this.http.get<DailyDashboard>('/api/dashboard/daily', { params });
  }

  /**
   * La factura del periodo. Por defecto el ciclo de facturacion real; con cycle=false,
   * el mes calendario.
   */
  getPeriod(useCycle: boolean, offset = 0, year?: number, month?: number): Observable<PeriodDashboard> {
    let params = new HttpParams().set('cycle', useCycle).set('offset', offset);
    if (year) params = params.set('year', year);
    if (month) params = params.set('month', month);
    return this.http.get<PeriodDashboard>('/api/dashboard/period', { params });
  }

  getCycleInfo(): Observable<CycleInfo> {
    return this.http.get<CycleInfo>('/api/dashboard/cycle');
  }

  getTariff(): Observable<TariffSchedule> {
    return this.http.get<TariffSchedule>('/api/tariff');
  }

  getTariffHistory(): Observable<TariffSchedule[]> {
    return this.http.get<TariffSchedule[]>('/api/tariff/history');
  }

  saveTariff(input: TariffInput): Observable<TariffSchedule> {
    return this.http.put<TariffSchedule>('/api/tariff', input);
  }

  deleteTariff(id: string): Observable<void> {
    return this.http.delete<void>(`/api/tariff/${id}`);
  }

  getBills(): Observable<ImportedBill[]> {
    return this.http.get<ImportedBill[]>('/api/tariff/bills');
  }

  deleteBill(id: string): Observable<void> {
    return this.http.delete<void>(`/api/tariff/bills/${id}`);
  }

  /** Sube el PDF de la distribuidora y carga tarifas y comprobante. */
  importBill(file: File): Observable<BillImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<BillImportResult>('/api/tariff/import', form);
  }
}
