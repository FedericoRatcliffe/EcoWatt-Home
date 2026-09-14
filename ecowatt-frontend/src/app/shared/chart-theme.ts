import { Injectable } from '@angular/core';

/**
 * Colores y tipografia que consumen los graficos. Son los mismos tokens que
 * `src/styles/_variables.scss`, pero ECharts necesita strings y no puede leer SCSS.
 *
 * Un solo tema, claro: el dashboard de la Ticketera no tiene variante oscura, y este va en
 * la misma familia. El azul de serie ($blue-600) pasa los seis chequeos de la paleta de
 * dataviz sobre surface blanco: banda de luminosidad, piso de croma y contraste >= 3:1.
 */
export interface ChartTheme {
  surface: string;
  textPrimary: string;
  textSecondary: string;
  muted: string;
  grid: string;
  baseline: string;
  series1: string;
  series1Soft: string;
  good: string;
  critical: string;
  font: string;
}

const THEME: ChartTheme = {
  surface: '#ffffff',
  textPrimary: '#0d1b2a',
  textSecondary: '#4a6a85',
  muted: '#728599',
  grid: 'rgba(0, 136, 212, 0.09)',
  baseline: 'rgba(0, 136, 212, 0.22)',
  series1: '#0088d4',
  series1Soft: 'rgba(0, 136, 212, 0.14)',
  good: '#0a9469',
  critical: '#c93030',
  font: "'Manrope', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
};

@Injectable({ providedIn: 'root' })
export class ChartThemeWatcher {
  /** Signal-compatible: se llama como funcion para no tocar los componentes que ya lo usan. */
  readonly theme = (): ChartTheme => THEME;
}
