import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { SimulatorCard } from './simulator-card';

function text(element: HTMLElement): string {
  return (element.textContent ?? '').replace(/[  ]/g, ' ');
}

/** 49 W todo el ciclo sobre una factura de 175 kWh que baja de tramo. */
const result = {
  cycleLabel: 'Ciclo 31/10 - 29/11',
  baselineSource: 'la proyeccion del ciclo en curso',
  hoursPerDay: 24,
  baselineKwh: 175,
  scenarioKwh: 139.72,
  deltaKwh: 35.28,
  baselineTotal: 78_995,
  scenarioTotal: 63_120,
  saving: 15_875,
  yearlySaving: 199_812,
  baselineMarginalPrice: 503.16,
  scenarioMarginalPrice: 376.32,
};

describe('SimulatorCard', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('un escenario que llega de otra card se calcula solo', async () => {
    const fixture = TestBed.createComponent(SimulatorCard);
    fixture.componentRef.setInput('preset', {
      label: 'Cortar el consumo base de Zapatilla Andy 1',
      watts: 49,
      fromHour: 0,
      toHour: 0,
    });
    await fixture.whenStable();

    const req = http.expectOne((r) => r.url === '/api/insights/simulate');
    expect(req.request.body).toEqual({ watts: 49, fromHour: 0, toHour: 0 });
    req.flush(result);
    await fixture.whenStable();

    const t = text(fixture.nativeElement as HTMLElement);
    expect(t).toContain('Cortar el consumo base de Zapatilla Andy 1');
    expect(t).toContain('Ahorras $ 15.875 por ciclo');
    // Bajar de tramo se dice, porque explica por que el ahorro no es lineal.
    expect(t).toContain('cambia de tramo');
  });

  it('sumar un aparato manda watts negativos y dice que la factura sube', async () => {
    const fixture = TestBed.createComponent(SimulatorCard);
    await fixture.whenStable();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    buttons.find((b) => b.textContent?.includes('Estufa'))!.click();
    await fixture.whenStable();

    const req = http.expectOne((r) => r.url === '/api/insights/simulate');
    expect(req.request.body).toEqual({ watts: -2000, fromHour: 20, toHour: 0 });
    req.flush({ ...result, saving: -40_000, yearlySaving: -500_000, scenarioMarginalPrice: 503.16 });
    await fixture.whenStable();

    expect(text(fixture.nativeElement as HTMLElement)).toContain('Sube $ 40.000 por ciclo');
  });
});
