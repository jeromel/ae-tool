import { Component, ElementRef, OnDestroy, computed, effect, input, output, signal, viewChild } from '@angular/core';
import { glyphPath } from './glyphs';
import { Point, Rect, Side, bezier, bezierMiddle, contains, facingSides, polylineMiddle, portPoint, roundedPath } from './geometry';

export interface DiagramNode {
  id: string; kind: string; elementId: string | null; name: string; typeLabel: string | null;
  x: number; y: number; w: number; h: number; fill: string; stroke: string; textColor: string;
  fontSize: number; bold: boolean; shape: string; hasChildren: boolean; viewRefId: string | null;
  template: string | null; glyph: string | null; layers: Record<string, string> | null;
}
interface Connection {
  id: string; sourceNodeId: string; targetNodeId: string; bendpoints: Point[];
  lineStyle: string; softLine: string; head: string; flow: boolean; color: string;
  name: string | null; typeKey: string | null; typeLabel: string | null;
  protocol: string | null; encryption: string | null; carried: string[] | null;
}
export interface DiagramLayer { key: string; label: string; kind: string; }

/** Nœud prêt à dessiner : aspect, police et placement du texte calculés une fois par vue. */
interface NodeView extends DiagramNode {
  look: 'zone' | 'carte' | 'rond' | 'note' | 'exact';
  shown: string;          // couleur de fond affichée (celle du calque si la coloration est active)
  radius: number;
  font: number;           // taille du nom, en unités du plan
  fontMax: number;        // plus grande taille qui tient encore dans la forme sur une ligne
  lines: number;
  text: Rect;             // boîte du nom
  left: boolean;          // nom aligné à gauche (pictogramme devant) ou centré
  glyphBox: Rect | null;
  header: number;         // zone : hauteur libre au-dessus du premier élément contenu
  badge: string | null;   // valeur du calque
  innerZones: string[];   // zone : identifiants des zones qu'elle contient
  innerCards: string[];   // zone : identifiants des éléments (hors zones) qu'elle contient
}
interface Wire {
  id: string; path: string; color: string; dash: string | null; head: string | null; tail: string | null;
  flow: boolean; mid: Point; tag: string | null; lock: boolean; label: string[]; title: string;
  sourceId: string; targetId: string;
}
interface Overlay { id: string; x: number; y: number; font: number; vertical: boolean; legend: boolean; text: string; }

const MARGIN = 32;
const CHAR = 0.56;          // largeur moyenne d'un caractère, en fraction de la taille de police
const MIN_SCREEN_TEXT = 9;  // en dessous, un texte est masqué plutôt qu'affiché illisible
const NOTE_COLORS = ['#cfe6d6', '#f4e6a6', '#f4bd86', '#e58a78'];
const CATEGORY_COLORS = ['#bcdcf5', '#f6d6a8', '#cdebc5', '#f3c4d3', '#d9cdf3', '#f5efaa', '#bfe8e4', '#e3d5c5'];
const DECISION_COLORS: Record<string, string> = {
  Maintenir: '#cdebc5', 'Moderniser / Migrer': '#f6d6a8', 'Éliminer': '#f1b3aa', 'Tolérer': '#bcdcf5', 'À instruire': '#d9cdf3',
};
const NEUTRAL = '#e9edee';

/**
 * Affichage d'une vue en SVG, sur toute la surface disponible.
 *
 * Rendu soigné (par défaut) : les positions d'Archi sont gardées, le dessin suit une grammaire courte.
 * Trois gabarits de nœud (carte, rond, zone), des liens courbes accrochés au côté qui fait face à leur cible,
 * un style par type de relation, une pastille sur les flux, et aucun texte affiché sous 9 px à l'écran.
 * Rendu exact : géométrie, polices et points de cassure d'Archi, pour vérifier une vue.
 *
 * Navigation : molette pour zoomer autour du pointeur, glisser pour déplacer, double-clic sur le fond pour recadrer.
 * Lecture seule dans ce lot ; l'édition graphique relève du lot « modélisation ».
 */
