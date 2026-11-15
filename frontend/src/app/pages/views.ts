import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Api, SOURCE_LABELS } from '../api';
import { DiagramNode, DiagramView } from '../diagram-view';
import { NoteGauge } from '../note-gauge';
import { Session } from '../session';
import { readPref, readText, writePref, writeText } from '../prefs';

/**
 * Atelier des vues : le plan occupe tout l'écran disponible. L'arborescence se replie à gauche,
 * la fiche de l'élément sélectionné s'ouvre à droite sans quitter la vue.
 */
@Component({
  imports: [RouterLink, NgTemplateOutlet, DiagramView, NoteGauge],
  host: { '(document:fullscreenchange)': 'syncFullscreen()', '(document:keydown.escape)': 'selected.set(null)', '(document:click)': 'closeMenu($event)' },
  template: `
    <div class="workspace" #workspace>
      @if (treeOpen()) {
        <aside class="pane tree">
          <div class="pane-head">
            <h2>Vues</h2>
            <button type="button" class="icon" title="Replier l'arborescence" aria-label="Replier l'arborescence" (click)="setTree(false)">«</button>
          </div>
          <input type="search" placeholder="Chercher une vue" [value]="treeFilter()" (input)="treeFilter.set($any($event.target).value)" />
          <div class="pane-body">
            @if (treeFilter().trim()) {
              @for (d of matches(); track d.id) {
                <a [routerLink]="['/vues', d.id]" [class.on]="d.id === id()">{{ d.name }}<span class="path">{{ d.path }}</span></a>
              } @empty {
                <p class="muted small">Aucune vue ne porte ce nom.</p>
              }
            } @else {
              <ng-template #folder let-f>
                <details [open]="isOpen(f)">
                  <summary>{{ f.label || '(racine)' }} <span class="count">{{ count(f) }}</span></summary>
                  @for (d of f.diagrams; track d.id) { <a [routerLink]="['/vues', d.id]" [class.on]="d.id === id()">{{ d.name }}</a> }
                  @for (c of f.children; track $index) { <ng-container [ngTemplateOutlet]="folder" [ngTemplateOutletContext]="{ $implicit: c }" /> }
                </details>
              </ng-template>
              @for (f of tree()?.folders ?? []; track $index) {
                @if (count(f) > 0) { <ng-container [ngTemplateOutlet]="folder" [ngTemplateOutletContext]="{ $implicit: f }" /> }
              }
              @for (d of tree()?.unfiled ?? []; track d.id) { <a [routerLink]="['/vues', d.id]" [class.on]="d.id === id()">{{ d.name }}</a> }
            }
          </div>
        </aside>
      }

      <section class="plan">
        <header class="bar">
          @if (!treeOpen()) { <button type="button" class="ghost" (click)="setTree(true)">Vues</button> }
          @if (diagram(); as d) {
            <div class="title">
              <h1 [title]="d.documentation || d.name">{{ d.name }}</h1>
              <span class="muted small">{{ d.nodes.length }} éléments, {{ d.connections.length }} connexions</span>
            </div>
            <input type="search" class="find" placeholder="Repérer un élément" [value]="find()" (input)="find.set($any($event.target).value)" />
            <details class="menu" #display>
              <summary class="btn ghost">Affichage</summary>
              <div class="panel">
                <label>Rendu
                  <select (change)="setExact($any($event.target).value === 'exact')">
                    <option value="soigne" [selected]="!exact()">Soigné (liens courbes)</option>
                    <option value="exact" [selected]="exact()">Géométrie Archi exacte</option>
                  </select>
                </label>
                <label>Couleurs
                  <select (change)="mode.set($any($event.target).value)">
                    <option value="diagramme">de la vue (Archi)</option>
                    <option value="metamodele">du métamodèle</option>
                  </select>
                </label>
                <label>Calque
                  <select [disabled]="exact()" (change)="setLayer($any($event.target).value)">
                    <option value="" [selected]="layer() === ''">Aucun</option>
                    @for (l of d.layers; track l.key) { <option [value]="l.key" [selected]="layer() === l.key">{{ l.label }}</option> }
                  </select>
                </label>
                <label class="check"><input type="checkbox" [checked]="colorize()" [disabled]="exact() || layer() === ''" (change)="setColorize($any($event.target).checked)" />
                  Colorer les cartes selon le calque</label>
                <label class="check"><input type="checkbox" [checked]="dark()" (change)="setDark($any($event.target).checked)" /> Fond sombre</label>
                <p class="muted small">Le calque affiche une propriété en pastille sur les cartes. Les propriétés proposées se règlent dans le métamodèle.</p>
              </div>
            </details>
            <a class="btn ghost" [href]="api.url('/export/archimate?diagram=' + d.id)">Exporter vers Archi</a>
            @if (session.can('contributeur') && !editing()) { <button type="button" class="ghost" (click)="startEditing()">Réorganiser</button> }
          } @else {
            <div class="title"><h1>Vues</h1></div>
          }
          <button type="button" (click)="toggleFullscreen()">{{ fullscreen() ? 'Quitter le plein écran' : 'Plein écran' }}</button>
        </header>

        @if (editing() && diagram(); as d) {
          <div class="editbar" role="toolbar" aria-label="Réorganiser la vue">
            <span class="small">Glissez une forme pour la déplacer (son contenu suit), la poignée en bas à droite pour la redimensionner.</span>
            <button type="button" class="ghost" (click)="plan()?.arrange()">Organiser automatiquement</button>
            @if (d.layout) { <button type="button" class="ghost" [disabled]="saving()" (click)="resetLayout()">Revenir à la disposition d'origine</button> }
            <button type="button" class="ghost" (click)="stopEditing()">Annuler</button>
            <button type="button" [disabled]="saving() || !plan()?.dirty()" (click)="saveLayout()">Enregistrer</button>
          </div>
        }
        @if (diagram(); as d) {
          <app-diagram-view #plan [editing]="editing()" [diagram]="d" [selectedId]="selected()" [find]="find()" [dark]="dark()" [exact]="exact()" [layer]="layer()" [colorize]="colorize()"
            (select)="onSelect($event)" (openObject)="go(['/objets', $event])" (openView)="go(['/vues', $event])" />
        } @else {
          <div class="empty">
            <p><b>Choisissez une vue dans l'arborescence.</b></p>
            <p class="muted">Molette pour zoomer, glisser pour se déplacer, clic sur un élément pour lire sa fiche sans quitter la vue.</p>
          </div>
        }
      </section>

      @if (detail(); as o) {
        <aside class="pane inspector">
          <div class="pane-head">
            <h2><span class="swatch" [style.background]="o.effectiveVisual.fill"></span>{{ o.brief.name || '(sans nom)' }}</h2>
            <button type="button" class="icon" title="Fermer (Échap)" aria-label="Fermer la fiche" (click)="selected.set(null)">×</button>
          </div>
          <div class="pane-body">
            <p class="chips">
              <span class="chip info">{{ o.brief.typeLabel }}</span>
              <span class="chip">{{ o.brief.layerLabel }}</span>
              <span class="chip">{{ sources[o.brief.source] ?? o.brief.source }}</span>
            </p>
            @if (o.object.documentation) { <p class="doc">{{ o.object.documentation }}</p> }

            @if (score(); as s) {
              <h3>Notation d'urbanisation</h3>
              <dl class="notes">
                <dt>Criticité métier</dt><dd><app-note-gauge [value]="s.assessment.cm" /></dd>
                @for (c of s.grid.criteria; track c.code) {
                  <dt [title]="c.label">{{ c.code }} {{ c.label }}</dt><dd><app-note-gauge [value]="s.result.notes[c.code]" [lowUnder]="2" /></dd>
                }
              </dl>
              @if (s.result.computedDecision) {
                <p>Décision calculée : <span class="chip" [class.bad]="s.result.computedDecision === 'À instruire'">{{ s.result.computedDecision }}</span></p>
              } @else {
                <p class="muted small">Notation incomplète : pas de décision calculée.</p>
              }
            }

            @if (o.properties.length) {
              <h3>Propriétés</h3>
              <dl class="props">
                @for (p of o.properties; track $index) { <dt>{{ p.label }}</dt><dd>{{ p.value }}</dd> }
              </dl>
            }

            @if (o.projects.length) {
              <h3>Projets qui le touchent</h3>
              @for (p of o.projects; track p.projectId) {
                <div class="line"><a [routerLink]="['/projets', p.projectId]">{{ p.projectName }}</a> <span class="chip">{{ p.nature }}</span></div>
              }
            }

            <h3>Relations ({{ o.outgoing.length + o.incoming.length }})</h3>
            @for (r of o.outgoing.slice(0, 12); track r.id) {
              <div class="line"><span class="muted">{{ r.typeLabel }} →</span> <a href="" (click)="focusOn(r.other?.id, $event)">{{ r.other?.name }}</a></div>
            }
            @for (r of o.incoming.slice(0, 12); track r.id) {
              <div class="line"><a href="" (click)="focusOn(r.other?.id, $event)">{{ r.other?.name }}</a> <span class="muted">→ {{ r.typeLabel }}</span></div>
            }
            @if (o.outgoing.length > 12 || o.incoming.length > 12) { <p class="muted small">Liste abrégée : la fiche complète les montre toutes.</p> }

            @if (o.diagrams.length > 1) {
              <h3>Aussi présent dans</h3>
              @for (v of o.diagrams; track v.id) {
                @if (v.id !== id()) { <div class="line"><a [routerLink]="['/vues', v.id]">{{ v.name }}</a></div> }
              }
            }
          </div>
          <div class="pane-foot">
            <a class="btn" [routerLink]="['/objets', o.brief.id]">Contexte</a>
            <a class="btn ghost" routerLink="/impact" [queryParams]="{ objet: o.brief.id }">Impact</a>
            @if (o.hasAssessment) { <a class="btn ghost" [routerLink]="['/portefeuille', o.brief.id]">Noter</a> }
          </div>
        </aside>
      }
    </div>
  `,
  styles: `
    .workspace { display: flex; height: 100vh; background: var(--bg); }
    .pane { flex: none; display: flex; flex-direction: column; background: var(--card); min-height: 0; }
    .pane.tree { width: 300px; border-right: 1px solid var(--line); }
    .pane.inspector { width: 360px; border-left: 1px solid var(--line); }
    .pane-head { display: flex; align-items: flex-start; gap: 8px; padding: 14px 14px 10px; }
    .pane-head h2 { flex: 1; margin: 0; font-size: 16px; line-height: 1.25; overflow-wrap: anywhere; }
    .pane > input { margin: 0 14px 10px; }
    .pane-body { flex: 1; overflow: auto; padding: 0 14px 14px; }
    .pane-foot { display: flex; gap: 8px; padding: 12px 14px; border-top: 1px solid var(--line); }
    button.icon { border: 0; background: transparent; color: var(--muted); font-size: 18px; line-height: 1; padding: 4px 8px; }
    button.icon:hover { background: var(--accent-soft); color: var(--ink); }

    .tree details { margin-left: 10px; }
    .tree .pane-body > details { margin-left: 0; }
    .tree summary { padding: 4px 0; font-weight: 600; }
    .tree .count { color: var(--muted); font-weight: 400; font-size: 12px; margin-left: 4px; }
    .tree a { display: block; padding: 4px 8px 4px 14px; border-radius: 5px; text-decoration: none; color: var(--ink); font-size: 13px; }
    .tree a:hover { background: var(--accent-soft); }
    .tree a.on { background: var(--accent); color: #fff; }
    .tree a .path { display: block; font-size: 11px; opacity: .7; }

    .plan { flex: 1; min-width: 0; display: flex; flex-direction: column; }
    .bar { display: flex; align-items: center; gap: 10px; padding: 8px 14px; background: var(--card); border-bottom: 1px solid var(--line); flex-wrap: wrap; }
    .bar .title { flex: 1 1 170px; min-width: 0; }
    .bar h1 { margin: 0; font-size: 17px; line-height: 1.2; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .bar .title span { display: block; white-space: nowrap; }
    .bar .find { width: 150px; }
    .menu { position: relative; }
    .menu summary { list-style: none; padding: 6px 12px; }
    .menu summary::-webkit-details-marker { display: none; }
    .menu[open] summary { background: var(--accent-soft); border-color: var(--accent); }
    .menu .panel { position: absolute; right: 0; top: calc(100% + 6px); z-index: 5; width: 300px; padding: 14px; display: grid; gap: 10px;
      background: var(--card); border: 1px solid var(--line); border-radius: 10px; box-shadow: 0 8px 24px #14262d2e; }
    .menu label { display: grid; gap: 3px; font-size: 12.5px; color: var(--muted); }
    .menu label.check { display: flex; align-items: center; gap: 7px; color: var(--ink); font-size: 13.5px; }
    .menu select { width: 100%; }
    .menu p { margin: 0; }
    app-diagram-view { flex: 1; }
    .editbar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; padding: 8px 14px; background: #e8f3f6; border-bottom: 1px solid #c5dde4; }
    .editbar span { flex: 1; min-width: 240px; }
    .empty { flex: 1; display: grid; place-content: center; text-align: center; padding: 24px; }
    .empty p { margin: 0 0 6px; max-width: 46ch; }

    .inspector h3 { margin: 18px 0 6px; font-size: 12.5px; color: var(--muted); font-weight: 600; }
    .inspector .chips { margin: 0 0 10px; display: flex; gap: 4px; flex-wrap: wrap; }
    .inspector .doc { margin: 0; white-space: pre-wrap; }
    .inspector dl { margin: 0; }
    .inspector dl.props dt { color: var(--muted); font-size: 12px; margin-top: 8px; }
    .inspector dl.props dd { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; }
    .inspector dl.notes { display: grid; grid-template-columns: 1fr auto; gap: 5px 12px; align-items: center; margin-bottom: 10px; }
    .inspector dl.notes dt { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .inspector dl.notes dd { margin: 0; }
    .inspector .line { padding: 3px 0; border-bottom: 1px solid var(--line); overflow-wrap: anywhere; }
  `,
})
export class ViewsPage {
  api = inject(Api);
  private router = inject(Router);
  private workspace = viewChild.required<ElementRef<HTMLElement>>('workspace');
  private display = viewChild<ElementRef<HTMLDetailsElement>>('display');
  plan = viewChild<DiagramView>('plan');
  session = inject(Session);
  editing = signal(false);
  saving = signal(false);

