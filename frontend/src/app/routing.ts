/**
 * Tracé orthogonal des liens dans les couloirs (maquette « tracé des liens », étape 2). Fonctions pures, sans affichage.
 *
 * Règle : un lien ne passe jamais sur une boîte qui n'est pas l'une de ses extrémités.
 * 1. Les boîtes (cartes, ronds, notes ; pas les zones) sont des obstacles.
 * 2. Une grille irrégulière est tirée de leurs bords grossis, du milieu des couloirs assez larges et des extrémités des liens :
 *    les vues d'Archi ont des positions libres, les couloirs se calculent à partir des obstacles.
 * 3. Chaque lien cherche le plus court chemin sur cette grille (A*), chaque coude coûtant comme un détour : peu de coudes,
 *    segments horizontaux et verticaux, croisements à angle droit.
 * 4. Les liens qui empruntent le même couloir sont écartés de 6 px les uns des autres pour rester distincts.
 * Quand aucun chemin n'existe (lien enfermé), le lien prend un coude simple : il ne disparaît jamais.
 */
import type { Point, Rect } from './geometry';

export interface RouteEnd {
  /** Point d'attache, sur le bord de la forme. */
  at: Point;
  /** Direction de départ depuis ce point (vecteur unitaire horizontal ou vertical). */
  dir: Point;
}
export interface RouteRequest { id: string; from: RouteEnd; to: RouteEnd; }

/** Distance à laquelle un lien longe une carte. */
export const MARGIN = 9;
/** Zone interdite autour d'une boîte : plus étroite que la marge, pour qu'un lien passe entre deux cartes serrées. */
const SAFE = 2;
/** Écart entre deux liens d'un même couloir. */
export const SPREAD = 6;
/** Coût d'un coude, en unités de longueur. */
const BEND = 28;
/** Au-delà, la recherche abandonne et le lien prend un coude simple. */
const MAX_EXPANSIONS = 120_000;

const DIRS: Point[] = [{ x: 1, y: 0 }, { x: -1, y: 0 }, { x: 0, y: 1 }, { x: 0, y: -1 }];

/** Trace tous les liens d'une vue ; renvoie, pour chaque lien, sa ligne brisée du départ à l'arrivée. */
export function routeAll(requests: RouteRequest[], boxes: Rect[]): Map<string, Point[]> {
  const out = new Map<string, Point[]>();
  if (requests.length === 0) return out;
  // Points d'attache calés sur la grille (demi-pixel) : le premier et le dernier segment restent droits.
  const snapped = requests.map((r) => ({ ...r, from: { ...r.from, at: half(r.from.at) }, to: { ...r.to, at: half(r.to.at) } }));
  const grid = buildGrid(snapped, boxes);
  for (const r of snapped) {
    const points = route(grid, r) ?? elbowBetween(r);
    out.set(r.id, simplify(points));
  }
  spread([...out.values()], boxes);
  return out;
}

function half(p: Point): Point {
  return { x: Math.round(p.x * 2) / 2, y: Math.round(p.y * 2) / 2 };
}

// ------------------------------------------------------------------ grille

interface Grid {
  xs: number[]; ys: number[]; nx: number; ny: number;
  /** Point bloqué (strictement dans un obstacle). */
  node: Uint8Array;
  /** Segment vers la droite / vers le bas bloqué. */
  right: Uint8Array; down: Uint8Array;
  // Tampons de recherche, réutilisés d'un lien à l'autre.
  g: Float64Array; parent: Int32Array; stamp: Int32Array; closed: Int32Array; round: number;
}

function stub(e: RouteEnd): Point {
  return { x: e.at.x + e.dir.x * MARGIN, y: e.at.y + e.dir.y * MARGIN };
}

