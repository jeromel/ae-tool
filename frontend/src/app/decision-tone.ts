/** Teinte d'une décision d'urbanisation : la même au portefeuille, dans le quadrant et sur la fiche d'un objet. */
export function decisionTone(decision: string): string {
  if (decision.startsWith('Maintenir')) return 'chip ok';
  if (decision.startsWith('Moderniser')) return 'chip warn';
  if (decision.startsWith('Éliminer') || decision.startsWith('À instruire')) return 'chip bad';
  if (decision.startsWith('Tolérer')) return 'chip info';
  return 'chip';
}
