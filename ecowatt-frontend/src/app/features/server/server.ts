import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Alert, ServerContainer, ServerOverall, ServerStatus } from '../../core/models/api.models';
import { EcowattApi } from '../../core/services/ecowatt-api';
import { ago, bytes, duration, percent } from '../../shared/format';

/**
 * Cada cuanto se pide el estado. CPU y memoria salen de /proc en el momento; contenedores,
 * disco y backups los actualiza el script del host cada minuto, asi que esos cambian menos.
 */
const REFRESH_MS = 5_000;

/** Nivel de una barra de uso: el color acompana al numero, nunca lo reemplaza. */
type Level = 'ok' | 'warning' | 'critical';

@Component({
  selector: 'app-server',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './server.html',
  styleUrl: './server.scss',
})
export class Server {
  private readonly api = inject(EcowattApi);

  protected readonly bytes = bytes;
  protected readonly duration = duration;
  protected readonly percent = percent;

  protected readonly status = signal<ServerStatus | null>(null);
  protected readonly error = signal<string | null>(null);
  private inFlight = false;

  protected readonly overallText = computed(() => {
    const labels: Record<ServerOverall, string> = {
      Ok: 'Todo bien',
      Warning: 'Hay algo para mirar',
      Critical: 'Hay un problema',
    };
    const s = this.status();
    return s ? labels[s.overall] : '';
  });

  /** Lo que no es informativo: los Info van aparte, en gris, para que no parezcan problemas. */
  protected readonly problems = computed(() => (this.status()?.checks ?? []).filter((c) => c.severity !== 'Info'));
  protected readonly notes = computed(() => (this.status()?.checks ?? []).filter((c) => c.severity === 'Info'));

  protected readonly silentCount = computed(() => (this.status()?.app.devices ?? []).filter((d) => d.silent).length);

  constructor() {
    this.load();
    const timer = setInterval(() => this.load(), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  private load(): void {
    // Con la pestana en segundo plano no tiene sentido seguir pidiendo, y si el servidor tarda
    // mas de 5 s en contestar no se apilan pedidos.
    if (this.inFlight || (this.status() && document.hidden)) return;

    this.inFlight = true;
    this.api.getServerStatus().subscribe({
      next: (s) => {
        this.status.set(s);
        this.error.set(null);
        this.inFlight = false;
      },
      error: (e: HttpErrorResponse) => {
        // Se deja a la vista el ultimo estado: que la API no conteste es justamente lo que hay
        // que poder ver, sin perder lo que se sabia hasta recien.
        this.error.set(
          e.status === 0 || e.status >= 500
            ? 'La API no responde. Lo de abajo es lo ultimo que se supo.'
            : `No se pudo leer el estado (${e.status}).`,
        );
        this.inFlight = false;
      },
    });
  }

  protected ago(iso: string | null | undefined): string {
    const s = this.status();
    return s ? ago(iso, s.generatedUtc) : '--';
  }

  /** Cuanto hace desde un instante, sin el "hace": para columnas como "Arriba hace". */
  protected since(iso: string | null | undefined): string {
    const s = this.status();
    return s && iso ? duration((Date.parse(s.generatedUtc) - Date.parse(iso)) / 1000) : '--';
  }

  protected level(used: number | null | undefined, warnAt: number, criticalAt: number): Level {
    if (used === null || used === undefined) return 'ok';
    return used >= criticalAt ? 'critical' : used >= warnAt ? 'warning' : 'ok';
  }

  protected severityLabel(severity: Alert['severity']): string {
    return severity === 'Critical' ? 'Critico' : severity === 'Warning' ? 'Atencion' : 'Info';
  }

  protected containerState(c: ServerContainer): { text: string; level: Level } {
    if (c.state !== 'running') return { text: c.state, level: 'critical' };
    if (c.health === 'unhealthy') return { text: 'no responde', level: 'critical' };
    if (c.health === 'starting') return { text: 'arrancando', level: 'warning' };
    return { text: c.health === 'healthy' ? 'corriendo, sano' : 'corriendo', level: 'ok' };
  }

  protected yesNo(value: boolean | null | undefined, yes: string, no: string): string {
    return value === null || value === undefined ? '--' : value ? yes : no;
  }

  protected count(value: number | null | undefined): string {
    return value === null || value === undefined ? '--' : new Intl.NumberFormat('es-AR').format(value);
  }

  protected loads(): string {
    const s = this.status()?.system;
    if (!s) return '--';
    return [s.load1, s.load5, s.load15].map((l) => l.toFixed(2).replace('.', ',')).join(' / ');
  }
}