function buildGrid(requests: RouteRequest[], boxes: Rect[]): Grid {
  const inflated = boxes.map((b) => ({ l: b.x - SAFE, r: b.x + b.w + SAFE, t: b.y - SAFE, b: b.y + b.h + SAFE }));
  const xs = new Set<number>();
  const ys = new Set<number>();
  const add = (set: Set<number>, v: number) => set.add(Math.round(v * 2) / 2);
  for (const b of boxes) { add(xs, b.x - MARGIN); add(xs, b.x + b.w + MARGIN); add(ys, b.y - MARGIN); add(ys, b.y + b.h + MARGIN); }
  // Milieu des couloirs assez larges : un lien y passe à égale distance des deux rangées de cartes.
  const middles = (set: Set<number>) => {
    const sorted = [...set].sort((a, b) => a - b);
    for (let i = 1; i < sorted.length; i++) if (sorted[i] - sorted[i - 1] > 3 * MARGIN) add(set, (sorted[i] + sorted[i - 1]) / 2);
  };
  middles(xs);
  middles(ys);
  for (const r of requests) {
    for (const e of [r.from, r.to]) {
      const s = stub(e);
      add(xs, s.x); add(ys, s.y);
    }
  }
  // Un cadre autour de tout : il y a toujours un chemin par l'extérieur.
  const allX = [...xs], allY = [...ys];
  add(xs, Math.min(...allX) - 4 * MARGIN); add(xs, Math.max(...allX) + 4 * MARGIN);
  add(ys, Math.min(...allY) - 4 * MARGIN); add(ys, Math.max(...allY) + 4 * MARGIN);

  const X = [...xs].sort((a, b) => a - b);
  const Y = [...ys].sort((a, b) => a - b);
  const nx = X.length, ny = Y.length;
  const node = new Uint8Array(nx * ny);
  const right = new Uint8Array(nx * ny);
  const down = new Uint8Array(nx * ny);
  for (const o of inflated) {
    // Lignes et colonnes strictement à l'intérieur de l'obstacle grossi.
    const i0 = upper(X, o.l), i1 = lower(X, o.r) - 1;
    const j0 = upper(Y, o.t), j1 = lower(Y, o.b) - 1;
    for (let j = j0; j <= j1; j++) {
      // Segments horizontaux de la rangée j qui entrent dans l'obstacle : xs[i] < r et xs[i+1] > l.
      for (let i = Math.max(0, i0 - 1); i <= Math.min(nx - 2, i1); i++) right[j * nx + i] = 1;
      for (let i = i0; i <= i1; i++) node[j * nx + i] = 1;
    }
    for (let i = i0; i <= i1; i++) {
      for (let j = Math.max(0, j0 - 1); j <= Math.min(ny - 2, j1); j++) down[j * nx + i] = 1;
    }
  }
  const states = nx * ny * 4;
  return {
    xs: X, ys: Y, nx, ny, node, right, down,
    g: new Float64Array(states), parent: new Int32Array(states), stamp: new Int32Array(states), closed: new Int32Array(states), round: 0,
  };
}

/** Premier indice dont la valeur est strictement supérieure à v. */
function upper(a: number[], v: number): number {
  let lo = 0, hi = a.length;
  while (lo < hi) { const m = (lo + hi) >> 1; if (a[m] <= v) lo = m + 1; else hi = m; }
  return lo;
}
/** Premier indice dont la valeur est supérieure ou égale à v. */
function lower(a: number[], v: number): number {
  let lo = 0, hi = a.length;
  while (lo < hi) { const m = (lo + hi) >> 1; if (a[m] < v) lo = m + 1; else hi = m; }
  return lo;
}

// ------------------------------------------------------------------ recherche

function dirIndex(d: Point): number {
  return d.x > 0 ? 0 : d.x < 0 ? 1 : d.y > 0 ? 2 : 3;
}

