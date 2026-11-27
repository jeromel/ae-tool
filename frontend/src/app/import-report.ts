/**
 * Compte rendu d'import Archi : les valeurs saisies ici qu'Archi a remplacées ou effacées (décisions 014 et 015), en
 * tableau groupé par objet (revue de Camille, réponses aux livraisons 0.15.15-0.15.21, §6). Module sans dépendance,
 * testé hors navigateur.
 */
export interface Overwrite { objectId: string; objectName: string; property: string; label: string; before: string; after: string | null; }
export interface OverwriteGroup { objectId: string; objectName: string; lines: Overwrite[]; }

/** Titre accordé au nombre : jamais « valeur(s) remplacée(s) ». */
export function overwritesTitle(count: number): string {
  return count === 1 ? '1 valeur saisie dans l\'outil remplacée par Archi' : `${count} valeurs saisies dans l'outil remplacées par Archi`;
}

/** Lignes groupées par objet, dans l'ordre où l'import les a trouvées : le nom de l'objet une seule fois. */
export function groupOverwrites(list: Overwrite[]): OverwriteGroup[] {
  const groups = new Map<string, OverwriteGroup>();
  for (const o of list) {
    if (!groups.has(o.objectId)) groups.set(o.objectId, { objectId: o.objectId, objectName: o.objectName, lines: [] });
    groups.get(o.objectId)!.lines.push(o);
  }
  return [...groups.values()];
}