@Component({
  selector: 'app-diagram-view',
  host: { '[class.dark]': 'dark()' },
  template: `
    <div class="stage" #stage [class.grabbing]="panning()" [style.background-size]="gridSize()" [style.background-position]="gridPosition()"
      (wheel)="onWheel($event)" (pointerdown)="onDown($event)" (pointermove)="onMove($event)" (pointerup)="onUp($event)"
      (pointercancel)="onUp($event)" (click)="onBackgroundClick()" (dblclick)="fit()">
      <svg width="100%" height="100%" role="img" [attr.aria-label]="diagram().name">
        <defs>
          <marker id="m-fleche" viewBox="0 0 12 12" refX="11" refY="6" markerWidth="11" markerHeight="11" markerUnits="userSpaceOnUse" orient="auto-start-reverse">
            <path d="M1,1.5 L11,6 L1,10.5 z" fill="context-stroke" />
          </marker>
          <marker id="m-ouverte" viewBox="0 0 12 12" refX="11" refY="6" markerWidth="11" markerHeight="11" markerUnits="userSpaceOnUse" orient="auto-start-reverse">
            <path d="M2,1.5 L11,6 L2,10.5" fill="none" stroke="context-stroke" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" />
          </marker>
          <marker id="m-triangle" viewBox="0 0 14 14" refX="13" refY="7" markerWidth="13" markerHeight="13" markerUnits="userSpaceOnUse" orient="auto-start-reverse">
            <path class="hollow" d="M1.5,1.5 L13,7 L1.5,12.5 z" stroke="context-stroke" stroke-width="1.4" stroke-linejoin="round" />
          </marker>
          <marker id="m-losange" viewBox="0 0 16 10" refX="1" refY="5" markerWidth="15" markerHeight="9" markerUnits="userSpaceOnUse" orient="auto">
            <path d="M1,5 L8,1 L15,5 L8,9 z" fill="context-stroke" />
          </marker>
          <marker id="m-losange-creux" viewBox="0 0 16 10" refX="1" refY="5" markerWidth="15" markerHeight="9" markerUnits="userSpaceOnUse" orient="auto">
            <path class="hollow" d="M1,5 L8,1 L15,5 L8,9 z" stroke="context-stroke" stroke-width="1.3" stroke-linejoin="round" />
          </marker>
          <marker id="m-point" viewBox="0 0 8 8" refX="1" refY="4" markerWidth="7" markerHeight="7" markerUnits="userSpaceOnUse" orient="auto">
            <circle cx="4" cy="4" r="3" fill="context-stroke" />
          </marker>
        </defs>
        <g [attr.transform]="transform()">
          @for (n of nodes(); track n.id) {
            <g class="node" [class]="n.look" [class.link]="n.elementId || n.viewRefId" [class.selected]="n.elementId && n.elementId === selectedId()"
              [class.dim]="isDimmed(n)" (click)="pick(n, $event)" (dblclick)="open(n, $event)">
              @if (n.look === 'carte' || n.look === 'rond') {
                <rect class="shadow" [attr.x]="n.x" [attr.y]="n.y + 2" [attr.width]="n.w" [attr.height]="n.h" [attr.rx]="n.radius" />
              }
              @if (n.look === 'exact' && n.shape === 'ellipse') {
                <ellipse class="body" [attr.cx]="n.x + n.w / 2" [attr.cy]="n.y + n.h / 2" [attr.rx]="n.w / 2" [attr.ry]="n.h / 2"
                  [style.fill]="n.shown" [style.stroke]="edge(n)" />
              } @else {
                <rect class="body" [attr.x]="n.x" [attr.y]="n.y" [attr.width]="n.w" [attr.height]="n.h" [attr.rx]="n.radius"
                  [style.fill]="n.shown" [style.stroke]="n.kind === 'Label' ? 'none' : edge(n)" />
              }
              @if (n.glyphBox; as g) {
                @if (showGlyph(n)) {
                  @if (n.look === 'rond') {
                    <circle class="disc" [attr.cx]="g.x + g.w / 2" [attr.cy]="g.y + g.h / 2" [attr.r]="g.w * 0.72" [style.stroke]="edge(n)" />
                  }
                  <path class="glyph" [attr.d]="glyph(n)" [attr.transform]="'translate(' + g.x + ' ' + g.y + ') scale(' + g.w / 16 + ')'" [style.stroke]="ink(n)" />
                }
              }
              @if (showName(n)) {
                <foreignObject [attr.x]="n.text.x" [attr.y]="n.text.y" [attr.width]="n.text.w" [attr.height]="n.text.h">
                  <div class="label" [class.top]="n.look === 'zone'" [class.left]="n.left" [style.color]="n.look === 'zone' && dark() ? null : n.textColor"
                    [style.font-size.px]="fontOf(n)" [style.font-weight]="n.bold || n.look === 'zone' ? 650 : n.look === 'exact' || n.look === 'note' ? 400 : 550"
                    [title]="n.typeLabel ? n.typeLabel + ' : ' + n.name : n.name">
                    <span [style.-webkit-line-clamp]="linesOf(n)">{{ n.name }}</span>
                  </div>
                </foreignObject>
              }
              @if (n.badge && showBadge(n)) {
                <g class="badge" [attr.transform]="'translate(' + (n.x + n.w - 5) + ' ' + n.y + ') scale(' + fontOf(n) / 14 + ')'">
                  <rect [attr.x]="-badgeWidth(n)" y="-8" [attr.width]="badgeWidth(n)" height="16" rx="8" [style.stroke]="edge(n)" />
                  <text [attr.x]="-badgeWidth(n) / 2" y="3.6" text-anchor="middle">{{ n.badge }}</text>
                </g>
              }
            </g>
          }
          @for (c of wires(); track c.id) {
            <g class="wire" [class.flow]="c.flow" [class.on]="isAttached(c)" [class.dim]="selectedNodeIds().size > 0 && !isAttached(c)">
              <path [attr.d]="c.path" fill="none" [style.stroke]="c.color" [attr.stroke-dasharray]="c.dash"
                [attr.marker-end]="c.head ? 'url(#m-' + c.head + ')' : null" [attr.marker-start]="c.tail ? 'url(#m-' + c.tail + ')' : null">
                <title>{{ c.title }}</title>
              </path>
              @if (c.flow && !exact()) {
                <g [attr.transform]="'translate(' + c.mid.x + ' ' + c.mid.y + ') scale(' + tagScale() + ')'">
                  @if (c.tag && showTags()) {
                    <rect class="tag" [attr.x]="-tagWidth(c) / 2" y="-8" [attr.width]="tagWidth(c)" height="16" rx="8" [style.fill]="c.color" />
                    @if (c.lock) { <path class="lock" [attr.transform]="'translate(' + (-tagWidth(c) / 2 + 5) + ' -5)'" d="M2 4.5h6v5H2zM3.5 4.5V3a1.5 1.5 0 0 1 3 0v1.5" /> }
                    <text class="tagtext" [attr.x]="c.lock ? 5 : 0" y="3.4" text-anchor="middle">{{ c.tag }}</text>
                  } @else {
                    <circle class="tag" r="4" [style.fill]="c.color" />
                  }
                  @if (showFlowLabel(c)) {
                    <text class="flowlabel" text-anchor="middle">
                      @for (line of c.label; track $index) { <tspan x="0" [attr.y]="(c.tag ? 21 : 17) + $index * 12">{{ line }}</tspan> }
                    </text>
                  }
                </g>
              } @else if (c.label.length && showFlowLabel(c)) {
                <text class="flowlabel" text-anchor="middle" [attr.transform]="'translate(' + c.mid.x + ' ' + (c.mid.y - 5) + ') scale(' + tagScale() + ')'">{{ c.label[0] }}</text>
              }
            </g>
          }
          @for (o of overlays(); track o.id) {
            <text class="overlay" [class.edge]="o.legend" [attr.text-anchor]="o.legend ? 'start' : 'middle'" dominant-baseline="central" [attr.font-size]="o.font"
              [attr.transform]="'translate(' + o.x + ' ' + o.y + ')' + (o.vertical ? ' rotate(-90)' : '')">{{ o.text }}</text>
          }
        </g>
      </svg>

      @if (legend(); as l) {
        <div class="legend" (pointerdown)="$event.stopPropagation()">
          <b>{{ l.title }}</b>
          @for (e of l.entries; track e.label) { <span><i [style.background]="e.color"></i>{{ e.label }}</span> }
        </div>
      }
      <div class="dock" (pointerdown)="$event.stopPropagation()" (click)="$event.stopPropagation()" (dblclick)="$event.stopPropagation()">
        <button type="button" title="Zoom avant" aria-label="Zoom avant" (click)="zoomBy(1.25)">+</button>
        <span>{{ percent() }} %</span>
        <button type="button" title="Zoom arrière" aria-label="Zoom arrière" (click)="zoomBy(0.8)">−</button>
        <button type="button" class="wide" title="Afficher toute la vue (double-clic sur le fond)" (click)="fit()">Tout voir</button>
        <button type="button" class="wide" title="Taille réelle" (click)="actualSize()">1:1</button>
      </div>
    </div>
  `,
  styles: `
    :host { display: block; height: 100%; min-height: 0; --stage: #f7f9f9; --dot: #c9d3d6; --zone-ink: #14262d; --flow: #1f6f8b; --link: #66787f; }
    :host(.dark) { --stage: #0f1e24; --dot: #2a3f47; --zone-ink: #e4ecee; --flow: #6fc3d8; --link: #91a6ae; }
    .stage { position: relative; height: 100%; overflow: hidden; cursor: grab; touch-action: none; user-select: none;
      background-color: var(--stage); background-image: radial-gradient(var(--dot) 1px, transparent 1.4px); }
    .stage.grabbing { cursor: grabbing; }
    svg { display: block; }

    .node.link { cursor: pointer; }
    .node .body { stroke-width: 1; vector-effect: non-scaling-stroke; }
    .node .shadow { fill: #14262d; opacity: .14; }
    :host(.dark) .node .shadow { fill: #000; opacity: .45; }
    .node.zone > .body { fill-opacity: .28; stroke-width: 1.2; }
    :host(.dark) .node.zone > .body { fill-opacity: .15; }
    .node.zone:not(.link) > .body { stroke-dasharray: 5 4; }
    .node.link:hover > .body { stroke-width: 2.2; }
    .node.selected > .body { stroke: var(--signal, #e5a50a) !important; stroke-width: 3; }
    .node.dim { opacity: .18; }
    .glyph { fill: none; stroke-width: 1.35; stroke-linecap: round; stroke-linejoin: round; }
    .disc { fill: #fff; fill-opacity: .7; stroke-width: 1; vector-effect: non-scaling-stroke; }
    .badge rect { fill: #fff; stroke-width: 1; vector-effect: non-scaling-stroke; }
    .badge text { font: 600 10px 'Segoe UI', system-ui, sans-serif; fill: #14262d; }

    .label { height: 100%; display: flex; align-items: center; justify-content: center; text-align: center; padding: 0 2px;
      line-height: 1.16; overflow: hidden; font-family: 'Segoe UI', system-ui, sans-serif; }
    .label span { display: -webkit-box; -webkit-box-orient: vertical; overflow: hidden; overflow-wrap: anywhere; }
    .label.left { justify-content: flex-start; text-align: left; }
    .label.top { align-items: flex-start; justify-content: flex-start; text-align: left; color: var(--zone-ink); }

    .wire path { stroke-width: 1.4; vector-effect: non-scaling-stroke; }
    .wire.flow path { stroke-width: 1.9; }
    .hollow { fill: var(--stage); }
    .tag { stroke: var(--stage); stroke-width: 1.5; }
    .tagtext { font: 700 8.5px 'Segoe UI', system-ui, sans-serif; fill: #fff; letter-spacing: .02em; }
    :host(.dark) .tagtext { fill: #0f1e24; }
    .lock { fill: none; stroke: #fff; stroke-width: 1.2; }
    :host(.dark) .lock { stroke: #0f1e24; }
    .flowlabel { font: 10px 'Segoe UI', system-ui, sans-serif; fill: var(--zone-ink); paint-order: stroke; stroke: var(--stage); stroke-width: 3.5px; stroke-linejoin: round; }
    .wire.on path { stroke: var(--signal, #e5a50a) !important; stroke-width: 2.6; }
    .wire.dim { opacity: .22; }
    .overlay { font-family: Bahnschrift, 'DIN Alternate', 'Segoe UI', system-ui, sans-serif; font-weight: 600; fill: var(--zone-ink);
      paint-order: stroke; stroke: var(--stage); stroke-opacity: .85; stroke-width: .28em; stroke-linejoin: round; pointer-events: none; }
    .overlay.edge { stroke-opacity: 1; stroke-width: .5em; }

    .legend, .dock { position: absolute; bottom: 14px; background: var(--card, #fff); border: 1px solid var(--line, #d9e0e2); border-radius: 10px;
      box-shadow: 0 4px 14px #14262d1f; cursor: default; }
    .legend { left: 14px; padding: 8px 12px; display: flex; flex-direction: column; gap: 4px; max-width: 260px; font-size: 12px; }
    .legend b { font-size: 12px; }
    .legend span { display: flex; align-items: center; gap: 7px; }
    .legend i { width: 12px; height: 12px; border-radius: 3px; border: 1px solid #0002; flex: none; }
    .dock { right: 14px; display: flex; flex-direction: column; align-items: stretch; gap: 2px; padding: 4px; }
    .dock button { border: 0; background: transparent; color: var(--ink, #14262d); min-width: 34px; height: 30px; padding: 0 6px; border-radius: 6px;
      font-size: 17px; line-height: 1; }
    .dock button.wide { font-size: 11.5px; font-weight: 600; }
    .dock button:hover { background: var(--accent-soft, #e0eef2); }
    .dock span { text-align: center; font-size: 11px; color: var(--muted, #5c6c73); font-variant-numeric: tabular-nums; }
  `,
})
export class DiagramView implements OnDestroy {
  diagram = input.required<any>();
  /** Identifiant de l'objet sélectionné : toutes ses occurrences et leurs connexions sont mises en évidence. */
  selectedId = input<string | null>(null);
  /** Texte recherché : les éléments qui ne le contiennent pas sont estompés. */
  find = input('');
  dark = input(false);
  /** Rendu exact : géométrie, polices et points de cassure d'Archi. */
  exact = input(false);
  /** Calque : clé de la propriété affichée en pastille sur les cartes ('' = aucun). */
  layer = input('');
  /** Colore les cartes selon la valeur du calque. */
  colorize = input(false);