function route(grid: Grid, r: RouteRequest): Point[] | null {
  const { xs, ys, nx, ny } = grid;
  const a = stub(r.from), b = stub(r.to);
  const ai = lower(xs, Math.round(a.x * 2) / 2), aj = lower(ys, Math.round(a.y * 2) / 2);
  const bi = lower(xs, Math.round(b.x * 2) / 2), bj = lower(ys, Math.round(b.y * 2) / 2);
  if (ai >= nx || aj >= ny || bi >= nx || bj >= ny) return null;
  const start = aj * nx + ai, goal = bj * nx + bi;
  // On arrive au point d'attache en remontant la direction de départ de l'arrivée.
  const arrive = dirIndex({ x: -r.to.dir.x, y: -r.to.dir.y });
  const round = ++grid.round;
  const heap = new Heap();
  const h = (n: number) => Math.abs(xs[n % nx] - xs[bi]) + Math.abs(ys[(n / nx) | 0] - ys[bj]);
  const s0 = start * 4 + dirIndex(r.from.dir);
  grid.stamp[s0] = round; grid.g[s0] = 0; grid.parent[s0] = -1;
  heap.push(s0, h(start));
  let expansions = 0;
  while (heap.size > 0) {
    const s = heap.pop();
    if (grid.closed[s] === round) continue;
    grid.closed[s] = round;
    const n = s >> 2, d = s & 3;
    if (n === goal) return trace(grid, s, r);
    if (++expansions > MAX_EXPANSIONS) return null;
    const i = n % nx, j = (n / nx) | 0;
    for (let nd = 0; nd < 4; nd++) {
      if (nd === (d ^ 1)) continue; // pas de demi-tour
      let ni = i, nj = j, blocked: number;
      if (nd === 0) { ni = i + 1; blocked = ni < nx ? grid.right[j * nx + i] : 1; }
      else if (nd === 1) { ni = i - 1; blocked = ni >= 0 ? grid.right[j * nx + ni] : 1; }
      else if (nd === 2) { nj = j + 1; blocked = nj < ny ? grid.down[j * nx + i] : 1; }
      else { nj = j - 1; blocked = nj >= 0 ? grid.down[nj * nx + i] : 1; }
      if (blocked) continue;
      const m = nj * nx + ni;
      if (grid.node[m] && m !== goal) continue;
      let cost = grid.g[s] + Math.abs(xs[ni] - xs[i]) + Math.abs(ys[nj] - ys[j]) + (nd === d ? 0 : BEND);
      if (m === goal && nd !== arrive) cost += BEND;
      const t = m * 4 + nd;
      if (grid.stamp[t] === round && grid.g[t] <= cost) continue;
      grid.stamp[t] = round; grid.g[t] = cost; grid.parent[t] = s;
      heap.push(t, cost + h(m));
    }
  }
  return null;
}

function trace(grid: Grid, end: number, r: RouteRequest): Point[] {
  const pts: Point[] = [];
  for (let s = end; s !== -1; s = grid.parent[s]) {
    const n = s >> 2;
    pts.push({ x: grid.xs[n % grid.nx], y: grid.ys[(n / grid.nx) | 0] });
  }
  pts.reverse();
  return [r.from.at, ...pts, r.to.at];
}

/** Coude simple, quand aucun chemin n'existe : il ne contourne rien, mais le lien reste visible. */
function elbowBetween(r: RouteRequest): Point[] {
  const a = stub(r.from), b = stub(r.to);
  const horizontal = r.from.dir.x !== 0;
  const mid = horizontal ? { x: (a.x + b.x) / 2, y: 0 } : { x: 0, y: (a.y + b.y) / 2 };
  const inner = horizontal ? [{ x: mid.x, y: a.y }, { x: mid.x, y: b.y }] : [{ x: a.x, y: mid.y }, { x: b.x, y: mid.y }];
  return [r.from.at, a, ...inner, b, r.to.at];
}

/** Retire les points confondus et ceux qui sont alignés avec leurs voisins. */
export function simplify(points: Point[]): Point[] {
  const out: Point[] = [];
  for (const p of points) {
    const last = out[out.length - 1];
    if (last && Math.abs(last.x - p.x) < 0.01 && Math.abs(last.y - p.y) < 0.01) continue;
    out.push({ x: p.x, y: p.y });
    while (out.length >= 3) {
      const [a, b, c] = out.slice(-3);
      const aligned = (Math.abs(a.x - b.x) < 0.01 && Math.abs(b.x - c.x) < 0.01) || (Math.abs(a.y - b.y) < 0.01 && Math.abs(b.y - c.y) < 0.01);
      if (!aligned) break;
      out.splice(out.length - 2, 1);
    }
  }
  return out;
}

// ------------------------------------------------------------------ écartement

/**
 * Écarte les liens qui partagent un couloir : les segments intérieurs (ni le premier ni le dernier, attachés aux formes)
 * portés par la même ligne et qui se recouvrent sont décalés de 6 px les uns des autres, de part et d'autre de la ligne.
 * Un décalage qui amènerait le lien sur une carte (autre que ses extrémités) n'est pas appliqué : la règle d'or l'emporte.
 */
