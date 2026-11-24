import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { MatrixView } from '../matrix-view';
import { QuestionBar } from '../questions';
import { Mark } from '../projection';
import { Session } from '../session';
import { SpaceConfig } from '../spaces.config';

/**
 * Espace en liste : une projection du référentiel, interrogée par des questions prêtes à l'emploi.
 * Le même composant sert Applications, Flux, Données et Infrastructure ; seule la configuration change.
 */
@Component({
  imports: [RouterLink, QuestionBar, MatrixView, Mark],
  template: `
    @let c = config();
    <h1>{{ c.title }}</h1>
    <p class="lead">{{ c.lead }}</p>
    <app-questions [questions]="c.questions" [selected]="selected()" (pick)="pick($event)" />

    <div class="card flush">
      <div class="tools">
        <input type="search" placeholder="Filtrer" aria-label="Filtrer la liste" [value]="filter()" (input)="filter.set($any($event.target).value)" />
        @if (c.matrix; as mx) {
          <div class="segmented" role="group" aria-label="Mode d'affichage">
            <button type="button" [attr.aria-pressed]="mode() === 'liste'" (click)="mode.set('liste')">Liste</button>
            <button type="button" [attr.aria-pressed]="mode() === 'matrice'" (click)="mode.set('matrice')" [title]="mx.label">Matrice</button>
          </div>
        }
        <span class="muted right">{{ shown().length }} sur {{ rows().length }} {{ c.noun }}</span>
        @if (c.createRole && createType() && session.can('contributeur')) {
          <a class="btn" routerLink="/objets/nouveau" [queryParams]="{ type: createType() }">{{ c.createLabel ?? 'Nouvel objet' }}</a>
        }
      </div>
      @if (mode() === 'matrice' && c.matrix) {
        <app-matrix-view [rows]="c.matrix.rows" [cols]="c.matrix.cols" [relation]="c.matrix.relation ?? ''" above="370px" />
      } @else {
        <div class="scroll tall" style="--above: 330px">
          <table>
            <tr>
              @for (col of c.columns; track col.key) {
                <th [class.num]="col.kind === 'nombre'" [attr.aria-sort]="sortKey() === col.key ? (descending() ? 'descending' : 'ascending') : null">
                  <button type="button" class="sort" (click)="sortBy(col.key)">{{ col.label }}{{ sortKey() === col.key ? (descending() ? ' ↓' : ' ↑') : '' }}</button>
                </th>
              }
              @if (c.actionsIdKey) { <th></th> }
            </tr>
            @for (r of shown(); track $index) {
              <tr>
                @for (col of c.columns; track col.key) {
                  <td [class.num]="col.kind === 'nombre'">
                    @switch (col.kind) {
                      @case ('objet') { <a [routerLink]="['/objets', r[col.idKey!]]"><b>{{ r[col.key] || '(sans nom)' }}</b></a><app-mark [id]="r[col.idKey!]" /> }
                      @case ('nombre') { <span [class.muted]="!r[col.key]">{{ r[col.key] ?? '–' }}</span> }
                      @case ('liste') { {{ r[col.key].join(', ') }} }
                      @case ('chip') { @if (r[col.key]) { <span class="chip">{{ r[col.key] }}</span> } }
                      @default { {{ r[col.key] }} }
                    }
                    @if (col.key === rowMarkColumn() && r.id && r.id !== r[c.columns[0].idKey ?? '']) { <app-mark [id]="r.id" /> }
                  </td>
                }
                @if (c.actionsIdKey; as key) {
                  <td class="actions">
                    <a [routerLink]="['/objets', r[key]]">Contexte</a>
                    <a routerLink="/impact" [queryParams]="{ objet: r[key] }">Impact</a>
                    <a [routerLink]="['/parcours', r[key]]">Parcours</a>
                  </td>
                }
              </tr>
            } @empty {
              <tr><td [attr.colspan]="c.columns.length + 1" class="muted">Rien ne correspond à cette question dans le référentiel.</td></tr>
            }
          </table>
        </div>
      }
    </div>
  `,
})
export class ListSpacePage {
  private api = inject(Api);
  private router = inject(Router);
  session = inject(Session);
  /** Colonne où s'affiche la marque de la ligne elle-même (un flux) : la première qui n'est pas un objet lié. */
  rowMarkColumn = computed(() => this.config().columns.find((col) => col.kind !== 'objet')?.key ?? '');
  config = input.required<SpaceConfig>();
  /** Question présélectionnée par l'adresse (?question=…), pour arriver depuis l'accueil ou un lien partagé. */
  question = input<string>();

  rows = signal<any[]>([]);
  selected = signal('');
  filter = signal('');
  mode = signal<'liste' | 'matrice'>('liste');
  sortKey = signal('');
  descending = signal(true);
  /** Clé du premier type portant le rôle de l'espace, lue dans le métamodèle ; vide tant qu'elle n'est pas connue. */
  createType = signal('');

  private current = computed(() => this.config().questions.find((q) => q.key === this.selected()));

  shown = computed(() => {
    const q = this.current();
    const needle = this.filter().trim().toLowerCase();
    const keys = this.config().searchKeys;
    let rows = this.rows().filter((r) => (!q?.filter || q.filter(r)) && (!needle || keys.some((k) => String(r[k] ?? '').toLowerCase().includes(needle))));
    const key = this.sortKey();
    if (key) {
      const sign = this.descending() ? -1 : 1;
      rows = [...rows].sort((a, b) => sign * compare(a[key], b[key]));
    }
    return rows;
  });

  constructor() {
    effect(() => {
      const c = this.config();
      this.api.get(c.endpoint).then((rows) => this.rows.set(rows));
    });
    effect(() => {
      const role = this.config().createRole;
      this.createType.set('');
      if (role) this.api.get('/metamodel').then((m) => this.createType.set(firstTypeWith(m, role)));
    });
    effect(() => {
      const c = this.config();
      const wanted = c.questions.find((q) => q.key === this.question()) ?? c.questions[0];
      this.selected.set(wanted.key);
      this.sortKey.set(wanted.sort ?? '');
      this.descending.set(true);
    });
  }

  pick(key: string): void {
    this.router.navigate([], { queryParams: { question: key }, replaceUrl: true }); // l'adresse porte la question : le lien se partage
  }

  sortBy(key: string): void {
    if (this.sortKey() === key) this.descending.update((d) => !d);
    else {
      this.sortKey.set(key);
      this.descending.set(true);
    }
  }
}

/** Premier type actif portant le rôle (à défaut, un type archivé), comme `Metamodel.FirstTypeWith` côté serveur. */
function firstTypeWith(meta: any, role: string): string {
  const types = (meta.objectTypes as any[]).filter((t) => t.roles.includes(role));
  return (types.find((t) => !t.archived) ?? types[0])?.key ?? '';
}

function compare(a: unknown, b: unknown): number {
  if (typeof a === 'number' || typeof b === 'number') return (Number(a) || 0) - (Number(b) || 0);
  return String(a ?? '').localeCompare(String(b ?? ''), 'fr', { numeric: true });
}
