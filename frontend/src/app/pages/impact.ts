import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ScopeNote } from '../scope-note';
import { Session } from '../session';
import { Router, RouterLink } from '@angular/router';
import { Api, Brief } from '../api';
import { ObjectSearch } from '../object-search';
import { type ReachedItem, type ServesRow, carriers, cellRank, countersOf, natureOf, readingOf, servesTable, subjectOf } from '../impact-reading';

interface TypeInfo { label: string; layer: string; roles: string[]; concept: string | null; }
type Side = 'aval' | 'amont';

/**
 * Analyse d'impact : ce qui porte un objet et ce qui en dépend. La colonne lue en premier, ses titres et ses compteurs
 * suivent la couche de l'objet analysé, et la première colonne dit qui sert qui (maquette « analyse d'impact selon
 * l'objet »). Les décisions de lecture sont dans impact-reading.ts, testé hors navigateur.
 */
@Component({
  imports: [RouterLink, ObjectSearch, ScopeNote, NgTemplateOutlet],
  template: `
    <h1>Analyse d'impact</h1>
    <p class="lead">Choisissez un objet, quel qu'il soit. L'outil suit les relations pour dire ce qui le porte et ce qui en dépend.</p>
    <div class="card">
      <div class="tools" style="margin: 0">
        <div style="flex: 1; min-width: 240px; max-width: 420px"><app-object-search placeholder="Objet à analyser" (picked)="choose($event)" /></div>
        <label>Distance
          <select (change)="depth.set(+$any($event.target).value)">
            @for (n of [1, 2, 3, 4, 5]; track n) { <option [selected]="depth() === n" [value]="n">{{ n }} relation{{ n > 1 ? 's' : '' }}</option> }
          </select>
        </label>
      </div>
    </div>

    @if (subject(); as s) {
      @if (loading()) {
        <div class="card muted">Recherche des relations…</div>
      } @else {
        <div class="stats">
          <div class="stat" style="flex: 2"><b style="font-size: 20px">{{ s.name }}</b><span>{{ s.typeLabel }}, {{ s.layerLabel }} · <a [routerLink]="['/objets', s.id]">contexte</a></span></div>
          @for (c of counters(); track c.label) {
            <div class="stat" [class.alert]="c.alert"><b>{{ c.value }}</b><span>{{ c.label }}</span></div>
          }
        </div>
        <!-- Sans l'infrastructure, l'analyse s'arrête où elle commence : on le dit sous le résultat (maquette périmètre, §3). -->
        @if (session.business()) { <app-scope-note text="Dépendances techniques non affichées : votre accès couvre les processus, applications, flux et données." /> }
        <div class="row">
          @for (side of sides(); track side.key) {
            <div class="card">
              <h2>{{ side.title }}</h2>
              <p class="muted small" style="margin-top: -6px">{{ side.hint }}</p>
              @if (side.table; as t) {
                @if (t.rows.length === 0) {
                  <p class="muted">
                    @if (t.cells === 'technique') { Aucune application ne porte {{ s.name }} dans le référentiel. }
                    @else { Aucune application n'est touchée dans le référentiel. }
                    <a [routerLink]="['/objets', s.id]">Voir la fiche</a>
                  </p>
                } @else {
                  <div class="serves" role="table" [attr.aria-label]="side.title">
                    <div class="head" role="row"><span role="columnheader">{{ t.rowsLabel }}</span><span role="columnheader">{{ t.cellsLabel }}</span></div>
                    @for (r of t.rows; track r.id) {
                      <div class="line" role="row">
                        <span role="cell"><a [routerLink]="['/objets', r.id]">{{ r.name || '(sans nom)' }}</a></span>
                        <span role="cell" class="cell">
                          @for (i of r.direct; track i.id; let last = $last) {
                            <a [routerLink]="['/objets', i.id]">{{ i.name }}</a><span class="muted small type">{{ types()[i.typeKey]?.label }}</span>
                            @if (i.also.length) { <span class="muted small"> (aussi : {{ i.also.join(', ') }})</span> }
                            @if (!last) { <span class="muted"> · </span> }
                          }
                          @if (r.below.length) {
                            @if (open().has(r.id)) {
                              <span class="muted"> · </span>
                              @for (i of r.below; track i.id; let last = $last) {
                                <a [routerLink]="['/objets', i.id]">{{ i.name }}</a><span class="muted small type">{{ types()[i.typeKey]?.label }}</span>
                                @if (i.also.length) { <span class="muted small"> (aussi : {{ i.also.join(', ') }})</span> }
                                @if (!last) { <span class="muted"> · </span> }
                              }
                              <button type="button" class="link small" (click)="toggle(r.id)">replier</button>
                            } @else {
                              <button type="button" class="link small" (click)="toggle(r.id)">+ {{ r.below.length }} en dessous</button>
                            }
                          }
                          @if (r.direct.length === 0 && r.below.length === 0) {
                            <span class="muted small">{{ emptyCell() }}</span>
                            @if (t.cells === 'technique' && !session.business()) { · <a class="small" [routerLink]="['/objets', r.id]">Renseigner</a> }
                          }
                        </span>
                      </div>
                    }
                  </div>
                }
              }
              <!-- Sous un tableau : ce qui est atteint sans en être une ligne, replié, compteur dans le titre (revue de Camille, I7). -->
              @if (side.table && side.groups.length) {
                <details class="others">
                  <summary><h3 style="display: inline">{{ side.table.others }} ({{ count(side.groups) }})</h3></summary>
                  <ng-container *ngTemplateOutlet="groupsTpl; context: { $implicit: side.groups }" />
                </details>
              } @else {
                <ng-container *ngTemplateOutlet="groupsTpl; context: { $implicit: side.groups }" />
                @if (!side.groups.length && !side.table) { <p class="muted">Rien à cette distance.</p> }
              }
            </div>
          }
        </div>
      }
    } @else {
      <div class="card muted">Aucun objet choisi. Cherchez-le par son nom, ou utilisez l'action « Impact » proposée sur les listes et les vues.</div>
    }
    <ng-template #groupsTpl let-groups>
              @for (g of groups; track g.layer) {
                <h3>{{ g.label }} ({{ g.items.length }})</h3>
                @for (r of g.items; track r.id) {
                  <div class="item">
                    <span class="chip" title="Distance, en nombre de relations">{{ r.depth }}</span>
                    <a [routerLink]="['/objets', r.id]">{{ r.name || '(sans nom)' }}</a>
                    <span class="muted small">{{ types()[r.typeKey]?.label ?? r.typeKey }}, via {{ r.via }}</span>
                    <a class="small right" routerLink="/impact" [queryParams]="{ objet: r.id }">Analyser</a>
                  </div>
                }
              }
    </ng-template>
  `,
  styles: `
    h3 { margin: 14px 0 4px; font-size: 12.5px; color: var(--muted); font-weight: 600; }
    details.others { margin-top: 18px; }
    .cell .type { margin-left: 4px; }
    details.others summary { cursor: pointer; }
    .item { display: flex; gap: 8px; align-items: baseline; padding: 4px 0; border-bottom: 1px solid var(--line); flex-wrap: wrap; }
    /* L'alignement remplace la flèche : une ligne par application, ce qui la sert à droite (maquette, §3). */
    .serves .head, .serves .line { display: grid; grid-template-columns: minmax(180px, 1fr) 2fr; gap: 12px; padding: 6px 0; border-bottom: 1px solid var(--line); }
    .serves .head { font-size: 12px; color: var(--muted); font-weight: 600; }
    .serves .cell { line-height: 1.6; }
    button.link { background: none; border: none; padding: 0 0 0 6px; color: var(--accent, #0c5a70); text-decoration: underline; cursor: pointer; }
    @media (max-width: 640px) {
      .serves .head { display: none; }
      .serves .line { grid-template-columns: 1fr; gap: 2px; }
      .serves .cell { padding-left: 16px; }
    }
  `,
})
export class ImpactPage {
  private api = inject(Api);
  private router = inject(Router);
  readonly session = inject(Session);
  /** Objet analysé, porté par l'adresse (?objet=…) : l'analyse se partage par lien. */
  objet = input<string>();
  depth = signal(3);
  subject = signal<Brief | null>(null);
  reached = signal<ReachedItem[]>([]);
  loading = signal(false);
  types = signal<Record<string, TypeInfo>>({});
  open = signal(new Set<string>());
  private layers = signal<{ key: string; label: string; order: number }[]>([]);

