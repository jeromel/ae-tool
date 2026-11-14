import { Component, inject, input, output, signal } from '@angular/core';
import { Api } from '../../api';
import { ConfirmPanel } from './confirm-panel';
import { MetaStore } from './store';

interface Move { kind: string; from: string; to: string; count: number; }
interface Reclassification { changes: number; moves: Move[]; dryRun: boolean; }

/** Simulation puis réapplication de la matrice enregistrée aux objets et relations déjà importés. */
@Component({
  selector: 'app-reapply-panel',
  imports: [ConfirmPanel],
  template: `
    <div class="card">
      <h2>Effet sur les données déjà importées</h2>
      <p class="muted">Modifier la matrice ne change que les imports et exports à venir : les objets déjà importés ne changent de type qu'au moment où vous réappliquez la matrice. Simulez d'abord.</p>
      <div class="tools">
        <button type="button" class="ghost" (click)="simulate()" [disabled]="blocked() || busy()">Simuler la réapplication</button>
        @if (blocked()) { <span class="muted small">Enregistrez d'abord la matrice : la simulation porte sur la matrice enregistrée.</span> }
      </div>
      @if (result(); as r) {
        @if (r.changes === 0) {
          <p class="note">Aucun objet ni aucune relation ne changerait de type.</p>
        } @else {
          <p><b>{{ total('objet') }}</b> objet(s) et <b>{{ total('relation') }}</b> relation(s) changeraient de type.</p>
          <table>
            <tr><th scope="col">Nature</th><th scope="col">Type actuel</th><th scope="col"></th><th scope="col">Nouveau type</th><th scope="col" class="num">Effectif</th></tr>
            @for (m of r.moves; track $index) {
              <tr><td>{{ m.kind === 'objet' ? 'Objets' : 'Relations' }}</td><td>{{ label(m.kind, m.from) }}</td><td aria-hidden="true">→</td><td>{{ label(m.kind, m.to) }}</td><td class="num">{{ m.count }}</td></tr>
            }
          </table>
          @if (!confirming()) { <div class="tools" style="margin-top: 12px"><button type="button" (click)="confirming.set(true)" [disabled]="blocked()">Réappliquer la matrice</button></div> }
        }
      }
      @if (confirming() && result()) {
        <app-confirm title="Réappliquer la matrice" action="Réappliquer" [disabled]="busy()" (confirm)="apply()" (cancel)="confirming.set(false)">
          <p>{{ total('objet') }} objet(s) et {{ total('relation') }} relation(s) vont changer de type, comme le montre la simulation. Les impacts déduits des projets seront recalculés et l'opération est inscrite au journal.
            Elle ne peut pas être annulée en un clic : un retour demande de modifier la matrice et de réappliquer.</p>
        </app-confirm>
      }
      @if (applied(); as a) { <p class="note" role="status">Matrice réappliquée : {{ a.objets }} objet(s) et {{ a.relations }} relation(s) ont changé de type.</p> }
    </div>
  `,
})
export class ReapplyPanel {
  private readonly api = inject(Api);
  private readonly store = inject(MetaStore);
  /** Vrai tant que le brouillon diffère de la matrice enregistrée. */
  blocked = input(false);
  /** Émis après une réapplication : les effectifs et les constats ont changé. */
  changed = output<void>();
  protected readonly result = signal<Reclassification | null>(null);
  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly applied = signal<{ objets: number; relations: number } | null>(null);
  protected total(kind: string): number {
    return (this.result()?.moves ?? []).filter((m) => m.kind === kind).reduce((n, m) => n + m.count, 0);
  }

  protected label(kind: string, key: string): string {
    return kind === 'objet' ? this.store.typeLabel(key) : this.store.relLabel(key);
  }

  protected async simulate(): Promise<void> {
    this.applied.set(null);
    this.confirming.set(false);
    this.busy.set(true);
    try {
      this.store.error.set('');
      this.result.set(await this.api.post<Reclassification>('/metamodel/transcoding/reclassify?dryRun=true', {}));
    } catch (e) {
      this.store.error.set((e as Error).message);
    }
    this.busy.set(false);
  }

  protected async apply(): Promise<void> {
    const objets = this.total('objet');
    const relations = this.total('relation');
    this.busy.set(true);
    const ok = await this.store.run(async () => {
      await this.api.post('/metamodel/transcoding/reclassify?dryRun=false', {});
      this.store.m.set(await this.api.get('/metamodel'));
      await this.store.refreshUsage();
    });
    this.busy.set(false);
    if (!ok) return;
    this.confirming.set(false);
    this.result.set(null);
    this.applied.set({ objets, relations });
    this.changed.emit();
  }
}
