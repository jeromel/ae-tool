import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Api, Brief } from '../api';
import { Session } from '../session';
import { ObjectSearch } from '../object-search';
import { ScopeNote } from '../scope-note';
import { LAYER_LABELS, type StepItem, SHOWN, emptyText, groupByCapability, hueOf, moreText, touchedSummary, viaText } from '../chains-reading';

interface Column { id: string; name: string; kind: string; capabilities: StepItem[]; layers: Record<string, StepItem[]>; }
interface Chain { id: string; name: string; hasSteps: boolean; ordered: boolean; columns: Column[]; whole: Column | null; }
interface ProcessView { id: string; name: string; ordered: boolean; columns: Column[]; }
interface OffChain { id: string; name: string; nature: string | null; topCapability: string | null; }
interface Grid {
  serversAvailable: boolean; natureKnown: boolean; layers: string[]; domains: { id: string; name: string }[];
  domainOf: Record<string, string>; chains: Chain[]; processes: ProcessView[]; offChain: OffChain[];
}
interface Touched { chainId: string; columnId: string; via: StepItem[]; }
interface Impact { object: Brief; layer: string | null; steps: Touched[]; offChain: string[]; }

/**
 * Chaînes et processus (EV-06, maquette `maquette-chaines-couches-impact-camille`, ratifiée le 27/11). Toutes les chaînes
 * l'une sous l'autre, leurs étapes de gauche à droite, une couche au choix dessous ; un objet désigné met en évidence les
 * étapes qu'il touche, avec le chemin. Un tableau HTML par chaîne (§6) : les chevrons et les cartes sont du style.
 * Couche, objet et domaine sont dans l'adresse : le lien se partage (C10).
 */
