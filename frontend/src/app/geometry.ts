/** Géométrie du tracé des liens : fonctions pures, sans dépendance à l'affichage. */

export interface Point { x: number; y: number; }
export interface Rect { x: number; y: number; w: number; h: number; }
/** Côté d'une forme : nord, est, sud, ouest. */
export type Side = 'N' | 'E' | 'S' | 'W';

const NORMAL: Record<Side, Point> = { N: { x: 0, y: -1 }, E: { x: 1, y: 0 }, S: { x: 0, y: 1 }, W: { x: -1, y: 0 } };

/** Vrai si « outer » contient entièrement « inner ». */
export function contains(outer: Rect, inner: Rect): boolean {
  return inner.x >= outer.x && inner.y >= outer.y && inner.x + inner.w <= outer.x + outer.w && inner.y + inner.h <= outer.y + outer.h;
}

/**
 * Côtés par lesquels un lien quitte la source et atteint la cible.
 * Formes séparées : les côtés qui se font face, selon l'axe où l'écart est le plus grand.
 * Forme contenue dans l'autre : le côté de la forme intérieure le plus proche du bord de l'extérieure, pour les deux.
 */
export function facingSides(s: Rect, t: Rect): [Side, Side] {
  const sInT = contains(t, s);
  if (sInT || contains(s, t)) {
    const inner = sInT ? s : t;
    const outer = sInT ? t : s;
    const gaps: [Side, number][] = [
      ['W', inner.x - outer.x], ['E', outer.x + outer.w - inner.x - inner.w],
      ['N', inner.y - outer.y], ['S', outer.y + outer.h - inner.y - inner.h],
    ];
    const side = gaps.sort((a, b) => a[1] - b[1])[0][0];
    return [side, side];
  }
  const gapX = Math.max(t.x - (s.x + s.w), s.x - (t.x + t.w));
  const gapY = Math.max(t.y - (s.y + s.h), s.y - (t.y + t.h));
  if (gapX >= gapY) return t.x + t.w / 2 >= s.x + s.w / 2 ? ['E', 'W'] : ['W', 'E'];
  return t.y + t.h / 2 >= s.y + s.h / 2 ? ['S', 'N'] : ['N', 'S'];
}

/** Position du lien numéro « index » parmi « count » sur un côté : répartis sur les 70 % centraux. */
export function portPoint(r: Rect, side: Side, index: number, count: number): Point {
  const t = 0.15 + (0.7 * (index + 0.5)) / Math.max(count, 1);
  switch (side) {
    case 'N': return { x: r.x + r.w * t, y: r.y };
    case 'S': return { x: r.x + r.w * t, y: r.y + r.h };
    case 'W': return { x: r.x, y: r.y + r.h * t };
    default: return { x: r.x + r.w, y: r.y + r.h * t };
  }
}

/** Points de contrôle d'une courbe qui quitte p et atteint q perpendiculairement à leurs côtés. */
function controls(p: Point, a: Side, q: Point, b: Side, nested: boolean): [Point, Point] {
  const reach = Math.max(18, Math.min(170, Math.hypot(q.x - p.x, q.y - p.y) * 0.45));
  if (nested) {
    // De l'intérieur vers le bord de la forme englobante : les deux extrémités regardent du même côté.
    const third = { x: (q.x - p.x) / 3, y: (q.y - p.y) / 3 };
    return [{ x: p.x + third.x, y: p.y + third.y }, { x: q.x - third.x, y: q.y - third.y }];
  }
  return [
    { x: p.x + NORMAL[a].x * reach, y: p.y + NORMAL[a].y * reach },
    { x: q.x + NORMAL[b].x * reach, y: q.y + NORMAL[b].y * reach },
  ];
}

export function bezier(p: Point, a: Side, q: Point, b: Side, nested = false): string {
  const [c1, c2] = controls(p, a, q, b, nested);
  return `M${r(p.x)},${r(p.y)} C${r(c1.x)},${r(c1.y)} ${r(c2.x)},${r(c2.y)} ${r(q.x)},${r(q.y)}`;
}

/** Milieu de la courbe (paramètre 0,5) : c'est là que se pose la pastille d'un flux. */
export function bezierMiddle(p: Point, a: Side, q: Point, b: Side, nested = false): Point {
  const [c1, c2] = controls(p, a, q, b, nested);
  return { x: (p.x + 3 * c1.x + 3 * c2.x + q.x) / 8, y: (p.y + 3 * c1.y + 3 * c2.y + q.y) / 8 };
}

/** Tracé passant par les points, coudes arrondis au rayon donné (réduit si un segment est trop court). */
export function roundedPath(points: Point[], radius: number): string {
  let d = `M${points[0].x},${points[0].y}`;
  for (let i = 1; i < points.length - 1; i++) {
    const a = points[i - 1];
    const p = points[i];
    const b = points[i + 1];
    const la = Math.hypot(p.x - a.x, p.y - a.y);
    const lb = Math.hypot(b.x - p.x, b.y - p.y);
    const rad = Math.min(radius, la / 2, lb / 2);
    if (rad < 1) {
      d += ` L${p.x},${p.y}`;
      continue;
    }
    const before = { x: p.x - ((p.x - a.x) * rad) / la, y: p.y - ((p.y - a.y) * rad) / la };
    const after = { x: p.x + ((b.x - p.x) * rad) / lb, y: p.y + ((b.y - p.y) * rad) / lb };
    d += ` L${r(before.x)},${r(before.y)} Q${p.x},${p.y} ${r(after.x)},${r(after.y)}`;
  }
  const last = points[points.length - 1];
  return `${d} L${last.x},${last.y}`;
}

/** Milieu d'une ligne brisée, mesuré le long de ses segments. */
export function polylineMiddle(points: Point[]): Point {
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

function r(n: number): number {
  return Math.round(n * 10) / 10;
}
