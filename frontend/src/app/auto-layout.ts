/**
 * Organisation automatique d'une vue, pour la rendre lisible sans tout redessiner.
 *
 * Principe : on garde les regroupements de la vue (zones et ce qu'elles contiennent), on les range en grille, et l'on
 * place côte à côte ce qui est relié. Chaque niveau est traité de la même façon, du plus profond au plus haut :
 * 1. les enfants d'une zone sont ordonnés par voisinage (le plus relié d'abord, puis à chaque pas celui qui a le plus
 *    de liens avec ceux déjà placés) ;
 * 2. ils sont posés en lignes, retour à la ligne quand la largeur visée est atteinte (une grille un peu plus large
 *    que haute, à la mesure d'un écran) ;
 * 3. la zone prend la taille de son contenu, avec une marge et la place de son titre.
 * Les espaces entre cartes laissent la place aux libellés et pastilles des liens. Les tailles des cartes sont
 * harmonisées ; les petites formes de dessin (cercles, losanges) gardent la leur.
 */

export interface LayoutNode { id: string; parentId: string | null; w: number; h: number; zone: boolean; element: boolean; label: string; }
export interface LayoutEdge { source: string; target: string; }
export interface Box { x: number; y: number; w: number; h: number; }

const CARD = { minW: 150, maxW: 220, minH: 56, maxH: 90 };
const GAP_CARD = { x: 70, y: 56 };
const GAP_ZONE = { x: 70, y: 70 };
const PAD = { side: 20, top: 40, bottom: 22 };
const SNAP = 10;

export function autoLayout(nodes: LayoutNode[], edges: LayoutEdge[]): Record<string, Box> {
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const children = new Map<string | null, LayoutNode[]>();
  for (const n of nodes) {
    const parent = n.parentId !== null && byId.has(n.parentId) ? n.parentId : null;
    children.set(parent, [...(children.get(parent) ?? []), n]);
  }
  const ancestors = (id: string): string[] => {
    const chain: string[] = [];
    for (let cur = byId.get(id); cur; cur = cur.parentId ? byId.get(cur.parentId) : undefined) chain.push(cur.id);
    return chain; // du nœud vers la racine
  };
  const chains = new Map(nodes.map((n) => [n.id, ancestors(n.id)]));

  /** Lien entre deux enfants directs d'un même conteneur, remonté depuis leurs descendants. */
  const lifted = (container: string | null, kids: LayoutNode[]): Map<string, Map<string, number>> => {
    const kidIds = new Set(kids.map((k) => k.id));
    const under = (id: string) => (chains.get(id) ?? []).find((a) => kidIds.has(a)) ?? null;
    const weights = new Map<string, Map<string, number>>(kids.map((k) => [k.id, new Map()]));
    for (const e of edges) {
      const a = under(e.source);
      const b = under(e.target);
      if (!a || !b || a === b) continue;
      if (container !== null && !(chains.get(e.source) ?? []).includes(container)) continue;
      weights.get(a)!.set(b, (weights.get(a)!.get(b) ?? 0) + 1);
      weights.get(b)!.set(a, (weights.get(b)!.get(a) ?? 0) + 1);
    }
    return weights;
  };

  const order = (kids: LayoutNode[], weights: Map<string, Map<string, number>>): LayoutNode[] => {
    const degree = (id: string) => [...(weights.get(id)?.values() ?? [])].reduce((s, v) => s + v, 0);
    const left = [...kids].sort((a, b) => degree(b.id) - degree(a.id) || a.id.localeCompare(b.id));
    const done: LayoutNode[] = [];
    while (left.length) {
      let best = 0;
      let bestScore = -1;
      left.forEach((k, i) => {
        const score = done.reduce((s, d) => s + (weights.get(k.id)?.get(d.id) ?? 0), 0) * 1000 + degree(k.id);
        if (score > bestScore) { bestScore = score; best = i; }
      });
      done.push(left.splice(best, 1)[0]);
    }
    return done;
  };

  const size = new Map<string, { w: number; h: number }>();
  const rel = new Map<string, { x: number; y: number }>();   // position dans le conteneur

  const arrange = (container: string | null): { w: number; h: number } => {
    const kids = children.get(container) ?? [];
    for (const k of kids) {
      if ((children.get(k.id) ?? []).length) size.set(k.id, arrange(k.id));
      else size.set(k.id, k.element || k.w >= 80 ? card(k) : { w: snap(k.w), h: snap(k.h) });
    }
    const zones = kids.some((k) => (children.get(k.id) ?? []).length);
    const gap = zones ? GAP_ZONE : GAP_CARD;
    const sorted = order(kids, lifted(container, kids));
    // Largeur visée : une grille d'environ 1,6 fois plus large que haute.
    const area = sorted.reduce((s, k) => s + (size.get(k.id)!.w + gap.x) * (size.get(k.id)!.h + gap.y), 0);
    const widest = Math.max(0, ...sorted.map((k) => size.get(k.id)!.w));
    const target = Math.max(widest, Math.sqrt(area * 1.6));
    let x = 0, y = 0, rowH = 0, maxW = 0;
    for (const k of sorted) {
      const s = size.get(k.id)!;
      if (x > 0 && x + s.w > target) { x = 0; y += rowH + gap.y; rowH = 0; }
      rel.set(k.id, { x, y });
      x += s.w + gap.x;
      rowH = Math.max(rowH, s.h);
      maxW = Math.max(maxW, x - gap.x);
    }
    const contentH = sorted.length ? y + rowH : 0;
    if (container === null) return { w: maxW, h: contentH };
    // La zone est au moins assez large pour son titre en entier.
    const title = (byId.get(container)?.label.length ?? 0) * 9.5 + 2 * PAD.side;
    return { w: snap(Math.max(maxW + 2 * PAD.side, 160, title)), h: snap(contentH + PAD.top + PAD.bottom) };
  };

  arrange(null);
  const out: Record<string, Box> = {};
  const place = (container: string | null, ox: number, oy: number) => {
    for (const k of children.get(container) ?? []) {
      const r = rel.get(k.id)!;
      const s = size.get(k.id)!;
      const x = snap(ox + r.x);
      const y = snap(oy + r.y);
      out[k.id] = { x, y, w: s.w, h: s.h };
      place(k.id, x + PAD.side, y + PAD.top);
    }
  };
  place(null, 0, 0);
  return out;
}

/** Carte harmonisée : assez large pour que son nom tienne sur une ou deux lignes, sans couper un mot. */
function card(n: LayoutNode): { w: number; h: number } {
  const longestWord = Math.max(0, ...n.label.split(/\s+/).map((w) => w.length));
  const wanted = Math.max(n.label.length * 8.5 / (n.label.length > 18 ? 2 : 1), longestWord * 10) + 56;
  return { w: snap(clamp(Math.max(wanted, CARD.minW), CARD.minW, CARD.maxW)), h: snap(clamp(n.h, CARD.minH, CARD.maxH)) };
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, v));
}

export function snap(v: number): number {
  return Math.round(v / SNAP) * SNAP;
}