@Component({
  imports: [RouterLink, NgTemplateOutlet, ObjectSearch, ScopeNote],
  template: `
    <h1>Chaînes et processus</h1>
    <p class="lead">Ce que l'entreprise produit, étape par étape, et ce qui porte chaque étape.</p>

    <div class="settings no-print">
      <fieldset class="segmented">
        <legend>Afficher sous les étapes :</legend>
        @for (l of layers(); track l) {
          <label [class.on]="layer() === l"><input type="radio" name="couche" [value]="l" [checked]="layer() === l" (change)="setLayer(l)" />{{ label(l) }}</label>
        }
      </fieldset>
      <div class="pick">
        <span>Étapes touchées par :</span>
        @if (impact(); as im) {
          <span class="chosen">{{ im.object.name }} <button type="button" class="x" aria-label="Effacer l'objet désigné" (click)="designate(null)">×</button></span>
        } @else {
          <app-object-search placeholder="Serveur, application ou projet…" [types]="searchTypes()" [showAll]="false" (picked)="designate($event.id)" />
        }
      </div>
    </div>
    @if (grid() && !grid()!.serversAvailable) { <app-scope-note text="Serveurs non affichés pour votre accès." /> }
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }

    @if (impact(); as im) {
      <div class="impact-line" role="status">
        @if (im.layer) {
          {{ summary() }}
        } @else {
          Les étapes touchées se lisent à partir d'un serveur, d'une application ou d'un projet.
        }
        <a [routerLink]="['/objets', im.object.id]">Ouvrir la carte</a>
        <a routerLink="/impact" [queryParams]="{ objet: im.object.id }">Analyser l'impact</a>
      </div>
    }

    @if (grid(); as g) {
      @if (domainsShown().length) {
        <div class="legend no-print" aria-label="Domaines">
          @for (d of domainsShown(); track d.id) {
            <button type="button" class="dom" [class.off]="domain() && domain() !== d.id" [attr.aria-pressed]="domain() === d.id"
              [style.--fond]="fond(d.id)" [style.--trait]="trait(d.id)" [style.--texte]="texte(d.id)" (click)="setDomain(d.id)">{{ d.name }}</button>
          }
        </div>
      }
      @if (g.chains.length === 0) { <p>Aucune chaîne de valeur dans le référentiel.</p> }
      @for (c of shownChains(); track c.id) {
        <ng-container *ngTemplateOutlet="chainTpl; context: { $implicit: c }" />
      }
      @if (hiddenChains().length) {
        <details class="others">
          <summary>{{ hiddenChains().length }} {{ hiddenChains().length > 1 ? 'chaînes non touchées' : 'chaîne non touchée' }}</summary>
          @for (c of hiddenChains(); track c.id) { <ng-container *ngTemplateOutlet="chainTpl; context: { $implicit: c }" /> }
        </details>
      }

      @if (g.processes.length) {
        <h2>Processus par étapes</h2>
        @for (p of g.processes; track p.id) {
          <section class="chain card">
            <header><h3>{{ p.name }}</h3>@if (!p.ordered) { <span class="muted small" title="L'ordre affiché n'est pas un enchaînement">Ordre incomplet ⓘ</span> }</header>
            <div class="scroll-x">
              <table>
                <thead><tr><th scope="col" class="corner"></th>
                  @for (s of p.columns; track s.id; let last = $last) {
                    <th scope="col"><span class="step" [class.chevron]="p.ordered">{{ s.name }}</span>@if (p.ordered && !last) { <span class="sr"> puis</span> }</th>
                  }
                </tr></thead>
                <tbody>
                  @for (l of ['qui', 'donnees', 'applications']; track l) {
                    <tr><th scope="row">{{ label(l) }}</th>
                      @for (s of p.columns; track s.id) {
                        <td>
                          @for (i of s.layers[l]; track i.id + (i.via ?? '')) {
                            <div class="item"><a [routerLink]="['/objets', i.id]">{{ i.name }}</a>@if (via(i); as v) { <span class="via">{{ l === 'donnees' ? (i.via === 'écrit' ? '✎ ' : '◉ ') + v : v }}</span> }</div>
                          } @empty { <span class="gap">Rien de connu</span> }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }
      }

      @if (g.offChain.length) {
        <details class="others" [open]="touchedOff().length > 0">
          <summary>{{ g.offChain.length }} processus hors de toute chaîne@if (touchedOff().length) { — {{ touchedOff().length }} {{ touchedOff().length > 1 ? 'touchés' : 'touché' }} }</summary>
          @if (g.natureKnown) {
            <h4>Pilotage et support : hors chaîne par nature</h4>
            <ul>@for (p of offSupport(); track p.id) { <li [class.hit]="touchedOff().includes(p.id)"><a [routerLink]="['/objets', p.id]">{{ p.name }}</a> <span class="via">{{ p.topCapability }}</span></li> }</ul>
            <h4>Rattachés à aucune chaîne</h4>
            <ul>@for (p of offOther(); track p.id) { <li [class.hit]="touchedOff().includes(p.id)"><a [routerLink]="['/objets', p.id]">{{ p.name }}</a> <span class="via">{{ p.topCapability }}</span> · <a [routerLink]="['/objets', p.id]">Rattacher</a></li> }</ul>
          } @else {
            <ul>@for (p of g.offChain; track p.id) { <li [class.hit]="touchedOff().includes(p.id)"><a [routerLink]="['/objets', p.id]">{{ p.name }}</a>@if (p.topCapability) { <span class="via">{{ p.topCapability }}</span> }@if (touchedOff().includes(p.id)) { <b> ● touché</b> }</li> }</ul>
          }
        </details>
      }
    } @else if (!error()) {
      <p class="muted">Chargement des chaînes…</p>
    }

    <ng-template #chainTpl let-c>
      <section class="chain card" [attr.aria-label]="c.name">
        <header>
          <h3>{{ c.name }}</h3>
          @if (!c.hasSteps) { <span class="muted small" title="L'ordre affiché n'est pas un enchaînement">Étapes non renseignées ⓘ</span> }
          @else if (!c.ordered) { <span class="muted small" title="L'ordre affiché n'est pas un enchaînement">Ordre incomplet ⓘ</span> }
        </header>
        <div class="scroll-x">
          <table>
            <thead>
              <tr>
                <th scope="col" class="corner"></th>
                @for (s of c.columns; track s.id; let last = $last) {
                  <th scope="col" [class.hit]="isTouched(c.id, s.id)">
                    <button type="button" class="step" [class.chevron]="c.ordered" [class.card-step]="!c.ordered" (click)="openPanel(c.id, s.id)">
                      {{ s.name }}@if (isTouched(c.id, s.id)) { <span class="touched">● touchée</span> }
                    </button>@if (c.ordered && !last) { <span class="sr"> puis</span> }
                  </th>
                }
                @if (c.whole) { <th scope="col" class="whole" [class.hit]="isTouched(c.id, c.whole.id)"><button type="button" class="step card-step dashed" (click)="openPanel(c.id, c.whole.id)">Toute la chaîne@if (isTouched(c.id, c.whole.id)) { <span class="touched">● touchée</span> }</button></th> }
              </tr>
            </thead>
            <tbody>
              @if (impact()?.layer) {
                <tr class="touch-row"><th scope="row">Touchée par</th>
                  @for (s of columnsOf(c); track s.id) {
                    <td [class.hit]="isTouched(c.id, s.id)" [class.whole]="s.kind === 'toute'">
                      @if (touch(c.id, s.id); as t) { touchée {{ t.via.length ? 'via ' + names(t.via) : '' }} } @else { — }
                    </td>
                  }
                </tr>
              }
              <tr>
                <th scope="row">{{ label(layer()) }}</th>
                @for (s of columnsOf(c); track s.id) {
                  <td [class.hit]="isTouched(c.id, s.id)" [class.whole]="s.kind === 'toute'">
                    @for (gr of groups(s); track gr.capability?.id ?? 'reste') {
                      <div class="dom-card" [class.faded]="faded(gr.capability?.id)" [style.--fond]="fond(gr.capability?.id)" [style.--trait]="trait(gr.capability?.id)" [style.--texte]="texte(gr.capability?.id)">
                        @if (layer() !== 'capacites' && gr.capability) { <div class="dom-title">{{ gr.capability.name }}</div> }
                        @for (i of shown(s.id, gr.capability?.id, gr.items); track i.id + (i.via ?? '')) {
                          <div class="item" [class.cap]="layer() === 'capacites'">
                            <button type="button" class="name" [class.chosen-item]="impact()?.object?.id === i.id" (click)="designate(i.id)">{{ i.name }}</button>
                            @if (via(i); as v) { <span class="via">{{ v }}</span> }
                          </div>
                        }
                        @if (more(s.id, gr.capability?.id, gr.items.length); as m) {
                          <button type="button" class="link small" (click)="expand(s.id, gr.capability?.id)">{{ m }}</button>
                        }
                      </div>
                    } @empty {
                      <span class="gap">{{ empty().text }}@if (empty().link) { · <a [routerLink]="['/objets', s.kind === 'capacite' ? s.id : s.capabilities[0]?.id ?? s.id]">Renseigner</a> }</span>
                    }
                  </td>
                }
              </tr>
            </tbody>
          </table>
        </div>
        @if (panelOf(c.id); as col) {
          <div class="panel" role="region" [attr.aria-label]="col.name + ', ce que l\\'étape mobilise'">
            <div class="panel-head"><b>{{ col.name }} — ce que l'étape mobilise</b><button type="button" class="ghost" (click)="panel.set(null)">Fermer</button></div>
            <dl>
              @for (l of layers(); track l) {
                <dt>{{ label(l) }}</dt>
                <dd>
                  @for (i of col.layers[l]; track i.id + (i.via ?? '')) {
                    <span class="pitem"><a [routerLink]="['/objets', i.id]">{{ i.name }}</a>@if (via(i); as v) { <span class="via"> ({{ v }})</span> }@if (l === 'applications' || l === 'serveurs') { · <a routerLink="/impact" [queryParams]="{ objet: i.id }">Analyser l'impact</a> }</span>
                  } @empty { <span class="gap">{{ emptyOf(l) }}</span> }
                </dd>
              }
            </dl>
          </div>
        }
      </section>
    </ng-template>
  `,
  styles: `
    .settings { display: flex; flex-wrap: wrap; gap: 14px 28px; align-items: center; margin-bottom: 12px; }
    fieldset.segmented { border: 0; padding: 0; margin: 0; display: flex; align-items: center; gap: 0; flex-wrap: wrap; }
    fieldset.segmented legend { float: left; margin-right: 10px; font-size: 13.5px; color: var(--muted); }
    .segmented label { border: 1px solid var(--line); padding: 6px 12px; cursor: pointer; background: var(--card); font-size: 13.5px; margin-left: -1px; }
    .segmented label:first-of-type { border-radius: 6px 0 0 6px; } .segmented label:last-of-type { border-radius: 0 6px 6px 0; }
    .segmented label.on { background: var(--accent); color: #fff; border-color: var(--accent); }
    .segmented input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .segmented label:focus-within { outline: 2px solid var(--signal, #f2b705); outline-offset: 1px; }
    .pick { display: flex; align-items: center; gap: 8px; min-width: 340px; font-size: 13.5px; color: var(--muted); }
    .pick app-object-search { flex: 1; }
    .chosen { background: var(--card); border: 1px solid var(--line); border-radius: 6px; padding: 5px 8px; color: var(--ink); font-weight: 600; }
    .x { background: none; border: 0; padding: 0 4px; color: var(--muted); cursor: pointer; font-size: 16px; }
    .impact-line { margin: 0 0 12px; display: flex; gap: 14px; flex-wrap: wrap; align-items: baseline; }
    .legend { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 10px; }
    .dom { background: var(--fond, var(--bg)); border: 1px solid var(--trait, var(--line)); color: var(--texte, var(--ink)); border-radius: 12px; padding: 3px 10px; font-size: 12.5px; font-weight: 600; }
    .dom.off { opacity: 0.35; }
    .chain { padding: 12px 14px; }
    .chain header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 8px; }
    .chain h3 { margin: 0; font-size: 17px; }
    .scroll-x { overflow-x: auto; }
    table { border-collapse: separate; border-spacing: 6px 4px; width: 100%; table-layout: fixed; }
    th, td { vertical-align: top; text-align: left; padding: 0; }
    th.corner, tbody th { width: 120px; font-size: 12.5px; color: var(--muted); font-weight: 600; padding-top: 6px; position: sticky; left: 0; background: var(--card); z-index: 1; }
    .step { display: block; width: 100%; text-align: left; font: inherit; font-weight: 700; font-size: 13.5px; color: var(--ink); background: var(--accent-soft, #e0eef2); border: 0; padding: 8px 22px 8px 12px; cursor: pointer; min-height: 40px; white-space: normal; line-height: 1.25; }
    .step.chevron { clip-path: polygon(0 0, calc(100% - 14px) 0, 100% 50%, calc(100% - 14px) 100%, 0 100%, 10px 50%); padding-left: 18px; }
    .step.card-step { border-radius: 8px; }
    .step.dashed { background: transparent; border: 1.5px dashed var(--line); color: var(--muted); }
    th.hit .step { background: var(--accent); color: #fff; }
    th.hit .step.dashed { border-color: var(--accent); color: var(--accent); background: transparent; }
    .touched { display: block; font-size: 11.5px; font-weight: 600; }
    td.whole, th.whole { border-left: 1px solid var(--line); padding-left: 6px; }
    td.hit { box-shadow: inset 3px 0 0 var(--accent); background: #f2f8fa; }
    .touch-row td { font-size: 12.5px; color: var(--ink); padding: 2px 6px; }
    .dom-card { background: var(--fond, #f6f8f8); border-left: 3px solid var(--trait, var(--line)); border-radius: 6px; padding: 5px 6px; margin-bottom: 5px; }
    .dom-card.faded { opacity: 0.35; }
    .dom-title { font-size: 11.5px; font-weight: 700; color: var(--texte, var(--muted)); margin-bottom: 3px; }
    .item { background: #fff; border: 1px solid #0000001a; border-radius: 5px; padding: 3px 6px; margin-top: 3px; font-size: 12.5px; overflow-wrap: anywhere; }
    .item.cap { background: transparent; border: 0; padding: 0; color: var(--texte); font-weight: 600; }
    .item .name { background: none; border: 0; padding: 0; font: inherit; color: inherit; text-align: left; cursor: pointer; font-weight: 600; }
    .item .name:hover { text-decoration: underline; background: none; }
    .item .name.chosen-item { outline: 2px solid var(--signal, #f2b705); }
    .via { display: block; color: var(--muted); font-size: 11.5px; font-weight: 400; }
    .gap { color: var(--muted); font-size: 12.5px; }
    .link { background: none; border: 0; padding: 0; color: var(--accent); text-decoration: underline; cursor: pointer; }
    .panel { margin-top: 10px; border-top: 1px solid var(--line); padding-top: 8px; }
    .panel-head { display: flex; justify-content: space-between; align-items: center; }
    .panel dl { display: grid; grid-template-columns: 120px 1fr; gap: 4px 12px; margin: 8px 0 0; font-size: 13px; }
    .panel dt { color: var(--muted); }
    .pitem { margin-right: 14px; }
    .pitem .via { display: inline; }
    .others { margin: 10px 0; }
    .others summary { cursor: pointer; color: var(--accent); font-weight: 600; }
    .others li.hit { font-weight: 600; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    @media print { .step { print-color-adjust: exact; } .panel { display: none; } }
  `,
})
export class ChainsPage {
  private api = inject(Api);
  private router = inject(Router);
  session = inject(Session);
  /** Réglages portés par l'adresse (`?couche=`, `?objet=`, `?domaine=`). */
  couche = input<string>();
  objet = input<string>();
  domaine = input<string>();