  select = output<DiagramNode | null>();
  openObject = output<string>();
  openView = output<string>();

  /** Échelle et décalage du plan, en pixels d'écran. */
  scale = signal(1);
  private offset = signal<Point>({ x: 0, y: 0 });
  panning = signal(false);
  percent = computed(() => Math.round(this.scale() * 100));
  transform = computed(() => `translate(${this.offset().x} ${this.offset().y}) scale(${this.scale()})`);
  gridSize = computed(() => {
    const step = 24 * this.scale();
    return `${step}px ${step}px`;
  });
  gridPosition = computed(() => `${this.offset().x}px ${this.offset().y}px`);

  /** Pastilles et libellés de flux grossissent quand on dézoome, jusqu'à 3,2 fois, pour rester lisibles. */
  tagScale = computed(() => Math.min(3.2, Math.max(1, 1 / this.scale())));
  showTags = computed(() => this.scale() * this.tagScale() >= 0.8);

  private stage = viewChild.required<ElementRef<HTMLElement>>('stage');
  private observer?: ResizeObserver;
  /** Vrai tant que l'utilisateur n'a ni zoomé ni déplacé : la vue se recadre alors seule quand la surface change. */
  private autoFit = true;
  private drag: { x: number; y: number; origin: Point; moved: boolean } | null = null;
  private justDragged = false;

