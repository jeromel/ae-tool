import { Component, ElementRef, computed, effect, input, output, signal, viewChild } from '@angular/core';

interface Node {
  id: string; kind: string; elementId: string | null; name: string; typeLabel: string | null;
  x: number; y: number; w: number; h: number; fill: string; stroke: string; textColor: string;
  fontSize: number; bold: boolean; shape: string; icon: string | null; hasChildren: boolean; viewRefId: string | null;
}
interface Connection {
  id: string; sourceNodeId: string; targetNodeId: string; bendpoints: { x: number; y: number }[];
  lineStyle: string; color: string; name: string | null; typeKey: string | null;
}

/**
 * Affichage d'une vue en SVG : positions, dimensions, imbrication et couleurs telles qu'importées.
 * Lecture seule dans ce lot ; l'édition graphique relève du lot « modélisation ».
 */
@Component({
  selector: 'app-diagram-view',
  template: `
    <div class="tools">
      <button class="ghost" (click)="zoom.set(zoom() / 1.25)">−</button>
      <span class="muted">{{ percent() }} %</span>
      <button class="ghost" (click)="zoom.set(zoom() * 1.25)">+</button>
      <button class="ghost" (click)="zoom.set(1)">100 %</button>
      <button class="ghost" (click)="fit()">Ajuster</button>
      <ng-content />
    </div>
    <div class="canvas" #canvas>
      <svg [attr.width]="box().w * zoom()" [attr.height]="box().h * zoom()" [attr.viewBox]="box().x + ' ' + box().y + ' ' + box().w + ' ' + box().h">
        <defs>
          <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M0,0 L10,5 L0,10 z" fill="context-stroke" />
          </marker>
        </defs>
        @for (n of nodes(); track n.id) {
          <g [class.link]="n.elementId || n.viewRefId" (click)="pick(n, $event)">
            @if (n.shape === 'ellipse') {
              <ellipse [attr.cx]="n.x + n.w / 2" [attr.cy]="n.y + n.h / 2" [attr.rx]="n.w / 2" [attr.ry]="n.h / 2" [attr.fill]="n.fill" [attr.stroke]="n.stroke" />
            } @else {
              <rect [attr.x]="n.x" [attr.y]="n.y" [attr.width]="n.w" [attr.height]="n.h" [attr.rx]="n.shape === 'arrondi' ? 10 : 0"
                [attr.fill]="n.fill" [attr.stroke]="n.kind === 'Label' ? 'none' : n.stroke" />
            }
            <foreignObject [attr.x]="n.x" [attr.y]="n.y" [attr.width]="n.w" [attr.height]="n.hasChildren ? minOf(n.h, n.fontSize * 4) : n.h">
              <div class="label" [class.top]="n.hasChildren" [style.color]="n.textColor" [style.font-size.px]="n.fontSize * 1.33"
                [style.font-weight]="n.bold ? 700 : 400" [title]="n.typeLabel ? n.typeLabel + ' : ' + n.name : n.name">
                {{ n.name }}
              </div>
            </foreignObject>
          </g>
        }
        @for (c of lines(); track c.id) {
          <polyline [attr.points]="c.points" fill="none" [attr.stroke]="c.color" stroke-width="1.2"
            [attr.stroke-dasharray]="c.dash" marker-end="url(#arrow)"><title>{{ c.title }}</title></polyline>
        }
      </svg>
    </div>
  `,
  styles: `
    g.link { cursor: pointer; }
    g.link:hover rect, g.link:hover ellipse { stroke-width: 2.5; }
    .label { height: 100%; display: flex; align-items: center; justify-content: center; text-align: center; padding: 2px 6px; line-height: 1.15; overflow: hidden; font-family: 'Segoe UI', system-ui, sans-serif; }
    .label.top { align-items: flex-start; }
  `,
})
export class DiagramView {
  diagram = input.required<any>();
  openObject = output<string>();
  openView = output<string>();
  zoom = signal(1);
  percent = computed(() => Math.round(this.zoom() * 100));

  private canvas = viewChild<ElementRef<HTMLElement>>('canvas');

  /** Un nœud qui en contient d'autres (imbriqués ou simplement posés dessus) affiche son libellé en haut. */
  nodes = computed<Node[]>(() => {
    const all = this.diagram().nodes as Node[];
    return all.map((n) => ({
      ...n,
      hasChildren:
        n.hasChildren ||
        all.some((o) => o !== n && o.x >= n.x && o.y >= n.y && o.x + o.w <= n.x + n.w && o.y + o.h <= n.y + n.h && o.w * o.h < n.w * n.h),
    }));
  });

  constructor() {
    // À l'ouverture d'une vue, on l'ajuste à la largeur disponible sans jamais l'agrandir.
    effect(() => {
      this.diagram();
      this.fit();
    });
  }

  fit(): void {
    const width = this.canvas()?.nativeElement.clientWidth ?? 900;
    this.zoom.set(Math.min(1, (width - 4) / this.box().w));
  }

  box = computed(() => {
    const ns = this.nodes();
    if (ns.length === 0) return { x: 0, y: 0, w: 400, h: 200 };
    const x = Math.min(...ns.map((n) => n.x)) - 20;
    const y = Math.min(...ns.map((n) => n.y)) - 20;
    return { x, y, w: Math.max(...ns.map((n) => n.x + n.w)) - x + 20, h: Math.max(...ns.map((n) => n.y + n.h)) - y + 20 };
  });

  lines = computed(() => {
    const byId = new Map(this.nodes().map((n) => [n.id, n]));
    const result: { id: string; points: string; color: string; dash: string | null; title: string }[] = [];
    for (const c of this.diagram().connections as Connection[]) {
      const s = byId.get(c.sourceNodeId);
      const t = byId.get(c.targetNodeId);
      if (!s || !t) continue; // extrémité portée par une autre connexion : non dessinée
      const centre = (n: Node) => ({ x: n.x + n.w / 2, y: n.y + n.h / 2 });
      const via = c.bendpoints;
      const start = this.border(s, via[0] ?? centre(t));
      const end = this.border(t, via[via.length - 1] ?? centre(s));
      result.push({
        id: c.id,
        points: [start, ...via, end].map((p) => `${p.x},${p.y}`).join(' '),
        color: c.color,
        dash: c.lineStyle === 'tirets' ? '6 4' : c.lineStyle === 'pointillés' ? '2 3' : null,
        title: [c.typeKey, c.name].filter(Boolean).join(' : '),
      });
    }
    return result;
  });

  minOf(a: number, b: number): number {
    return Math.min(a, b);
  }

  /** Point du bord du nœud sur le segment qui relie son centre au point visé. */
  private border(n: Node, toward: { x: number; y: number }): { x: number; y: number } {
    const cx = n.x + n.w / 2;
    const cy = n.y + n.h / 2;
    const dx = toward.x - cx;
    const dy = toward.y - cy;
    if (dx === 0 && dy === 0) return { x: cx, y: cy };
    const k = Math.min(dx === 0 ? Infinity : n.w / 2 / Math.abs(dx), dy === 0 ? Infinity : n.h / 2 / Math.abs(dy));
    return { x: Math.round(cx + dx * Math.min(k, 1)), y: Math.round(cy + dy * Math.min(k, 1)) };
  }

  pick(n: Node, event: Event): void {
    event.stopPropagation();
    if (n.elementId) this.openObject.emit(n.elementId);
    else if (n.viewRefId) this.openView.emit(n.viewRefId);
  }
}