  grid = signal<Grid | null>(null);
  impact = signal<Impact | null>(null);
  error = signal('');
  meta = signal<any>(null);
  panel = signal<{ chainId: string; columnId: string } | null>(null);
  expanded = signal<Set<string>>(new Set());

  layers = computed(() => this.grid()?.layers ?? ['capacites', 'processus', 'applications', 'projets']);
  layer = computed(() => (this.couche() && this.layers().includes(this.couche()!) ? this.couche()! : 'capacites'));
  domain = computed(() => this.domaine() ?? null);
  empty = computed(() => emptyText(this.layer()));
  /** Recherche limitée aux serveurs, applications et projets (et sans serveurs pour le lecteur « métier »). */
  searchTypes = computed(() => {
    const m = this.meta();
    if (!m) return [];
    const servers = this.grid()?.serversAvailable ?? false;
    return (m.objectTypes as any[]).filter((t) => t.roles.includes('application') || t.roles.includes('projet')
      || (servers && (t.concept === 'Node' || t.concept === 'Device'))).map((t) => t.key);
  });
  touchedChains = computed(() => new Set((this.impact()?.steps ?? []).map((t) => t.chainId)));
  shownChains = computed(() => {
    const g = this.grid();
    if (!g) return [];
    return this.impact()?.layer && this.touchedChains().size ? g.chains.filter((c) => this.touchedChains().has(c.id)) : g.chains;
  });
  hiddenChains = computed(() => {
    const g = this.grid();
    if (!g || !this.impact()?.layer || this.touchedChains().size === 0) return [];
    return g.chains.filter((c) => !this.touchedChains().has(c.id));
  });
  touchedOff = computed(() => this.impact()?.offChain ?? []);
  offSupport = computed(() => (this.grid()?.offChain ?? []).filter((p) => p.nature === 'support' || p.nature === 'pilotage'));
  offOther = computed(() => (this.grid()?.offChain ?? []).filter((p) => p.nature !== 'support' && p.nature !== 'pilotage'));
  /** Domaines présents à l'écran, dans l'ordre de la carte des capacités. */
  domainsShown = computed(() => {
    const g = this.grid();
    if (!g) return [];
    const present = new Set(Object.values(g.domainOf));
    return g.domains.filter((d) => present.has(d.id));
  });
  summary = computed(() => {
    const im = this.impact();
    return im ? touchedSummary(im.object.name, im.object.typeLabel, im.steps.length, this.touchedChains().size) : '';
  });