  private activeLayer = computed<DiagramLayer | null>(
    () => ((this.diagram().layers ?? []) as DiagramLayer[]).find((l) => l.key === this.layer()) ?? null,
  );

  /** Couleur de chaque valeur du calque, dans l'ordre de la légende. */
  private layerColors = computed(() => {
    const layer = this.activeLayer();
    const colors = new Map<string, string>();
    if (!layer) return colors;
    const values = new Set<string>();
    for (const n of this.diagram().nodes as DiagramNode[]) {
      const v = n.layers?.[layer.key];
      if (v) values.add(v);
    }
    const sorted = [...values].sort((a, b) => a.localeCompare(b, 'fr', { numeric: true }));
    sorted.forEach((v, i) => {
      if (layer.kind === 'note') colors.set(v, NOTE_COLORS[Math.min(NOTE_COLORS.length, Math.max(1, Math.round(+v))) - 1]);
      else colors.set(v, (layer.key === 'decision' && DECISION_COLORS[v]) || CATEGORY_COLORS[i % CATEGORY_COLORS.length]);
    });
    return colors;
  });

  legend = computed(() => {
    const layer = this.activeLayer();
    if (!layer || !this.colorize() || this.exact()) return null;
    const entries = [...this.layerColors()].map(([label, color]) => ({ label, color }));
    return { title: layer.label, entries: [...entries, { label: 'Sans valeur', color: NEUTRAL }] };
  });

