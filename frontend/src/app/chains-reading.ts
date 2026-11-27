/**
 * Lecture de l'écran « Chaînes et processus » (EV-06, maquette `maquette-chaines-couches-impact-camille`). Module pur,
 * testé sans navigateur : les mots, les groupes par capacité et les couleurs de domaine se décident ici.
 */

export interface StepItem { id: string; name: string; typeLabel: string; via: string | null; viaKind: string | null; viaId: string | null; capability: string | null; }

export const LAYER_LABELS: Record<string, string> = {
  capacites: 'Capacités', processus: 'Processus', applications: 'Applications', serveurs: 'Serveurs', projets: 'Projets',
  qui: 'Qui', donnees: 'Données',
};

/** Les 9 teintes de la charte (§9.1), dans l'ordre ; au-delà, la teinte neutre. */
export const HUES = ['bleu', 'sarcelle', 'vert', 'olive', 'orange', 'rose', 'magenta', 'violet', 'indigo'];

export function hueOf(domainIndex: number): string | null {
  return domainIndex >= 0 && domainIndex < HUES.length ? HUES[domainIndex] : null;
}

/**
 * « via » nomme toujours la nature du maillon : « via la capacité Relever les compteurs » et non « via Relever les
 * compteurs » sous le processus du même nom (maquette §2). Un serveur dit seulement l'application : « via ONDINE ».
 */
export function viaText(item: Pick<StepItem, 'via' | 'viaKind'>): string | null {
  if (!item.via) return null;
  switch (item.viaKind) {
    case 'capacite': return `via la capacité ${item.via}`;
    case 'processus': return `via le processus ${item.via}`;
    case 'service': return `via le service ${item.via}`;
    case 'acces': return item.via;
    default: return `via ${item.via}`;
  }
}

/** Une cellule groupée par capacité de l'étape, dans l'ordre des capacités de l'étape. */
export function groupByCapability(items: StepItem[], capabilities: { id: string; name: string }[]): { capability: { id: string; name: string } | null; items: StepItem[] }[] {
  const groups = capabilities.map((c) => ({ capability: c, items: items.filter((i) => i.capability === c.id) })).filter((g) => g.items.length > 0);
  const rest = items.filter((i) => !capabilities.some((c) => c.id === i.capability));
  return rest.length ? [...groups, { capability: null, items: rest }] : groups;
}

/** Phrase d'une cellule vide (maquette §5). `link` : un « Renseigner » a un sens (ce n'est pas un trou pour les projets). */
export function emptyText(layer: string): { text: string; link: boolean } {
  switch (layer) {
    case 'capacites': return { text: 'Aucune capacité ne sert cette étape', link: true };
    case 'processus': return { text: 'Aucun processus décrit', link: true };
    case 'applications': return { text: 'Aucune application', link: true };
    case 'serveurs': return { text: 'Aucun serveur connu', link: true };
    case 'projets': return { text: 'Aucun projet', link: false };
    default: return { text: 'Rien de connu', link: false };
  }
}

/** Résumé de l'impact : il compte les étapes, pas les applications (maquette §3). */
export function touchedSummary(name: string, typeLabel: string, steps: number, chains: number): string {
  if (steps === 0) return `${name} ne touche aucune étape : aucune de ses applications ne porte une capacité rattachée à une chaîne ou à un processus.`;
  return `${name} · ${typeLabel} — touche ${steps} ${steps > 1 ? 'étapes' : 'étape'} dans ${chains} ${chains > 1 ? 'chaînes' : 'chaîne'}.`;
}

/** Au-delà de 5 noms : les 5 premiers, puis « + N autres » (maquette §2). */
export const SHOWN = 5;
export function moreText(count: number): string | null {
  const n = count - SHOWN;
  return n > 0 ? `+ ${n} ${n > 1 ? 'autres' : 'autre'}` : null;
}