  constructor() {
    this.api.get('/metamodel').then((m) => this.meta.set(m)).catch(() => {});
    this.api.get('/chaines').then((g) => this.grid.set(g)).catch((e) => this.error.set((e as Error).message));
    effect(() => {
      const id = this.objet();
      if (!id) { this.impact.set(null); return; }
      this.api.get(`/chaines/impact/${encodeURIComponent(id)}`).then((r) => this.impact.set(r)).catch(() => this.impact.set(null));
    });
  }

  label(l: string): string { return LAYER_LABELS[l] ?? l; }
  via = viaText;
  names(items: StepItem[]): string { return items.map((i) => i.name).join(', '); }
  emptyOf(l: string): string { return emptyText(l).text; }

  columnsOf(c: Chain): Column[] { return c.whole ? [...c.columns, c.whole] : c.columns; }
  groups(s: Column) { return groupByCapability(s.layers[this.layer()] ?? [], s.capabilities); }

  private key(col: string, cap: string | undefined): string { return `${col}|${cap ?? ''}`; }
  shown(col: string, cap: string | undefined, items: StepItem[]): StepItem[] {
    return this.expanded().has(this.key(col, cap)) ? items : items.slice(0, SHOWN);
  }
  more(col: string, cap: string | undefined, count: number): string | null {
    return this.expanded().has(this.key(col, cap)) ? null : moreText(count);
  }
  expand(col: string, cap: string | undefined): void { this.expanded.update((s) => new Set(s).add(this.key(col, cap))); }

