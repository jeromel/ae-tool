import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { NoteGauge } from '../note-gauge';
import { decisionTone } from '../decision-tone';
import { type SortKey, controlCell, decisionCell, sortRows } from '../portfolio-table';

/**
 * Portefeuille applicatif (maquette portefeuille de Camille, 27/11) : la décision en deuxième colonne, sa justification
 * après, les critères C1 à C4 sur demande. Une ligne par application, notation recalculée selon la grille du métamodèle.
 * S'imprime en paysage (charte §8).
 */
@Component({
  imports: [NoteGauge, RouterLink],
  host: { class: 'print-landscape' },
  styles: `
    td.nowrap, th { white-space: nowrap; }
    th .sub { font-weight: 400; font-size: 11.5px; white-space: normal; }
    th button.sort { background: none; border: 0; padding: 0; font: inherit; color: inherit; cursor: pointer; }
    th button.sort:hover { background: none; text-decoration: underline; }
    .app { position: sticky; left: 0; background: var(--card, #fff); z-index: 1; min-width: 190px; }
    .sub-line { color: var(--muted); font-size: 12px; }
    .decision .why { color: var(--muted); font-size: 12px; white-space: normal; max-width: 240px; }
    .computed { font-style: italic; }
    .summary { margin: 0 0 14px; }
    .filter-line { display: none; }
    @media print { .filter-line { display: block; } }
  `,
  template: `
    <h1>Portefeuille applicatif</h1>
    <p class="lead">
      Notation d'urbanisation : criticité métier (CM) et critères notés sur preuves. Une preuve « NC » ne vaut jamais zéro : elle bloque la
      note, donc la décision calculée.
    </p>
    @if (loaded()) {
      <p class="summary">
        <b>{{ rows().length }}</b> {{ rows().length > 1 ? 'applications notées' : 'application notée' }}
        · <b>{{ keyPerson() }}</b> {{ keyPerson() > 1 ? 'dépendent' : 'dépend' }} d'une personne clé
        · <b>{{ incomplete() }}</b> {{ incomplete() > 1 ? 'notations incomplètes' : 'notation incomplète' }}
        · <b>{{ toInstruct() }}</b> « À instruire »
      </p>
    }

    <div class="tabs" role="tablist">
      <button type="button" role="tab" [attr.aria-selected]="tab() === 'tableau'" (click)="tab.set('tableau')">Tableau</button>
      <button type="button" role="tab" [attr.aria-selected]="tab() === 'quadrant'" (click)="tab.set('quadrant')">Quadrant d'urbanisation</button>
    </div>

    @if (loaded() && rows().length === 0) {
      <div class="card">
        <p>Aucune application n'est encore notée. La notation se fait sur la page de chaque application.</p>
        <a routerLink="/applications">Voir les applications</a>
      </div>
    } @else if (tab() === 'quadrant') {
      <div class="quadrant">
        @for (q of quadrants; track q.key) {
          <div>
            <h3>{{ q.key }} <span class="muted">{{ q.hint }}</span></h3>
            @for (r of inQuadrant(q.key); track r.id) {
              <a class="chip" [class]="tone(q.key)" style="margin: 0 4px 4px 0; text-decoration: none" [href]="'/portefeuille/' + r.id" (click)="open(r.id); $event.preventDefault()">{{ r.name }}</a>
            } @empty {
              <span class="muted small">Aucune application placée.</span>
            }
          </div>
        }
      </div>
      @if (incomplete() > 0) {
        <p class="muted" style="margin: 10px 0 0">{{ notPlaced() }}</p>
      }
    } @else {
      <div class="card flush">
        <div class="tools">
          <input type="search" placeholder="Filtrer par nom ou domaine" aria-label="Filtrer par nom ou domaine" [value]="filter()" (input)="filter.set($any($event.target).value)" />
          <label><input type="checkbox" [checked]="onlyIncomplete()" (change)="onlyIncomplete.set($any($event.target).checked)" /> Notations incomplètes seulement</label>
          <label><input type="checkbox" [checked]="criteria()" (change)="setCriteria($any($event.target).checked)" /> Afficher les critères C1 à C4</label>
          <span class="muted right">{{ shown().length }} {{ shown().length > 1 ? 'lignes' : 'ligne' }}, cliquer une ligne pour noter</span>
        </div>
        @if (filterLine()) { <p class="filter-line small">{{ filterLine() }}</p> }
        @if (loaded() && shown().length === 0) {
          <p class="muted" style="padding: 16px; margin: 0">
            {{ emptyLine() }}
            <button type="button" class="link" (click)="clearFilter()">Effacer le filtre</button>
          </p>
        } @else {
          <div class="scroll tall" style="--above: 300px">
            <table>
              <thead>
                <tr>
                  <th class="app"><button type="button" class="sort" (click)="sortBy('nom')">Application{{ arrow('nom') }}</button></th>
                  <th><button type="button" class="sort" (click)="sortBy('decision')">Décision{{ arrow('decision') }}</button></th>
                  <th><button type="button" class="sort" (click)="sortBy('priorite')">Priorité{{ arrow('priorite') }}</button></th>
                  <th>Besoin</th><th>CM</th><th>Maîtrise</th>
                  @if (criteria()) {
                    @for (c of grid()?.criteria ?? []; track c.code) { <th>{{ c.code }}<div class="sub">{{ c.label }}</div></th> }
                  }
                  <th>Contrôle</th>
                </tr>
              </thead>
              <tbody>
                @for (r of shown(); track r.id) {
                  <tr class="click" (click)="open(r.id)">
                    <td class="app" [title]="r.urbaId ?? ''"><b>{{ r.name }}</b>
                      @if (!r.inArchi) { <span class="chip warn" title="Absente du modèle Archi">hors Archi</span> }
                      <div class="sub-line">{{ subLine(r) }}</div></td>
                    <td class="decision">
                      @if (decision(r); as dc) {
                        @if (dc.text) {
                          <span [class]="tone(dc.text)" [class.computed]="dc.computed">{{ dc.computed ? dc.text + ' (calculée)' : dc.text }}</span>
                        } @else { <span class="muted">—</span> }
                        @if (dc.note) { <div class="why">{{ dc.note }}</div> }
                      }
                    </td>
                    <td>@if (r.result.priority) { <span class="chip" [class.bad]="r.result.priority === 'Haute'" [class.warn]="r.result.priority === 'Moyenne'">{{ r.result.priority }}</span> } @else { <span class="muted">—</span> }</td>
                    <td class="nowrap">{{ need(r.need) }}</td>
                    <td><app-note-gauge [value]="r.cm" /></td>
                    <td><app-note-gauge [value]="r.result.mastery" [lowUnder]="2.5" /></td>
                    @if (criteria()) {
                      @for (c of grid()?.criteria ?? []; track c.code) { <td><app-note-gauge [value]="r.result.notes[c.code]" [lowUnder]="2" /></td> }
                    }
                    <td class="nowrap">
                      @if (control(r.result.control); as cc) { <span [class]="cc.tone ? 'chip ' + cc.tone : 'muted'">{{ cc.symbol }} {{ cc.word }}</span> }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }
  `,
})
export class PortfolioPage {
  private api = inject(Api);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  rows = signal<any[]>([]);
  grid = signal<any>(null);
  loaded = signal(false);
  tab = signal<'tableau' | 'quadrant'>('tableau');
  filter = signal('');
  onlyIncomplete = signal(false);
  criteria = signal(this.route.snapshot.queryParamMap.get('criteres') === '1');
  sortKey = signal<SortKey>('defaut');
  descending = signal(false);

