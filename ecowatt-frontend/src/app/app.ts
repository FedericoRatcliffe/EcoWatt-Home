import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Realtime } from './core/services/realtime';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  protected readonly realtime = inject(Realtime);

  constructor() {
    // La conexión al hub la abre el shell, no una pantalla: si la abriera el dashboard,
    // entrar directo a la ficha de un dispositivo (o recargar estando en ella) dejaría el
    // tiempo real apagado, y el encabezado diría "desconectado" sin que nada esté mal.
    void this.realtime.start();
  }
}
