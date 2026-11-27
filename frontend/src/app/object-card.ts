/**
 * Propriétés de la fiche d'un objet (maquette carte objet §6, addendum du 27/11). Module pur, testé sans navigateur.
 * - O1 : jamais une clé. Une propriété sans libellé dans le métamodèle n'est montrée qu'à l'administrateur, avec la
 *   mention « sans libellé ». Les doublons (même libellé, même valeur) sont fusionnés.
 * - Ordre : 6 propriétés de premier plan (les 6 premières renseignées, tant que le métamodèle ne les désigne pas par
 *   type), puis les autres dans une section repliée.
 */

export interface RawProperty { key: string; label: string; labeled?: boolean; value: string | null; }
export interface ShownProperty { key: string; label: string; value: string; unlabeled: boolean; }

export const FRONT_COUNT = 6;

/** `reserved` : lignes de premier plan déjà prises par la fiche (le responsable), retirées des 6. */
export function cardProperties(props: RawProperty[], admin: boolean, reserved = 0): { front: ShownProperty[]; rest: ShownProperty[] } {
  const seen = new Set<string>();
  const shown: ShownProperty[] = [];
  for (const p of props) {
    const value = (p.value ?? '').trim();
    if (!value) continue;
    const unlabeled = p.labeled === false;
    if (unlabeled && !admin) continue;
    const label = unlabeled ? p.key : p.label;
    const id = `${label}\u0000${value}`;
    if (seen.has(id)) continue;
    seen.add(id);
    shown.push({ key: p.key, label, value, unlabeled });
  }
  // Les propriétés sans libellé (vues par l'administrateur seulement) passent après les autres : le premier plan reste lisible.
  shown.sort((a, b) => Number(a.unlabeled) - Number(b.unlabeled));
  const n = Math.max(0, FRONT_COUNT - reserved);
  return { front: shown.slice(0, n), rest: shown.slice(n) };
}