  id = input<string>();
  tree = signal<any>(null);
  diagram = signal<any>(null);
  mode = signal('diagramme');
  find = signal('');
  treeFilter = signal('');
  treeOpen = signal(readPref('vues.arbre', true));
  dark = signal(readPref('vues.sombre', false));
  exact = signal(readPref('vues.exact', false));
  /** Calque choisi par l'utilisateur ; mémorisé dans son navigateur, sans toucher à la vue. */
  layer = signal(readText('vues.calque', 'cm'));
  colorize = signal(readPref('vues.colorer', false));
  fullscreen = signal(false);
  /** Identifiant de l'objet sélectionné dans le plan, et sa fiche chargée. */
  selected = signal<string | null>(null);
  detail = signal<any>(null);
  score = signal<any>(null);
  sources = SOURCE_LABELS;

  /** Toutes les vues à plat, avec leur chemin de dossiers, pour la recherche par nom. */
  private flat = computed(() => {
    const out: { id: string; name: string; path: string }[] = [];
    const walk = (f: any, path: string[]) => {
      const here = f.label ? [...path, f.label] : path;
      for (const d of f.diagrams) out.push({ id: d.id, name: d.name, path: here.join(' / ') });
      for (const c of f.children) walk(c, here);
    };
    for (const f of this.tree()?.folders ?? []) walk(f, []);
    for (const d of this.tree()?.unfiled ?? []) out.push({ id: d.id, name: d.name, path: '' });
    return out;
  });
  matches = computed(() => {
    const q = this.treeFilter().trim().toLowerCase();
    return this.flat().filter((d) => d.name.toLowerCase().includes(q)).slice(0, 80);
  });