  private roles = computed(() => {
    const t = this.types();
    return {
      isApplication: (k: string) => t[k]?.roles.includes('application') ?? false,
      isCapability: (k: string) => t[k]?.roles.includes('capacite') ?? false,
      natureOf: (k: string) => t[k] ? natureOf(t[k].roles, t[k].concept) : 'autre' as const,
    };
  });
  kind = computed(() => {
    const s = this.subject();
    const info = s ? this.types()[s.typeKey] : undefined;
    return info ? subjectOf(natureOf(info.roles, info.concept)) : 'autre';
  });
  reading = computed(() => readingOf(this.kind()));
  /** Applications qui portent l'objet (lignes du tableau), et pas leurs simples voisines. */
  private rows = computed(() => carriers(this.subject()?.id ?? '', this.kind(), this.reached(), this.roles()));
  counters = computed(() => {
    const table = this.sides().find((x) => x.table)?.table;
    const cells = table ? new Set(table.rows.flatMap((r) => [...r.direct, ...r.below].map((i) => i.id))) : undefined;
    return countersOf(this.kind(), this.reached(), this.roles(), this.rows(), cells).filter((c) => !(c.infra && this.session.business()));
  });

  sides = computed(() => {
    const reading = this.reading();
    const roles = this.roles();
    const order: Side[] = reading.first === 'amont' ? ['amont', 'aval'] : ['aval', 'amont'];
    return order.map((key) => {
      const list = this.reached().filter((r) => r.direction === key);
      const spec = key === reading.first ? reading.table : null;
      if (!spec) return { key, title: reading.titles[key], hint: reading.hints[key], table: null, groups: this.group(list) };
      const carrying = this.rows();
      const isRow = (r: ReachedItem) => carrying.has(r.id);
      const isCell = (r: ReachedItem) => spec.cells === 'technique' ? roles.natureOf(r.typeKey) === 'technique' : roles.isCapability(r.typeKey);
      const t = this.types();
      const rank = spec.cells === 'technique' ? (k: string) => cellRank(t[k]?.roles ?? [], t[k]?.concept) : undefined;
      const rows: ServesRow[] = servesTable(this.reached(), key, isRow, isCell, rank);
      const inTable = new Set(rows.flatMap((r) => [r.id, ...r.direct.map((i) => i.id), ...r.below.map((i) => i.id)]));
      return {
        key, title: reading.titles[key], hint: reading.hints[key],
        table: { rows, rowsLabel: spec.rowsLabel, cellsLabel: spec.cellsLabel, cells: spec.cells, others: spec.others },
        groups: this.group(list.filter((r) => !inTable.has(r.id))),
      };
    });
  });

