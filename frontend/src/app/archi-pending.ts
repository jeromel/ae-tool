/**
 * Textes de la section « Pas encore dans Archi » (export B, maquette de Camille, réponses du 27/11 §4). Module pur, testé
 * sans navigateur : les nombres et les accords se lisent ici.
 */

export interface ArchiPendingSummary {
  lastImport: string | null;
  neverImported: boolean;
  objects: unknown[];
  relations: unknown[];
  excludedProposed: number;
  excludedDrafts: number;
  excludedRelations: number;
  citedObjects: number;
  viewsHere: number;
}

export type PendingState = 'chargement' | 'erreur' | 'jamais' | 'vide' | 'liste';

export function pendingState(summary: ArchiPendingSummary | null, error: string): PendingState {
  if (error) return 'erreur';
  if (!summary) return 'chargement';
  if (summary.neverImported) return 'jamais';
  return summary.objects.length + summary.relations.length === 0 ? 'vide' : 'liste';
}

/** « 1 objet », « 12 objets » : jamais « objet(s) ». */
export function count(n: number, singular: string, plural = singular + 's'): string {
  return `${n} ${n > 1 ? plural : singular}`;
}

/** Récapitulatif toujours affiché : « 12 objets · 18 relations ». */
export function pendingTotals(s: ArchiPendingSummary): string {
  return `${count(s.objects.length, 'objet')} · ${count(s.relations.length, 'relation')}`;
}

/** « N objets proposés ou brouillons ne sont pas inclus, ni les M relations qui les touchent. » (ratifié, PO 27/11). */
export function exclusionLine(s: ArchiPendingSummary): string | null {
  const n = s.excludedProposed + s.excludedDrafts;
  const m = s.excludedRelations;
  if (n === 0 && m === 0) return null;
  // Sans objet exclu, une relation attend parce qu'elle touche un objet archivé ou une relation qui attend elle-même.
  if (n === 0) return m > 1 ? `${m} relations ne sont pas incluses : elles touchent un objet qui ne part pas.` : '1 relation n\'est pas incluse : elle touche un objet qui ne part pas.';
  const objects = n > 1 ? `${n} objets proposés ou brouillons ne sont pas inclus` : `1 objet proposé ou brouillon n'est pas inclus`;
  if (m === 0) return `${objects}.`;
  return `${objects}, ni ${m > 1 ? `les ${m} relations qui les touchent` : n > 1 ? 'la relation qui les touche' : 'la relation qui le touche'}.`;
}

/** Objets d'Archi cités par une relation : une information, plus un avertissement (essai du PO du 27/11, addendum Q2). */
export function citedLine(s: ArchiPendingSummary): string | null {
  const n = s.citedObjects;
  if (n === 0) return null;
  return n > 1
    ? `${n} objets d'Archi sont cités par leurs relations : nom seulement, leurs valeurs ne sont pas touchées.`
    : `1 objet d'Archi est cité par ses relations : nom seulement, ses valeurs ne sont pas touchées.`;
}

/** Ligne des vues (réponses de Camille §4), seulement s'il existe des vues créées ici. */
export function viewsLine(s: ArchiPendingSummary): string | null {
  return s.viewsHere > 0
    ? 'Les vues dessinées ici ne partent pas : Archi reste maître de ses vues. Les objets et les relations qu\'elles contiennent partent.'
    : null;
}