  constructor() {
    this.api.get('/folders').then((t) => this.tree.set(t));
    effect(() => {
      const id = this.id();
      const mode = this.mode();
      untracked(() => this.editing.set(false));
      if (!id) return this.diagram.set(null);
      this.api.get(`/diagrams/${id}?visuel=${mode}`).then((d) => this.diagram.set(d));
    });
    // Changer de vue vide la recherche et la sélection, sauf si l'objet sélectionné y figure aussi.
    effect(() => {
      const d = this.diagram();
      const current = untracked(this.selected);
      this.find.set('');
      if (d && current && !d.nodes.some((n: DiagramNode) => n.elementId === current)) this.selected.set(null);
    });
    effect(() => {
      const id = this.selected();
      this.detail.set(null);
      this.score.set(null);
      if (!id) return;
      this.api.get(`/objects/${id}`).then((o) => {
        if (this.selected() !== id) return;
        this.detail.set(o);
        if (o.hasAssessment) this.api.get(`/portfolio/${id}`).then((s) => this.selected() === id && this.score.set(s));
      });
    });
  }

  startEditing(): void {
    this.selected.set(null);
    this.editing.set(true);
  }

  stopEditing(): void {
    this.plan()?.discard();
    this.editing.set(false);
  }

  async saveLayout(): Promise<void> {
    const d = this.diagram();
    const view = this.plan();
    if (!d || !view) return;
    this.saving.set(true);
    try {
      await this.api.put(`/diagrams/${d.id}/layout`, { boxes: view.layoutToSave() });
      this.editing.set(false);
      this.diagram.set(await this.api.get(`/diagrams/${d.id}?visuel=${this.mode()}`));
    } finally {
      this.saving.set(false);
    }
  }

