import { Component, ElementRef, OnDestroy, computed, effect, input, output, signal, viewChild } from '@angular/core';

export interface DiagramNode {
  id: string; kind: string; elementId: string | null; name: string; typeLabel: string | null;
  x: number; y: number; w: number; h: number; fill: string; stroke: string; textColor: string;
  fontSize: number; bold: boolean; shape: string; icon: string | null; hasChildren: boolean; viewRefId: string | null;
}
interface Connection {
  id: string; sourceNodeId: string; targetNodeId: string; bendpoints: Point[];
  lineStyle: string; color: string; name: string | null; typeKey: string | null;
}
interface Point { x: number; y: number; }
interface Line {
  id: string; path: string; color: string; dash: string | null; title: string;
  label: string | null; mid: Point; sourceId: string; targetId: string;
}

const MARGIN = 32;

/**
 * Affichage d'une vue en SVG, sur toute la surface disponible.
 * Navigation : molette pour zoomer autour du pointeur, glisser pour déplacer, double-clic sur le fond pour recadrer.
 * Un clic sélectionne un élément (le parent décide quoi afficher), un double-clic ouvre sa fiche.
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
          <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M0,1 L10,5 L0,9 z" fill="context-stroke" />
          </marker>
        </defs>
        <g [attr.transform]="transform()">
          @for (n of nodes(); track n.id) {
            <g class="node" [class.link]="n.elementId || n.viewRefId" [class.zone]="n.hasChildren" [class.note]="!n.elementId"
              [class.selected]="n.elementId && n.elementId === selectedId()" [class.dim]="isDimmed(n)"
              (click)="pick(n, $event)" (dblclick)="open(n, $event)">
              @if (!n.hasChildren && n.elementId) {
                <rect class="shadow" [attr.x]="n.x" [attr.y]="n.y + 2" [attr.width]="n.w" [attr.height]="n.h" [attr.rx]="radius(n)" />
              }
              @if (n.shape === 'ellipse') {
                <ellipse class="body" [attr.cx]="n.x + n.w / 2" [attr.cy]="n.y + n.h / 2" [attr.rx]="n.w / 2" [attr.ry]="n.h / 2"
                  [attr.fill]="n.fill" [style.stroke]="edge(n)" />
              } @else {
                <rect class="body" [attr.x]="n.x" [attr.y]="n.y" [attr.width]="n.w" [attr.height]="n.h" [attr.rx]="radius(n)"
                  [attr.fill]="n.fill" [style.stroke]="n.kind === 'Label' ? 'none' : edge(n)" />
              }
              <foreignObject [attr.x]="n.x" [attr.y]="n.y" [attr.width]="n.w" [attr.height]="n.hasChildren ? minOf(n.h, n.fontSize * 4) : n.h">
                <div class="label" [class.top]="n.hasChildren" [style.color]="n.hasChildren && dark() ? null : n.textColor"
                  [style.font-size.px]="n.fontSize * 1.33" [style.font-weight]="n.bold || n.hasChildren ? 650 : 400"
                  [title]="n.typeLabel ? n.typeLabel + ' : ' + n.name : n.name">
                  {{ n.name }}
                </div>
              </foreignObject>
            </g>
          }
          @for (c of lines(); track c.id) {
            <g class="wire" [class.on]="isAttached(c)" [class.dim]="selectedNodeIds().size > 0 && !isAttached(c)">
              <path [attr.d]="c.path" fill="none" [attr.stroke]="c.color" [attr.stroke-dasharray]="c.dash" marker-end="url(#arrow)">
                <title>{{ c.title }}</title>
              </path>
              @if (c.label) { <text [attr.x]="c.mid.x" [attr.y]="c.mid.y - 4" text-anchor="middle">{{ c.label }}</text> }
            </g>
          }
        </g>
      </svg>

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
    :host { display: block; height: 100%; min-height: 0; --stage: #f7f9f9; --dot: #c9d3d6; --zone-ink: #14262d; --wire-halo: #f7f9f9; }
    :host(.dark) { --stage: #0f1e24; --dot: #2a3f47; --zone-ink: #e4ecee; --wire-halo: #0f1e24; }
    .stage { position: relative; height: 100%; overflow: hidden; cursor: grab; touch-action: none; user-select: none;
      background-color: var(--stage); background-image: radial-gradient(var(--dot) 1px, transparent 1.4px); }
    .stage.grabbing { cursor: grabbing; }
    svg { display: block; }

    .node.link { cursor: pointer; }
    .node .body { stroke-width: 1; vector-effect: non-scaling-stroke; }
    .node .shadow { fill: #14262d; opacity: .13; }
    :host(.dark) .node .shadow { fill: #000; opacity: .45; }
    .node.zone > .body { fill-opacity: .3; stroke-width: 1.2; }
    :host(.dark) .node.zone > .body { fill-opacity: .16; }
    .node.zone.note > .body { stroke-dasharray: 5 4; }
    .node.link:hover > .body { stroke-width: 2.2; }
    .node.selected > .body { stroke: var(--signal, #e5a50a) !important; stroke-width: 3; }
    .node.dim { opacity: .18; }

    .label { height: 100%; display: flex; align-items: center; justify-content: center; text-align: center; padding: 3px 8px;
      line-height: 1.18; overflow: hidden; font-family: 'Segoe UI', system-ui, sans-serif; }
    .label.top { align-items: flex-start; justify-content: flex-start; text-align: left; padding: 6px 10px; color: var(--zone-ink); }

    .wire path { stroke-width: 1.3; vector-effect: non-scaling-stroke; }
    .wire text { font: 10px 'Segoe UI', system-ui, sans-serif; fill: var(--zone-ink); paint-order: stroke; stroke: var(--wire-halo); stroke-width: 4px; }
    .wire.on path { stroke: var(--signal, #e5a50a); stroke-width: 2.4; }
    .wire.dim { opacity: .25; }

    .dock { position: absolute; right: 14px; bottom: 14px; display: flex; flex-direction: column; align-items: stretch; gap: 2px; padding: 4px;
      background: var(--card, #fff); border: 1px solid var(--line, #d9e0e2); border-radius: 10px; box-shadow: 0 4px 14px #14262d1f; cursor: default; }
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

  private stage = viewChild.required<ElementRef<HTMLElement>>('stage');
  private observer?: ResizeObserver;
  /** Vrai tant que l'utilisateur n'a ni zoomé ni déplacé : la vue se recadre alors seule quand la surface change. */
  private autoFit = true;
  private drag: { x: number; y: number; origin: Point; moved: boolean } | null = null;
  private justDragged = false;

  /** Un nœud qui en contient d'autres (imbriqués ou simplement posés dessus) devient une zone, libellé en haut à gauche. */
  nodes = computed<DiagramNode[]>(() => {
    const all = this.diagram().nodes as DiagramNode[];
    return all.map((n) => ({
      ...n,
      hasChildren:
        n.hasChildren ||
        all.some((o) => o !== n && o.x >= n.x && o.y >= n.y && o.x + o.w <= n.x + n.w && o.y + o.h <= n.y + n.h && o.w * o.h < n.w * n.h),
    }));
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

  lines = computed<Line[]>(() => {
    const byId = new Map(this.nodes().map((n) => [n.id, n]));
    const dark = this.dark();
    const result: Line[] = [];
    for (const c of this.diagram().connections as Connection[]) {
      const s = byId.get(c.sourceNodeId);
      const t = byId.get(c.targetNodeId);
      if (!s || !t) continue; // extrémité portée par une autre connexion : non dessinée
      const centre = (n: DiagramNode) => ({ x: n.x + n.w / 2, y: n.y + n.h / 2 });
      const via = c.bendpoints;
      const points = [this.border(s, via[0] ?? centre(t)), ...via, this.border(t, via[via.length - 1] ?? centre(s))];
      result.push({
        id: c.id,
        path: roundedPath(points, 8),
        color: readable(c.color, dark),
        dash: c.lineStyle === 'tirets' ? '6 4' : c.lineStyle === 'pointillés' ? '2 3' : null,
        title: [c.typeKey, c.name].filter(Boolean).join(' : '),
        label: c.name || null,
        mid: middle(points),
        sourceId: s.id,
        targetId: t.id,
      });
    }
    return result;
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

  isDimmed(n: DiagramNode): boolean {
    const q = this.find().trim().toLowerCase();
    return q.length > 0 && !n.hasChildren && !n.name.toLowerCase().includes(q);
  }

  isAttached(c: Line): boolean {
    const ids = this.selectedNodeIds();
    return ids.has(c.sourceId) || ids.has(c.targetId);
  }

  radius(n: DiagramNode): number {
    if (n.kind === 'Label') return 3;
    return n.hasChildren ? 9 : n.shape === 'arrondi' ? 14 : 5;
  }

  /** Contour : la couleur de fond assombrie, pour que chaque forme garde sa teinte sans cerne noir. */
  edge(n: DiagramNode): string {
    return `color-mix(in srgb, ${n.fill} 58%, ${this.dark() ? '#ffffff' : '#14262d'})`;
  }

  minOf(a: number, b: number): number {
    return Math.min(a, b);
  }

  /** Point du bord du nœud sur le segment qui relie son centre au point visé. */
  private border(n: DiagramNode, toward: Point): Point {
    const cx = n.x + n.w / 2;
    const cy = n.y + n.h / 2;
    const dx = toward.x - cx;
    const dy = toward.y - cy;
    if (dx === 0 && dy === 0) return { x: cx, y: cy };
    const k = Math.min(dx === 0 ? Infinity : n.w / 2 / Math.abs(dx), dy === 0 ? Infinity : n.h / 2 / Math.abs(dy));
    return { x: Math.round(cx + dx * Math.min(k, 1)), y: Math.round(cy + dy * Math.min(k, 1)) };
  }
}

/** Tracé passant par les points, coudes arrondis au rayon donné (réduit si un segment est trop court). */
function roundedPath(points: Point[], radius: number): string {
  let d = `M${points[0].x},${points[0].y}`;
  for (let i = 1; i < points.length - 1; i++) {
    const a = points[i - 1];
    const p = points[i];
    const b = points[i + 1];
    const la = Math.hypot(p.x - a.x, p.y - a.y);
    const lb = Math.hypot(b.x - p.x, b.y - p.y);
    const r = Math.min(radius, la / 2, lb / 2);
    if (r < 1) {
      d += ` L${p.x},${p.y}`;
      continue;
    }
    const before = { x: p.x - ((p.x - a.x) * r) / la, y: p.y - ((p.y - a.y) * r) / la };
    const after = { x: p.x + ((b.x - p.x) * r) / lb, y: p.y + ((b.y - p.y) * r) / lb };
    d += ` L${round(before.x)},${round(before.y)} Q${p.x},${p.y} ${round(after.x)},${round(after.y)}`;
  }
  const last = points[points.length - 1];
  return `${d} L${last.x},${last.y}`;
}

/** Milieu du tracé, mesuré le long des segments : c'est là que se pose le libellé de la relation. */
function middle(points: Point[]): Point {
  const lengths = points.slice(1).map((p, i) => Math.hypot(p.x - points[i].x, p.y - points[i].y));
  let remaining = lengths.reduce((a, b) => a + b, 0) / 2;
  for (let i = 0; i < lengths.length; i++) {
    if (remaining <= lengths[i] && lengths[i] > 0) {
      const t = remaining / lengths[i];
      return { x: points[i].x + (points[i + 1].x - points[i].x) * t, y: points[i].y + (points[i + 1].y - points[i].y) * t };
    }
    remaining -= lengths[i];
  }
  return points[0];
}

/** Une connexion noire ou très sombre est adoucie sur fond clair et éclaircie sur fond sombre ; les couleurs choisies sont gardées. */
function readable(hex: string, dark: boolean): string {
  const m = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!m) return hex;
  const v = parseInt(m[1], 16);
  const luminance = (0.299 * (v >> 16) + 0.587 * ((v >> 8) & 255) + 0.114 * (v & 255)) / 255;
  if (luminance > 0.25) return hex;
  return dark ? '#9db3bb' : '#40535b';
}

function round(n: number): number {
  return Math.round(n * 10) / 10;
}