export function spread(routes: Point[][], boxes: Rect[]): void {
  type Seg = { pts: Point[]; k: number; lo: number; hi: number; order: number };
  const groups = new Map<string, Seg[]>();
  for (const pts of routes) {
    for (let k = 1; k < pts.length - 2; k++) {
      const a = pts[k], b = pts[k + 1];
      const horizontal = Math.abs(a.y - b.y) < 0.01;
      const key = (horizontal ? 'h' : 'v') + Math.round(horizontal ? a.y : a.x);
      const lo = horizontal ? Math.min(a.x, b.x) : Math.min(a.y, b.y);
      const hi = horizontal ? Math.max(a.x, b.x) : Math.max(a.y, b.y);
      // Ordre dans le couloir : d'où vient le lien, pour limiter les croisements entre voisins.
      const prev = pts[k - 1];
      const order = horizontal ? prev.y : prev.x;
      groups.set(key, [...(groups.get(key) ?? []), { pts, k, lo, hi, order }]);
    }
  }
  for (const [key, segs] of groups) {
    if (segs.length < 2) continue;
    const horizontal = key[0] === 'h';
    segs.sort((a, b) => a.lo - b.lo);
    // Grappes de segments qui se recouvrent.
    let cluster: Seg[] = [];
    let end = -Infinity;
    const flush = () => {
      if (cluster.length > 1) {
        cluster.sort((a, b) => a.order - b.order);
        cluster.forEach((s, n) => {
          // Au-delà de cinq liens, le couloir ne s'élargit plus : les suivants se superposent plutôt que toucher les cartes.
          const off = Math.max(-2 * SPREAD, Math.min(2 * SPREAD, (n - (cluster.length - 1) / 2) * SPREAD));
          shift(s.pts, s.k, horizontal, off);
          if (crossesBox(s.pts, s.k, boxes)) shift(s.pts, s.k, horizontal, -off);
        });
      }
      cluster = [];
    };
    for (const s of segs) {
      if (s.lo >= end - 0.01) { flush(); end = -Infinity; }
      cluster.push(s);
      end = Math.max(end, s.hi);
    }
    flush();
  }
}

function shift(pts: Point[], k: number, horizontal: boolean, off: number): void {
  if (horizontal) { pts[k].y += off; pts[k + 1].y += off; } else { pts[k].x += off; pts[k + 1].x += off; }
}

/** Vrai si le segment k ou l'un de ses deux voisins passe sur une boîte qui n'est pas une extrémité du lien. */
function crossesBox(pts: Point[], k: number, boxes: Rect[]): boolean {
  const first = pts[0], last = pts[pts.length - 1];
  const touches = (b: Rect, p: Point) => p.x >= b.x - 0.5 && p.x <= b.x + b.w + 0.5 && p.y >= b.y - 0.5 && p.y <= b.y + b.h + 0.5;
  for (let i = Math.max(0, k - 1); i <= Math.min(pts.length - 2, k + 1); i++) {
    const p = pts[i], q = pts[i + 1];
    const l = Math.min(p.x, q.x), r = Math.max(p.x, q.x), t = Math.min(p.y, q.y), b = Math.max(p.y, q.y);
    for (const box of boxes) {
      if (touches(box, first) || touches(box, last)) continue;
      if (l < box.x + box.w && r > box.x && t < box.y + box.h && b > box.y) return true;
    }
  }
  return false;
}

// ------------------------------------------------------------------ tas binaire

class Heap {
  private keys: number[] = [];
  private prio: number[] = [];
  get size(): number { return this.keys.length; }
  push(key: number, p: number): void {
    const k = this.keys, q = this.prio;
    let i = k.length;
    k.push(key); q.push(p);
    while (i > 0) {
      const up = (i - 1) >> 1;
      if (q[up] <= p) break;
      k[i] = k[up]; q[i] = q[up]; i = up;
    }
    k[i] = key; q[i] = p;
  }
  pop(): number {
    const k = this.keys, q = this.prio;
    const top = k[0];
    const lastK = k.pop()!, lastP = q.pop()!;
    if (k.length > 0) {
      let i = 0;
      for (;;) {
        let c = 2 * i + 1;
        if (c >= k.length) break;
        if (c + 1 < k.length && q[c + 1] < q[c]) c++;
        if (q[c] >= lastP) break;
        k[i] = k[c]; q[i] = q[c]; i = c;
      }
      k[i] = lastK; q[i] = lastP;
    }
    return top;
  }
}