  async resetLayout(): Promise<void> {
    const d = this.diagram();
    if (!d || !confirm('Revenir à la disposition d\'origine ? Les déplacements enregistrés pour cette vue seront perdus.')) return;
    this.saving.set(true);
    try {
      await this.api.delete(`/diagrams/${d.id}/layout`);
      this.editing.set(false);
      this.diagram.set(await this.api.get(`/diagrams/${d.id}?visuel=${this.mode()}`));
    } finally {
      this.saving.set(false);
    }
  }

  onSelect(node: DiagramNode | null): void {
    this.selected.set(node?.elementId ?? null);
  }

  /** Depuis la fiche : sélectionne l'objet lié s'il est sur le plan, sinon ouvre sa fiche complète. */
  focusOn(id: string | undefined, event: Event): void {
    event.preventDefault();
    if (!id) return;
    if (this.diagram()?.nodes.some((n: DiagramNode) => n.elementId === id)) this.selected.set(id);
    else this.go(['/objets', id]);
  }

  setTree(open: boolean): void {
    this.treeOpen.set(open);
    writePref('vues.arbre', open);
  }

  setDark(dark: boolean): void {
    this.dark.set(dark);
    writePref('vues.sombre', dark);
  }

  /** Un clic hors du menu Affichage le referme. */
  closeMenu(event: Event): void {
    const menu = this.display()?.nativeElement;
    if (menu?.open && !menu.contains(event.target as Node)) menu.open = false;
  }

  setExact(exact: boolean): void {
    this.exact.set(exact);
    writePref('vues.exact', exact);
  }

  setLayer(key: string): void {
    this.layer.set(key);
    writeText('vues.calque', key);
  }

  setColorize(on: boolean): void {
    this.colorize.set(on);
    writePref('vues.colorer', on);
  }

  toggleFullscreen(): void {
    if (document.fullscreenElement) document.exitFullscreen();
    else this.workspace().nativeElement.requestFullscreen().catch(() => undefined); // refusé par le navigateur : on reste en fenêtre
  }

  syncFullscreen(): void {
    this.fullscreen.set(document.fullscreenElement !== null);
  }

  /** Nombre de vues dans le dossier, sous-dossiers compris. */
  count(f: any): number {
    return f.diagrams.length + f.children.reduce((sum: number, c: any) => sum + this.count(c), 0);
  }

  isOpen(f: any): boolean {
    const id = this.id();
    const contains = (x: any): boolean => x.diagrams.some((d: any) => d.id === id) || x.children.some(contains);
    return this.count(f) <= 12 || contains(f);
  }

  go(commands: unknown[]): void {
    this.router.navigate(commands);
  }
}
