import { Component, computed, effect, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { SourceState } from './source/model';
import { ConnectionTab } from './source/connection';
import { MappingTab } from './source/mapping';
import { ReconcileTab } from './source/reconcile';
import { WorkstationsTab } from './source/workstations';

type Tab = 'connexion' | 'correspondances' | 'rapprochement' | 'postes';

/** Une source : sa connexion (API REST), sa matrice de correspondances, son rapprochement et le comptage des postes, en onglets portés par l'adresse. */
@Component({
  selector: 'app-source',
  imports: [RouterLink, ConnectionTab, MappingTab, ReconcileTab, WorkstationsTab],
  providers: [SourceState],
  template: `
    <a routerLink="/sources">← Sources externes</a>
    @if (state.error()) { <div class="error" role="alert">{{ state.error() }}</div> }
    @if (state.profile(); as p) {
      <h1>{{ p.label }}</h1>
      <p class="lead">
        <span class="chip info">{{ p.kind === 'api' ? 'API REST' : 'Fichier CSV ou Excel' }}</span>
        <span class="muted small">Clé : {{ p.key }}</span>
        <span class="muted small">L'outil lit la source ; il n'y écrit jamais.</span>
      </p>

      <div class="tabs" role="tablist" aria-label="Sections de la source">
        @for (t of tabs(); track t.key) {
          <button type="button" role="tab" [attr.aria-selected]="current() === t.key" (click)="go(t.key)">
            {{ t.label }}
            @if (t.key === 'correspondances' && state.mappingDirty()) { <span class="dot" title="Modifications non enregistrées"></span> }
            @if (t.key === 'connexion' && state.connectionDirty()) { <span class="dot" title="Modifications non enregistrées"></span> }
            @if (t.key === 'rapprochement' && state.pending().length) { <span class="chip warn">{{ state.pending().length }}</span> }
          </button>
        }
      </div>

      @switch (current()) {
        @case ('connexion') { <app-connection-tab /> }
        @case ('correspondances') { <app-mapping-tab /> }
        @case ('rapprochement') { <app-reconcile-tab /> }
        @case ('postes') { <app-workstations-tab /> }
      }
    }
  `,
  styles: `
    .lead { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-top: 6px; }
    .tabs button { display: inline-flex; gap: 8px; align-items: center; }
    .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--signal); display: inline-block; }
  `,
})
export class SourcePage {
  readonly state = inject(SourceState);
  private router = inject(Router);
  key = input.required<string>();
  onglet = input<string>();

  tabs = computed(() => {
    const all: { key: Tab; label: string }[] = [
      { key: 'connexion', label: 'Connexion' }, { key: 'correspondances', label: 'Correspondances' }, { key: 'rapprochement', label: 'Rapprochement' },
      { key: 'postes', label: 'Postes de travail' },
    ];
    return this.state.profile()?.kind === 'api' ? all : all.slice(1);
  });

  /** Onglet demandé par l'adresse s'il existe pour ce profil ; sinon le premier. */
  current = computed<Tab>(() => this.tabs().find((t) => t.key === this.onglet())?.key ?? this.tabs()[0].key);

  constructor() {
    effect(() => void this.state.open(this.key()));
  }

  go(tab: Tab): void {
    void this.router.navigate(['/sources', this.key()], { queryParams: { onglet: tab }, replaceUrl: true });
  }
}