  nodes = computed<NodeView[]>(() => {
    const all = this.diagram().nodes as DiagramNode[];
    const exact = this.exact();
    const layer = this.activeLayer();
    const colors = this.layerColors();
    const colorize = this.colorize() && !exact && layer !== null;
    // Un nœud qui en contient d'autres (imbriqués ou simplement posés dessus) devient une zone.
    const inside = (n: DiagramNode) => all.filter((o) => o !== n && contains(n, o) && o.w * o.h < n.w * n.h);
    const zones = new Set(all.filter((n) => n.hasChildren || inside(n).length > 0).map((n) => n.id));
    const views = all.map((n) => {
      const kids = zones.has(n.id) ? inside(n) : [];
      const isZone = zones.has(n.id) || (!exact && n.template === 'zone');
      const look: NodeView['look'] = isZone ? 'zone' : exact ? 'exact' : !n.elementId ? 'note' : n.template === 'rond' && n.w >= 1.6 * n.h ? 'rond' : 'carte';
      const value = layer && n.elementId ? (n.layers?.[layer.key] ?? null) : null;
      const view: NodeView = {
        ...n, look, hasChildren: isZone,
        // Calque coloré : une carte sans valeur garde sa teinte, très estompée, pour ne pas concurrencer les autres.
        shown: colorize && look !== 'zone' && n.elementId ? (value ? colors.get(value)! : `color-mix(in srgb, ${n.fill} 22%, ${NEUTRAL})`) : n.fill,
        radius: n.kind === 'Label' ? 3 : look === 'zone' ? 9 : look === 'rond' ? n.h / 2 : look === 'exact' ? (n.shape === 'arrondi' ? 12 : 2) : Math.min(8, n.h / 5),
        font: n.fontSize * 1.33, fontMax: n.fontSize * 1.33, lines: 6, innerCards: [], left: false, glyphBox: null, badge: exact || look === 'zone' ? null : shorten(value, 16),
        text: { x: n.x + 6, y: n.y + 2, w: n.w - 12, h: n.h - 4 },
        header: kids.length ? Math.max(0, Math.min(...kids.map((k) => k.y)) - n.y) : n.h,
        innerZones: kids.filter((k) => zones.has(k.id)).map((k) => k.id),
      };
      if (look === 'zone') layoutZone(view, exact);
      else if (look === 'carte' || look === 'rond') layoutCard(view);
      return view;
    });
    for (const z of views) if (zones.has(z.id)) z.innerCards = inside(z).filter((k) => !zones.has(k.id)).map((k) => k.id);
    return views;
  });

  selectedNodeIds = computed(() => {
    const id = this.selectedId();
    return new Set(id ? this.nodes().filter((n) => n.elementId === id).map((n) => n.id) : []);
  });

  box = computed(() => {
    const ns = this.nodes();
    if (ns.length === 0) return { x: 0, y: 0, w: 400, h: 200 };
    const x = Math.min(...ns.map((n) => n.x));
    const y = Math.min(...ns.map((n) => n.y));
    return { x, y, w: Math.max(...ns.map((n) => n.x + n.w)) - x, h: Math.max(...ns.map((n) => n.y + n.h)) - y };
  });

