/**
 * Palier 3 de la charte d'interface (§2.1) : en rendu lisible, une case dont le nom serait tronqué grandit en hauteur
 * si la place en dessous est libre. La disposition enregistrée n'est pas modifiée : seul l'affichage change.
 *
 * Module sans dépendance, pour être testé hors navigateur.
 */
import type { Rect } from './geometry';

const contains = (outer: Rect, inner: Rect) =>
  inner.x >= outer.x && inner.y >= outer.y && inner.x + inner.w <= outer.x + outer.w && inner.y + inner.h <= outer.y + outer.h;

/** Écart gardé entre la case agrandie et ce qui se trouve dessous, ou le bord de sa zone. */
export const GROW_GAP = 6;

/**
 * Hauteur que la case peut gagner vers le bas sans toucher une autre forme ni sortir de la zone qui la contient.
 * « others » : toutes les autres formes de la vue, zones comprises. Une zone qui contient la case la borne par son bord
 * bas ; toute autre forme placée dessous et qui la chevauche en largeur l'arrête.
 */
export function roomBelow(card: Rect, others: Rect[], gap = GROW_GAP): number {
  const bottom = card.y + card.h;
  let limit = Infinity;
  for (const o of others) {
    if (o === card) continue;
    if (contains(o, card) && o.w * o.h > card.w * card.h) { limit = Math.min(limit, o.y + o.h - gap); continue; }
    const overlapsX = o.x < card.x + card.w && o.x + o.w > card.x;
    if (overlapsX && o.y >= bottom - 0.5) limit = Math.min(limit, o.y - gap);
  }
  // Sans zone ni forme dessous, la case ne grandit pas plus que sa propre hauteur : le dessin garde ses proportions.
  return Math.max(0, Math.min(limit - bottom, card.h));
}

/**
 * Plus petite hauteur, entre h et h + room, pour laquelle « fits » dit que le nom tient entier ; null s'il ne tient pas,
 * même agrandi : on garde alors la case telle quelle et le palier 4 (troncature) s'applique.
 */
export function grownHeight(h: number, room: number, fits: (height: number) => boolean, step = 2): number | null {
  for (let extra = step; extra <= room + 1e-9; extra += step) if (fits(h + extra)) return h + extra;
  return room > 0 && fits(h + room) ? h + room : null;
}
