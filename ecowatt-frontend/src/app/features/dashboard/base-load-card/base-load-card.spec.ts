import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { BaseLoadCard } from './base-load-card';
import { SimulationPreset } from '../simulator-card/simulator-card';

/** Los numeros de la noche real del 30/10: casa en 148 W, Andy 1 con 49 W fijos. */
const baseLoad = {
  nights: 7,
  windowStartHour: 0,
  windowEndHour: 8,
  houseWatts: 148,
  lastNightWatts: 196,
  typicalWatts: 148,
  monthlyKwh: 106.56,
  monthlyCost: 53_617,
  marginalPricePerKwh: 503.16,
  sharePercent: 61,
  devices: [
    {
      deviceId: '00000000-0000-0000-0000-000000000000',
      name: 'Resto de la casa',
      watts: 67,
      monthlyKwh: 48.24,
      monthlyCost: 24_272,
      lastNightWatts: null,
      typicalWatts: null,
      isUnidentified: true,
    },
    {
      deviceId: 'andy-1',
      name: 'Zapatilla Andy 1',
      watts: 49,
      monthlyKwh: 35.28,
      monthlyCost: 17_751,
      lastNightWatts: 95,
      typicalWatts: 49,
      isUnidentified: false,
    },
  ],
  history: [],
};

function text(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/[  ]/g, ' ');
}

describe('BaseLoadCard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('muestra la base de la casa, lo que cuesta por ciclo y de donde sale', async () => {
    const fixture = TestBed.createComponent(BaseLoadCard);
    http.expectOne((r) => r.url === '/api/insights/base-load').flush(baseLoad);
    await fixture.whenStable();

    const t = text(fixture.nativeElement as HTMLElement);
    expect(t).toContain('148 W');
    expect(t).toContain('$ 53.617');
    expect(t).toContain('Zapatilla Andy 1');
    expect(t).toContain('Resto de la casa');

    // La suba de anoche va en palabras, no solo en rojo.
    expect(t).toContain('48 W mas que lo normal');
  });

  it('simular un enchufe manda su base todo el dia', async () => {
    const fixture = TestBed.createComponent(BaseLoadCard);
    http.expectOne((r) => r.url === '/api/insights/base-load').flush(baseLoad);
    await fixture.whenStable();

    let preset: SimulationPreset | null = null;
    fixture.componentInstance.simulate.subscribe((p) => (preset = p));

    const rows = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'));
    const andy = rows.find((r) => (r.textContent ?? '').includes('Zapatilla Andy 1'))!;
    (andy.querySelector('button') as HTMLButtonElement).click();

    expect(preset).toEqual({ label: 'Cortar el consumo base de Zapatilla Andy 1', watts: 49, fromHour: 0, toHour: 0 });
  });

  it('sin ninguna madrugada medida explica cuando aparece', async () => {
    const fixture = TestBed.createComponent(BaseLoadCard);
    http
      .expectOne((r) => r.url === '/api/insights/base-load')
      .flush({ ...baseLoad, nights: 0, houseWatts: null, lastNightWatts: null, typicalWatts: null, devices: [] });
    await fixture.whenStable();

    expect(text(fixture.nativeElement as HTMLElement)).toContain('Todavia no hay ninguna madrugada medida');
  });
});