  wires = computed<Wire[]>(() => {
    const byId = new Map(this.nodes().map((n) => [n.id, n]));
    const dark = this.dark();
    const exact = this.exact();
    const drawn = (this.diagram().connections as Connection[])
      .map((c) => ({ c, s: byId.get(c.sourceNodeId), t: byId.get(c.targetNodeId) }))
      .filter((e): e is { c: Connection; s: NodeView; t: NodeView } => !!e.s && !!e.t); // extrémité portée par une autre connexion : non dessinée

    // Rendu soigné : chaque lien s'accroche au côté qui fait face à l'autre extrémité, et les liens d'un même côté s'y répartissent.
    const sides = new Map<string, [Side, Side]>();
    const slots = new Map<string, { id: string; along: number }[]>();
    if (!exact) {
      for (const { c, s, t } of drawn) {
        const pair = facingSides(s, t);
        sides.set(c.id, pair);
        const push = (node: NodeView, side: Side, other: NodeView, end: string) => {
          const key = node.id + side;
          const along = side === 'E' || side === 'W' ? other.y + other.h / 2 : other.x + other.w / 2;
          slots.set(key, [...(slots.get(key) ?? []), { id: c.id + end, along }]);
        };
        push(s, pair[0], t, '>');
        push(t, pair[1], s, '<');
      }
      for (const list of slots.values()) list.sort((a, b) => a.along - b.along);
    }
    const port = (node: NodeView, side: Side, id: string): Point => {
      const list = slots.get(node.id + side)!;
      return portPoint(node, side, list.findIndex((e) => e.id === id), list.length);
    };

    return drawn.map(({ c, s, t }) => {
      let path: string;
      let mid: Point;
      if (exact) {
        const centre = (n: DiagramNode) => ({ x: n.x + n.w / 2, y: n.y + n.h / 2 });
        const via = c.bendpoints;
        const points = [borderPoint(s, via[0] ?? centre(t)), ...via, borderPoint(t, via[via.length - 1] ?? centre(s))];
        path = roundedPath(points, 8);
        mid = polylineMiddle(points);
      } else {
        const [a, b] = sides.get(c.id)!;
        const p = port(s, a, c.id + '>');
        const q = port(t, b, c.id + '<');
        const nested = contains(s, t) || contains(t, s);
        path = bezier(p, a, q, b, nested);
        mid = bezierMiddle(p, a, q, b, nested);
      }
      const custom = luminance(c.color) > 0.25; // couleur choisie dans Archi : on la garde
      const line = exact ? c.lineStyle : c.softLine;
      const head = exact ? 'fleche' : c.head;
      return {
        id: c.id, path, mid, flow: c.flow,
        color: custom ? c.color : exact ? (dark ? '#9db3bb' : '#40535b') : c.flow ? 'var(--flow)' : 'var(--link)',
        dash: line === 'tirets' ? '7 5' : line === 'pointillés' ? '2 4' : null,
        head: head === 'aucune' || head.startsWith('losange') ? null : head === 'point' ? 'fleche' : head,
        tail: exact ? null : head.startsWith('losange') || head === 'point' ? head : null,
        tag: shorten(c.protocol?.split(/[\s,;/(]+/)[0]?.toUpperCase() ?? null, 7),
        lock: !!c.encryption && !/^(non|aucun|none|no)$/i.test(c.encryption.trim()),
        label: wrap(c.name, 30, 2),
        title: [
          [c.typeLabel ?? c.typeKey, c.name].filter(Boolean).join(' : '),
          c.protocol ? 'Protocole : ' + c.protocol : '',
          c.encryption ? 'Chiffrement : ' + c.encryption : '',
          c.carried?.length ? 'Transporte : ' + c.carried.join(', ') : '',
        ].filter(Boolean).join('\n'),
        sourceId: s.id, targetId: t.id,
      };
    });
  });

  /**
   * Titres de zones posés en surimpression quand leur bandeau est trop petit pour être lu, comme les noms de régions
   * d'une carte routière. Une zone cède la place à celles qu'elle contient dès que l'une d'elles devient lisible.
   */
  overlays = computed<Overlay[]>(() => {
    if (this.exact()) return [];
    const k = this.scale();
    const byId = new Map(this.nodes().map((n) => [n.id, n]));
    const zones = this.nodes().filter((n) => n.look === 'zone' && n.name);
    const centred = new Map<string, Overlay>();
    const legends: Overlay[] = [];
    for (const z of zones) {
      if (z.font * k >= MIN_SCREEN_TEXT) continue; // le bandeau se lit : pas de surimpression
      // Assez large : le titre se pose à cheval sur le bord supérieur, comme la légende d'un cadre.
      const edgeFont = 11.5 / k;
      const room = Math.floor((z.w * 0.86) / (CHAR * edgeFont));
      const contentReads = z.innerCards.some((id) => this.showName(byId.get(id)!));
      if (room >= Math.min(z.name.length, 12) || (contentReads && room >= 5)) {
        legends.push({ id: z.id, x: z.x + Math.min(z.w * 0.04, 14 / k), y: z.y, font: edgeFont, vertical: false, legend: true, text: shorten(z.name, room)! });
        continue;
      }
      if (contentReads) continue; // trop étroite pour un titre, et l'écrire en travers masquerait des noms lisibles
      // Trop étroite et contenu illisible à cette échelle : le titre s'inscrit en grand, couché si la zone est en hauteur.
      const flat = Math.min((z.w * 0.9) / (CHAR * z.name.length), z.h * 0.5);
      const tall = Math.min((z.h * 0.9) / (CHAR * z.name.length), z.w * 0.5);
      const font = Math.min(Math.max(flat, tall), 17 / k);
      if (font * k < 10) continue;
      centred.set(z.id, { id: z.id, x: z.x + z.w / 2, y: z.y + z.h / 2, font, vertical: tall > flat, legend: false, text: z.name });
    }
    return [...legends, ...zones.filter((z) => centred.has(z.id) && !z.innerZones.some((id) => centred.has(id))).map((z) => centred.get(z.id)!)];
  });

  constructor() {
    // À l'ouverture d'une vue, on la cadre en entier dans la surface disponible.
    effect(() => {
      this.diagram();
      this.autoFit = true;
      requestAnimationFrame(() => this.fit());
    });
    effect(() => {
      this.observer?.disconnect();
      this.observer = new ResizeObserver(() => {
        if (this.autoFit) this.fit();
      });
      this.observer.observe(this.stage().nativeElement);
    });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  // ---- Zoom sémantique : ce qui ne se lirait pas à l'écran n'est pas affiché.

  showName(n: NodeView): boolean {
    return n.name.length > 0 && (n.look === 'exact' || n.look === 'note' || this.exact() || this.fontOf(n) * this.scale() >= (n.look === 'zone' ? MIN_SCREEN_TEXT : 7.5));
  }

  /** Taille du nom : quand on dézoome, il grossit jusqu'à remplir sa forme pour rester lisible. */
  fontOf(n: NodeView): number {
    if (n.look !== 'carte' && n.look !== 'rond') return n.font;
    const target = 10.5 / this.scale();
    if (n.font >= target) return n.font;
    if (n.fontMax >= target) return target;
    // Le nom entier ne tient pas à cette taille : on l'abrège si au moins six caractères restent visibles.
    const abridged = target * 1.15 <= n.text.h && n.text.w / (CHAR * target) >= 6;
    return abridged ? target : n.fontMax;
  }

  linesOf(n: NodeView): number {
    return n.look === 'zone' ? 1 : Math.max(1, Math.floor(n.text.h / (this.fontOf(n) * 1.16)));
  }

  showGlyph(n: NodeView): boolean {
    return n.glyphBox !== null && n.glyphBox.w * this.scale() >= 9;
  }

  showBadge(n: NodeView): boolean {
    return this.fontOf(n) * 0.72 * this.scale() >= 7;
  }

  showFlowLabel(c: Wire): boolean {
    return c.label.length > 0 && (this.isAttached(c) || this.scale() >= 0.5);
  }

  badgeWidth(n: NodeView): number {
    return (n.badge?.length ?? 0) * 5.6 + 14;
  }

  tagWidth(c: Wire): number {
    return (c.tag?.length ?? 0) * 5.4 + 12 + (c.lock ? 10 : 0);
  }

  glyph(n: NodeView): string {
    return glyphPath(n.glyph);
  }

  /** Cadre toute la vue, sans jamais l'agrandir au-delà de 125 %. */
  fit(): void {
    const el = this.stage().nativeElement;
    const b = this.box();
    const k = Math.min(1.25, (el.clientWidth - 2 * MARGIN) / b.w, (el.clientHeight - 2 * MARGIN) / b.h);
    const scale = Math.max(0.05, k);
    this.scale.set(scale);
    this.offset.set({ x: (el.clientWidth - b.w * scale) / 2 - b.x * scale, y: (el.clientHeight - b.h * scale) / 2 - b.y * scale });
    this.autoFit = true;
  }

  actualSize(): void {
    const el = this.stage().nativeElement;
    this.zoomAt(1 / this.scale(), el.clientWidth / 2, el.clientHeight / 2);
  }

  zoomBy(factor: number): void {
    const el = this.stage().nativeElement;
    this.zoomAt(factor, el.clientWidth / 2, el.clientHeight / 2);
  }

  /** Zoome en gardant fixe le point d'écran (px, py). */
  private zoomAt(factor: number, px: number, py: number): void {
    const before = this.scale();
    const after = Math.min(4, Math.max(0.05, before * factor));
    const o = this.offset();
    this.offset.set({ x: px - ((px - o.x) * after) / before, y: py - ((py - o.y) * after) / before });
    this.scale.set(after);
    this.autoFit = false;
  }

  onWheel(event: WheelEvent): void {
    event.preventDefault();
    const rect = this.stage().nativeElement.getBoundingClientRect();
    this.zoomAt(Math.exp(-event.deltaY * 0.0015), event.clientX - rect.left, event.clientY - rect.top);
  }

  onDown(event: PointerEvent): void {
    if (event.button !== 0) return;
    this.drag = { x: event.clientX, y: event.clientY, origin: this.offset(), moved: false };
  }

  onMove(event: PointerEvent): void {
    const d = this.drag;
    if (!d) return;
    const dx = event.clientX - d.x;
    const dy = event.clientY - d.y;
    if (!d.moved && Math.hypot(dx, dy) < 4) return; // en dessous de 4 px, c'est un clic
    if (!d.moved) {
      d.moved = true;
      this.panning.set(true);
      this.stage().nativeElement.setPointerCapture(event.pointerId);
    }
    this.offset.set({ x: d.origin.x + dx, y: d.origin.y + dy });
    this.autoFit = false;
  }

  onUp(event: PointerEvent): void {
    if (this.drag?.moved) {
      this.stage().nativeElement.releasePointerCapture(event.pointerId);
      this.justDragged = true;
      setTimeout(() => (this.justDragged = false));
    }
    this.drag = null;
    this.panning.set(false);
  }

  onBackgroundClick(): void {
    if (!this.justDragged) this.select.emit(null);
  }

  pick(n: DiagramNode, event: Event): void {
    event.stopPropagation();
    if (this.justDragged) return;
    if (n.elementId) this.select.emit(n);
    else if (n.viewRefId) this.openView.emit(n.viewRefId);
    else this.select.emit(null);
  }

  open(n: DiagramNode, event: Event): void {
    event.stopPropagation();
    if (n.elementId) this.openObject.emit(n.elementId);
  }

  isDimmed(n: NodeView): boolean {
    const q = this.find().trim().toLowerCase();
    return q.length > 0 && n.look !== 'zone' && !n.name.toLowerCase().includes(q);
  }

  isAttached(c: Wire): boolean {
    const ids = this.selectedNodeIds();
    return ids.has(c.sourceId) || ids.has(c.targetId);
  }

  /** Contour : la couleur de fond assombrie, pour que chaque forme garde sa teinte sans cerne noir. */
  edge(n: NodeView): string {
    return `color-mix(in srgb, ${n.shown} 58%, ${this.dark() ? '#ffffff' : '#14262d'})`;
  }

  /** Trait du pictogramme : la teinte de la famille, nettement assombrie. */
  ink(n: NodeView): string {
    return `color-mix(in srgb, ${n.shown} 30%, #14262d)`;
  }
}

/** Carte ou rond : pictogramme devant le nom si la forme est allongée, sinon nom centré et pictogramme en coin. */
function layoutCard(n: NodeView): void {
  const pad = Math.max(5, Math.min(12, n.h * 0.16));
  const wide = n.w >= 2 * n.h;
  if (wide) {
    const g = Math.max(11, Math.min(n.h * 0.44, 30));
    const inset = n.look === 'rond' ? (n.h - g) / 2 : pad;
    n.glyphBox = { x: n.x + inset, y: n.y + (n.h - g) / 2, w: g, h: g };
    n.text = { x: n.x + inset + g + pad * 0.8, y: n.y + 2, w: n.w - inset - g - pad * 1.8, h: n.h - 4 };
    n.left = true;
  } else {
    const g = Math.max(9, Math.min(n.h * 0.2, 16));
    n.glyphBox = n.h >= 44 && n.w >= 70 ? { x: n.x + 5, y: n.y + 5, w: g, h: g } : null;
    n.text = { x: n.x + pad, y: n.y + 2, w: n.w - 2 * pad, h: n.h - 4 };
  }
  // La plus grande police qui tient sur une, deux ou trois lignes, plafonnée à 30 % de la hauteur de la forme.
  let best = 0;
  let lines = 1;
  for (let count = 1; count <= 3; count++) {
    const perLine = Math.ceil(n.name.length / count) + (count > 1 ? 3 : 0);
    const size = Math.min(n.text.w / (CHAR * Math.max(perLine, 3)), n.text.h / (1.2 * count));
    if (size > best * 1.12) {
      best = size;
      lines = count;
    }
  }
  n.font = Math.max(7, Math.min(best, n.h * 0.3, 44));
  n.fontMax = Math.max(n.font, Math.min(best, n.text.h * 0.72));
  n.lines = lines;
}

/** Zone : titre en haut à gauche, dans l'espace laissé libre au-dessus des éléments contenus. */
function layoutZone(n: NodeView, exact: boolean): void {
  n.left = true;
  if (exact) {
    n.text = { x: n.x + 6, y: n.y + 4, w: n.w - 12, h: Math.min(n.h, n.fontSize * 4) };
    return;
  }
  const room = n.header < n.h ? n.header : n.h * 0.3;
  n.font = Math.max(9, Math.min(room * 0.62, n.fontSize * 1.33 * 1.5, n.h * 0.2, 30));
  n.lines = 1;
  n.text = { x: n.x + 8, y: n.y + Math.max(2, (room - n.font * 1.25) / 2), w: n.w - 16, h: n.font * 1.3 };
}

/** Point du bord du nœud sur le segment qui relie son centre au point visé (rendu exact). */
function borderPoint(n: Rect, toward: Point): Point {
  const cx = n.x + n.w / 2;
  const cy = n.y + n.h / 2;
  const dx = toward.x - cx;
  const dy = toward.y - cy;
  if (dx === 0 && dy === 0) return { x: cx, y: cy };
  const k = Math.min(dx === 0 ? Infinity : n.w / 2 / Math.abs(dx), dy === 0 ? Infinity : n.h / 2 / Math.abs(dy));
  return { x: Math.round(cx + dx * Math.min(k, 1)), y: Math.round(cy + dy * Math.min(k, 1)) };
}

function luminance(hex: string): number {
  const m = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!m) return 1;
  const v = parseInt(m[1], 16);
  return (0.299 * (v >> 16) + 0.587 * ((v >> 8) & 255) + 0.114 * (v & 255)) / 255;
}

function shorten(text: string | null | undefined, max: number): string | null {
  if (!text) return null;
  return text.length > max ? text.slice(0, max - 1).trimEnd() + '…' : text;
}

/** Coupe un libellé aux espaces en lignes d'au plus « width » caractères ; le surplus est abrégé. */
function wrap(text: string | null, width: number, maxLines: number): string[] {
  if (!text) return [];
  const lines: string[] = [];
  let current = '';
  for (const word of text.trim().split(/\s+/)) {
    if (current && (current + ' ' + word).length > width) {
      lines.push(current);
      current = word;
    } else current = current ? current + ' ' + word : word;
  }
  if (current) lines.push(current);
  if (lines.length > maxLines) {
    lines.length = maxLines;
    lines[maxLines - 1] = shorten(lines[maxLines - 1] + ' …', width)!;
  }
  return lines.map((l) => shorten(l, width + 2)!);
}