  quadrants = [
    { key: 'Moderniser / Migrer', hint: 'besoin maintenu, maîtrise faible' },
    { key: 'Maintenir', hint: 'besoin maintenu, maîtrise suffisante' },
    { key: 'Éliminer', hint: 'besoin non maintenu, maîtrise faible' },
    { key: 'Tolérer', hint: 'besoin non maintenu, maîtrise suffisante' },
  ];

  active = computed(() => this.rows().filter((r) => !r.result.retired));
  keyPerson = computed(() => this.active().filter((r) => (r.keyPerson ?? '').startsWith('Oui')).length);
  incomplete = computed(() => this.active().filter((r) => r.result.control === 'Calcul incomplet').length);
  toInstruct = computed(() => this.active().filter((r) => r.result.computedDecision === 'À instruire').length);
  shown = computed(() => {
    const f = this.filter().toLowerCase();
    const rows = this.rows().filter(
      (r) =>
        (!f || `${r.name} ${r.domain ?? ''} ${r.urbaId ?? ''}`.toLowerCase().includes(f)) &&
        (!this.onlyIncomplete() || r.result.control === 'Calcul incomplet'),
    );
    return sortRows(rows, this.sortKey(), this.descending());
  });
  /** Ligne imprimée quand un filtre restreint la page (charte §8). */
  filterLine = computed(() => {
    const parts = [this.filter() ? `« ${this.filter()} »` : '', this.onlyIncomplete() ? 'notations incomplètes seulement' : ''].filter(Boolean);
    return parts.length ? `Filtre : ${parts.join(', ')}` : '';
  });
  emptyLine = computed(() => (this.filter() ? `Aucune application ne correspond à « ${this.filter()} ».` : 'Aucune notation incomplète.'));
  notPlaced = computed(() => {
    const n = this.incomplete();
    return `${n > 1 ? `${n} applications ne sont pas placées` : '1 application n’est pas placée'} : il manque des preuves, le besoin métier ou les déclencheurs rouges.`;
  });

  decision = decisionCell;
  control = controlCell;
  /** Teinte d'une décision : la même dans le tableau, le quadrant et la fiche. */
  tone = decisionTone;

  constructor() {
    this.api.get('/portfolio').then((d) => {
      this.rows.set(d.rows);
      this.grid.set(d.grid);
      this.loaded.set(true);
    });
  }

  subLine(r: { domain?: string | null; state?: string | null }): string {
    return [r.domain, r.state].filter(Boolean).join(' · ');
  }

  need(value: string | null): string {
    return value === 'Oui' ? 'Maintenu' : value === 'Non' ? 'Non maintenu' : value ?? '—';
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) this.descending.update((d) => !d);
    else {
      this.sortKey.set(key);
      this.descending.set(false);
    }
  }

  arrow(key: SortKey): string {
    return this.sortKey() === key ? (this.descending() ? ' ▼' : ' ▲') : '';
  }

  /** Le choix d'afficher les critères est gardé dans l'adresse (`?criteres=1`). */
  setCriteria(on: boolean): void {
    this.criteria.set(on);
    this.router.navigate([], { relativeTo: this.route, queryParams: { criteres: on ? 1 : null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  clearFilter(): void {
    this.filter.set('');
    this.onlyIncomplete.set(false);
  }

  inQuadrant(key: string): any[] {
    return this.active().filter((r) => r.result.quadrant === key);
  }

  open(id: string): void {
    this.router.navigate(['/portefeuille', id]);
  }
}
