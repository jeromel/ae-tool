/**
 * Lecture d'une étape de trajectoire (maquette Trajectoires de Camille, 27/11). Module pur, testé sans navigateur.
 * Chaque nature a un symbole et un mot : le compteur porte le mot, le changement porte le symbole (et le mot pour le
 * lecteur d'écran). Un compteur à zéro ne s'affiche pas.
 */

export interface ChangeLike { op: string; issue: string | null; }

interface Nature { symbol: string; singular: string; plural: string; spoken: string; }

const NATURES: Record<string, Nature> = {
  ajout: { symbol: '+', singular: 'ajout', plural: 'ajouts', spoken: 'ajout' },
  retrait: { symbol: '−', singular: 'retrait', plural: 'retraits', spoken: 'retrait' },
  modification: { symbol: '~', singular: 'modification', plural: 'modifications', spoken: 'modification' },
  remplacement: { symbol: '⇄', singular: 'remplacement', plural: 'remplacements', spoken: 'remplacement' },
};

/** « ajout-objet » et « ajout-lien » sont tous deux des ajouts. */
function natureOf(op: string): Nature {
  return NATURES[op.startsWith('ajout') ? 'ajout' : op] ?? NATURES['modification'];
}

export function changeSymbol(op: string): string {
  return natureOf(op).symbol;
}

/** Ce que lit le lecteur d'écran avant le libellé : « ajout : ». */
export function changeSpoken(op: string): string {
  return `${natureOf(op).spoken} :`;
}

/** Compteurs d'une étape, dans l'ordre ajouts, retraits, modifications, remplacements, puis « ⚠ N à revoir ». */
export function stepSummary(changes: ChangeLike[]): string[] {
  const parts: string[] = [];
  for (const key of ['ajout', 'retrait', 'modification', 'remplacement']) {
    const n = changes.filter((c) => natureOf(c.op) === NATURES[key]).length;
    if (n > 0) parts.push(`${NATURES[key].symbol} ${n} ${n > 1 ? NATURES[key].plural : NATURES[key].singular}`);
  }
  const review = changes.filter((c) => c.issue).length;
  if (review > 0) parts.push(`⚠ ${review} à revoir`);
  return parts;
}
