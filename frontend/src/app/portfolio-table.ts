/**
 * Tableau du portefeuille (maquette portefeuille de Camille, 27/11). Module pur, testé sans navigateur.
 * - Décision : la retenue, sinon la calculée marquée « (calculée) » ; « calculée : X » sous une retenue qui diffère ;
 *   « il manque : … » sous un calcul incomplet.
 * - Contrôle : un symbole **et** un mot, jamais la couleur seule.
 * - Tri : par décision ou par priorité ; par défaut, priorité haute d'abord, puis par nom.
 */

export interface PortfolioRow {
  name: string;
  retainedDecision?: string | null;
  result: { computedDecision?: string | null; priority?: string | null; control?: string | null; missing?: string[]; retired?: boolean };
}

export interface DecisionCell { text: string | null; computed: boolean; note: string | null; }

export function decisionCell(r: PortfolioRow): DecisionCell {
  const computed = r.result.computedDecision ?? null;
  const missing = r.result.missing ?? [];
  const note = r.result.control === 'Calcul incomplet' && missing.length
    ? `il manque : ${missing.join(', ')}`
    : r.retainedDecision && computed && computed !== r.retainedDecision ? `calculée : ${computed}` : null;
  if (r.retainedDecision) return { text: r.retainedDecision, computed: false, note };
  return { text: computed, computed: computed !== null, note };
}

export interface ControlCell { symbol: string; word: string; tone: 'ok' | 'warn' | ''; }

export function controlCell(control: string | null | undefined): ControlCell {
  switch (control) {
    case 'Conforme au calcul': return { symbol: '✓', word: 'conforme', tone: 'ok' };
    case 'Calcul incomplet': return { symbol: '⚠', word: 'calcul incomplet', tone: 'warn' };
    case 'Justification requise': return { symbol: '⚠', word: 'retenue ≠ calculée, justification requise', tone: 'warn' };
    case 'Écart justifié, à revoir en comité': return { symbol: '⚠', word: 'retenue ≠ calculée, justifiée', tone: 'warn' };
    case 'Sans objet': case null: case undefined: case '': return { symbol: '', word: '—', tone: '' };
    default: return { symbol: '⚠', word: control.toLowerCase(), tone: 'warn' };
  }
}

export type SortKey = 'defaut' | 'decision' | 'priorite' | 'nom';

const PRIORITY: Record<string, number> = { Haute: 0, Moyenne: 1, Basse: 2 };
const byName = (a: PortfolioRow, b: PortfolioRow) => a.name.localeCompare(b.name, 'fr');
const priority = (r: PortfolioRow) => PRIORITY[r.result.priority ?? ''] ?? 3;
/** Ordre des décisions ; une ligne sans décision va à la fin, quel que soit le sens. */
const byDecision = (a: PortfolioRow, b: PortfolioRow, sign: number) => {
  const x = decisionCell(a).text, y = decisionCell(b).text;
  return x === y ? 0 : x === null ? 1 : y === null ? -1 : sign * x.localeCompare(y, 'fr');
};

export function sortRows<T extends PortfolioRow>(rows: T[], key: SortKey, descending = false): T[] {
  const sign = descending ? -1 : 1;
  const compare: (a: T, b: T) => number =
    key === 'decision' ? (a, b) => byDecision(a, b, sign) || byName(a, b)
    : key === 'nom' ? (a, b) => sign * byName(a, b)
    : (a, b) => sign * (priority(a) - priority(b)) || byName(a, b);
  return [...rows].sort(compare);
}
