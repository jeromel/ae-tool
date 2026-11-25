/**
 * Organisation automatique d'une vue, pour la rendre lisible sans tout redessiner.
 *
 * Principe : on garde les regroupements de la vue (zones et ce qu'elles contiennent), on les range en grille, et l'on
 * place côte à côte ce qui est relié. Chaque niveau est traité de la même façon, du plus profond au plus haut :
 * 1. les enfants d'une zone sont ordonnés par voisinage (le plus relié d'abord, puis à chaque pas celui qui a le plus
 *    de liens avec ceux déjà placés) ;
 * 2. quand les liens donnent une orientation lisible, ils sont posés en couches orientées (maquette « tracé des liens »,
 *    étape 3) : rang = longueur du plus long chemin entrant, cycles cassés au lien le plus faible, ordre dans un rang
 *    choisi pour limiter les croisements ; des cartes en colonnes de gauche à droite (émetteurs → relais → récepteurs),
 *    des zones en rangées de haut en bas. Sinon (aucun lien, ou trop de cycles), ils sont posés en lignes, retour à la
 *    ligne quand la largeur visée est atteinte (une grille un peu plus large que haute, à la mesure d'un écran) ;
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

  /** Liens orientés entre enfants directs d'un même conteneur : poids de a vers b. */
  const directed = (container: string | null, kids: LayoutNode[]): Map<string, Map<string, number>> => {
    const kidIds = new Set(kids.map((k) => k.id));
    const under = (id: string) => (chains.get(id) ?? []).find((a) => kidIds.has(a)) ?? null;
    const weights = new Map<string, Map<string, number>>(kids.map((k) => [k.id, new Map()]));
    for (const e of edges) {
      const a = under(e.source);
      const b = under(e.target);
      if (!a || !b || a === b) continue;
      if (container !== null && !(chains.get(e.source) ?? []).includes(container)) continue;
      weights.get(a)!.set(b, (weights.get(a)!.get(b) ?? 0) + 1);
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
    const layered = layers(sorted.map((k) => k.id), directed(container, kids));
    if (layered) {
      // Couches orientées : des zones en rangées de haut en bas, des cartes en colonnes de gauche à droite.
      const vertical = zones;
      const along = (id: string) => (vertical ? size.get(id)!.w : size.get(id)!.h);
      const across = (id: string) => (vertical ? size.get(id)!.h : size.get(id)!.w);
      const gapAlong = vertical ? gap.x : gap.y;
      const gapAcross = vertical ? gap.y : gap.x;
      const spans = layered.map((l) => l.reduce((sum, id) => sum + along(id), 0) + gapAlong * (l.length - 1));
      const widest = Math.max(...spans);
      let main = 0;
      layered.forEach((l, r) => {
        let cross = (widest - spans[r]) / 2; // chaque couche centrée sur la plus longue
        for (const id of l) {
          rel.set(id, vertical ? { x: snap(cross), y: main } : { x: main, y: snap(cross) });
          cross += along(id) + gapAlong;
        }
        main += Math.max(...l.map(across)) + gapAcross;
      });
      const w = vertical ? widest : main - gapAcross;
      const h = vertical ? main - gapAcross : widest;
      if (container === null) return { w, h };
      const title = (byId.get(container)?.label.length ?? 0) * 9.5 + 2 * PAD.side;
      return { w: snap(Math.max(w + 2 * PAD.side, 160, title)), h: snap(h + PAD.top + PAD.bottom) };
    }
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

/**
 * Couches orientées, ou null si les liens ne donnent pas d'orientation lisible (aucun lien, une seule couche, ou plus
 * d'un tiers du poids des liens à retourner pour casser les cycles).
 * 1. Ordre des éléments qui casse les cycles au plus faible (heuristique d'Eades) : on retire les puits à droite, les
 *    sources à gauche, sinon l'élément dont les liens sortants pèsent le plus ; les liens qui remontent cet ordre sont ignorés.
 * 2. Rang = longueur du plus long chemin entrant.
 * 3. Dans un rang, l'ordre suit la moyenne des positions des voisins du rang précédent puis du suivant (deux passes).
 * « ids » arrive dans l'ordre de voisinage, qui sert d'ordre de départ et départage les égalités.
 */
export function layers(ids: string[], w: Map<string, Map<string, number>>): string[][] | null {
  const weight = (a: string, b: string) => w.get(a)?.get(b) ?? 0;
  const total = ids.reduce((sum, a) => sum + ids.reduce((s2, b) => s2 + weight(a, b), 0), 0);
  if (ids.length < 3 || total === 0) return null;

  const left: string[] = [];
  const right: string[] = [];
  const rest = new Set(ids);
  const outOf = (a: string) => [...rest].reduce((s, b) => s + (b !== a ? weight(a, b) : 0), 0);
  const into = (a: string) => [...rest].reduce((s, b) => s + (b !== a ? weight(b, a) : 0), 0);
  while (rest.size) {
    let moved = true;
    while (moved) {
      moved = false;
      for (const a of ids) {
        if (!rest.has(a)) continue;
        if (outOf(a) === 0) { right.unshift(a); rest.delete(a); moved = true; }
        else if (into(a) === 0) { left.push(a); rest.delete(a); moved = true; }
      }
    }
    if (!rest.size) break;
    let best = '';
    let bestScore = -Infinity;
    for (const a of ids) {
      if (!rest.has(a)) continue;
      const score = outOf(a) - into(a);
      if (score > bestScore) { bestScore = score; best = a; }
    }
    left.push(best);
    rest.delete(best);
  }
  const seq = [...left, ...right];
  const pos = new Map(seq.map((a, i) => [a, i]));
  let back = 0;
  for (const a of ids) for (const b of ids) if (weight(a, b) && pos.get(a)! > pos.get(b)!) back += weight(a, b);
  if (back * 3 > total) return null;

  const rank = new Map<string, number>();
  for (const a of seq) {
    let r = 0;
    for (const p of seq) if (pos.get(p)! < pos.get(a)! && weight(p, a)) r = Math.max(r, rank.get(p)! + 1);
    rank.set(a, r);
  }
  const depth = Math.max(...rank.values());
  if (depth === 0) return null;
  const out: string[][] = Array.from({ length: depth + 1 }, () => []);
  for (const a of ids) out[rank.get(a)!].push(a);

  const near = (a: string, b: string) => weight(a, b) + weight(b, a);
  const sweep = (r: number, ref: string[]) => {
    const at = new Map(ref.map((b, i) => [b, i]));
    const own = new Map(out[r].map((a, i) => [a, i]));
    const centre = (a: string) => {
      let sum = 0, count = 0;
      for (const b of ref) { const k = near(a, b); if (k) { sum += at.get(b)! * k; count += k; } }
      // Sans voisin dans la couche de référence : l'élément garde sa place relative.
      return count ? sum / count : (own.get(a)! * Math.max(ref.length - 1, 1)) / Math.max(out[r].length - 1, 1);
    };
    const keys = new Map(out[r].map((a) => [a, centre(a)]));
    out[r].sort((a, b) => keys.get(a)! - keys.get(b)! || own.get(a)! - own.get(b)!);
  };
  for (let pass = 0; pass < 2; pass++) {
    for (let r = 1; r <= depth; r++) sweep(r, out[r - 1]);
    for (let r = depth - 1; r >= 0; r--) sweep(r, out[r + 1]);
  }
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