  private hue(cap: string | undefined | null): string | null {
    const g = this.grid();
    if (!g || !cap) return null;
    const top = g.domainOf[cap] ?? cap;
    return hueOf(g.domains.findIndex((d) => d.id === top));
  }
  fond(cap?: string | null): string | null { const h = this.hue(cap); return h ? `var(--dom-${h}-fond)` : null; }
  trait(cap?: string | null): string | null { const h = this.hue(cap); return h ? `var(--dom-${h}-trait)` : null; }
  texte(cap?: string | null): string | null { const h = this.hue(cap); return h ? `var(--dom-${h}-texte)` : null; }
  faded(cap?: string | null): boolean {
    const d = this.domain();
    if (!d || !cap) return false;
    return (this.grid()?.domainOf[cap] ?? cap) !== d;
  }

  touch(chainId: string, columnId: string): Touched | undefined {
    return this.impact()?.steps.find((t) => t.chainId === chainId && t.columnId === columnId);
  }
  isTouched(chainId: string, columnId: string): boolean { return !!this.touch(chainId, columnId); }

  panelOf(chainId: string): Column | null {
    const p = this.panel();
    if (!p || p.chainId !== chainId) return null;
    const c = this.grid()?.chains.find((x) => x.id === chainId);
    return c ? this.columnsOf(c).find((s) => s.id === p.columnId) ?? null : null;
  }
  openPanel(chainId: string, columnId: string): void {
    const p = this.panel();
    this.panel.set(p?.chainId === chainId && p.columnId === columnId ? null : { chainId, columnId });
  }

  private go(params: Record<string, string | null>): void {
    this.router.navigate([], { queryParams: params, queryParamsHandling: 'merge', replaceUrl: true });
  }
  setLayer(l: string): void { this.go({ couche: l === 'capacites' ? null : l }); }
  designate(id: string | null): void { this.go({ objet: id }); }
  setDomain(id: string): void { this.go({ domaine: this.domain() === id ? null : id }); }
}