  count(groups: { items: unknown[] }[]): number { return groups.reduce((n, g) => n + g.items.length, 0); }

  /** Cellule vide : jamais blanche (maquette, §4). */
  emptyCell = computed(() => {
    const cells = this.reading().table?.cells;
    if (cells === 'technique') return this.session.business() ? 'Non affichée dans votre accès' : 'Aucune infrastructure connue';
    return 'Aucune capacité connue';
  });

  constructor() {
    this.api.get('/metamodel').then((m) => {
      // Concept : servi par le serveur (Metamodel.ConceptOf), jamais recalculé ici.
      this.types.set(Object.fromEntries(m.objectTypes.map((t: any) => [t.key, { label: t.label, layer: t.layerKey, roles: t.roles ?? [], concept: t.concept ?? null }])));
      this.layers.set(m.layers);
    });
    effect(() => {
      const id = this.objet();
      const depth = this.depth();
      this.open.set(new Set());
      if (!id) {
        this.subject.set(null);
        return this.reached.set([]);
      }
      this.loading.set(true);
      this.api.get(`/objects/${id}`).then((o) => this.subject.set(o.brief)).catch(() => this.subject.set(null));
      this.api.get<ReachedItem[]>(`/analysis/impact/${id}?depth=${depth}&direction=tous`)
        .then((r) => this.reached.set(r))
        .catch(() => this.reached.set([]))
        .finally(() => this.loading.set(false));
    });
  }

  choose(o: Brief): void {
    this.router.navigate([], { queryParams: { objet: o.id } });
  }

  toggle(id: string): void {
    const next = new Set(this.open());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.open.set(next);
  }

  /** Regroupe par couche, dans l'ordre du métamodèle, puis par distance croissante. */
  private group(list: ReachedItem[]): { layer: string; label: string; items: ReachedItem[] }[] {
    return this.layers()
      .slice()
      .sort((a, b) => a.order - b.order)
      .map((l) => ({ layer: l.key, label: l.label, items: list.filter((r) => r.layer === l.key).sort((a, b) => a.depth - b.depth || a.name.localeCompare(b.name, 'fr')) }))
      .filter((g) => g.items.length > 0);
  }
}
