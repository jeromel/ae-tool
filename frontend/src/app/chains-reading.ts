/**
 * Lecture de l'écran « Chaînes et processus » (EV-06, maquette `maquette-chaines-couches-impact-camille`). Module pur,
 * testé sans navigateur : les mots, les groupes par capacité et les couleurs de domaine se décident ici.
 */

export interface StepItem { id: string; name: string; typeLabel: string; via: string | null; viaKind: string | null; viaId: string | null; capability: string | null; }

export const LAYER_LABELS: Record<string, string> = {
  capacites: 'Capacités', processus: 'Processus', applications: 'Applications', serveurs: 'Serveurs', projets: 'Projets',
  qui: 'Qui', donnees: 'Données', aucune: 'Aucune',
};

/**
 * Couches au choix sous la chaîne (revue de Camille, 27/11, §4.2, arbitrage PO) : les capacités sont toujours sous les
 * chevrons, elles ne sont plus un bouton ; « Aucune » (l'image seule) est la couche par défaut.
 */
export const NO_LAYER = 'aucune';
export function choosableLayers(apiLayers: string[]): string[] {
  return [...apiLayers.filter((l) => l !== 'capacites'), NO_LAYER];
}

/** Onglet « Hors chaîne » (revue §4.1) : il remplace le bloc replié du bas. */
export const OFF_CHAIN = 'hors';

/** Nombre d'étapes touchées par chaîne : la marque des onglets. */
export function stepsByChain(steps: { chainId: string }[]): Map<string, number> {
  const m = new Map<string, number>();
  for (const t of steps) m.set(t.chainId, (m.get(t.chainId) ?? 0) + 1);
  return m;
}

/**
 * Onglet ouvert (revue §4.1) : celui de l'adresse s'il existe ; sinon, avec un objet désigné, la première chaîne touchée ;
 * sinon la première chaîne (ordre de la carte des capacités) ; sans chaîne, « Hors chaîne ».
 */
export function openChain(chainIds: string[], wanted: string | null | undefined, touched: Map<string, number>, hasOffChain: boolean): string | null {
  if (wanted && (chainIds.includes(wanted) || (wanted === OFF_CHAIN && hasOffChain))) return wanted;
  const firstTouched = chainIds.find((id) => (touched.get(id) ?? 0) > 0);
  if (firstTouched) return firstTouched;
  return chainIds[0] ?? (hasOffChain ? OFF_CHAIN : null);
}

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

/**
 * Changement de réglage, en paramètres d'adresse à fusionner : chacun ne touche que sa clé, si bien que changer de couche
 * garde l'objet désigné et le domaine, et désigner un objet garde la couche (C10). « Aucune » est la couche par défaut.
 * Seule exception : désigner un objet efface la chaîne choisie, pour ouvrir la première chaîne touchée (revue §4.1).
 */
export function layerQuery(layer: string): Record<string, string | null> {
  return { couche: layer === NO_LAYER ? null : layer };
}
export function objectQuery(id: string | null): Record<string, string | null> {
  return id ? { objet: id, chaine: null } : { objet: null };
}
export function domainQuery(current: string | null, clicked: string): Record<string, string | null> {
  return { domaine: current === clicked ? null : clicked };
}

/**
 * Ligne d'une couche sous une étape (revue de Camille, 27/11, §4.2) : les noms dédoublonnés, directs d'abord ; les
 * indirects regroupés en fin de cellule par nature (« + 4 par processus »), le chemin restant dans le panneau d'étape.
 * Pour les serveurs et les projets, le « via » est le chemin normal (l'application, la capacité) : tous sont directs.
 */
export interface CellLine { direct: StepItem[]; indirect: { kind: string; label: string; items: StepItem[] }[]; }

export function cellLine(items: StepItem[], layer: string): CellLine {
  const unique = (list: StepItem[]) => list.filter((i, k) => list.findIndex((j) => j.id === i.id) === k);
  const allDirect = layer === 'serveurs' || layer === 'projets';
  const direct = unique(items.filter((i) => allDirect || !i.via));
  const seen = new Set(direct.map((i) => i.id));
  const indirect: CellLine['indirect'] = [];
  for (const [kind, label] of [['processus', 'par processus'], ['capacite', 'par sous-capacité'], ['service', 'par service']]) {
    const group = unique(items.filter((i) => !allDirect && i.via && i.viaKind === kind && !seen.has(i.id)));
    group.forEach((i) => seen.add(i.id));
    if (group.length) indirect.push({ kind, label, items: group });
  }
  return { direct, indirect };
}

/** Au-delà de 6 noms directs : « + N autres » (revue §4.2). */
export const SHOWN_DIRECT = 6;

/** Marque d'un onglet de chaîne touchée par l'objet désigné : « ● 3 étapes » (revue §4.1). */
export function tabMark(steps: number): string | null {
  return steps > 0 ? `● ${steps} ${steps > 1 ? 'étapes' : 'étape'}` : null;
}
/** Marque de l'onglet « Hors chaîne » : « ● 2 touchés ». */
export function offMark(processes: number): string | null {
  return processes > 0 ? `● ${processes} ${processes > 1 ? 'touchés' : 'touché'}` : null;
}

/** « + N autres » au-delà des noms directs affichés. */
export function moreDirectText(count: number): string | null {
  const n = count - SHOWN_DIRECT;
  return n > 0 ? `+ ${n} ${n > 1 ? 'autres' : 'autre'}` : null;
}

export function chainQuery(id: string | null): Record<string, string | null> {
  return { chaine: id };
}

/**
 * Le nom porte-t-il l'impact sous une étape touchée (revue §4.3) : c'est l'objet désigné lui-même, ou un maillon du chemin
 * par lequel il touche l'étape (l'application qu'un serveur héberge, par exemple). Il est alors encadré dans la ligne.
 */
export function carriesImpact(itemId: string, objectId: string, via: { id: string }[]): boolean {
  return itemId === objectId || via.some((v) => v.id === itemId);
}

/** Phrase « Fait partie de » d'un processus sans place (maquette processus §5) : support, ou à rattacher. */
export function noPlaceText(nature: string | null | undefined): { text: string; attach: boolean } {
  return nature === 'support' || nature === 'pilotage'
    ? { text: `Processus de ${nature} : ne fait partie d'aucune chaîne`, attach: false }
    : { text: 'Fait partie de : aucune chaîne de valeur', attach: true };
}

/**
 * Domaines de la chaîne affichée (revue §4.4) : ceux de ses capacités, et des capacités par lesquelles ses objets y
 * arrivent, dans l'ordre de la carte des capacités. La légende n'en montre que 2 à 4, et non les 9.
 */
export function chainDomains(
  columns: { id: string; kind: string; capabilities: { id: string }[]; layers: Record<string, { capability: string | null }[]> }[],
  domainOf: Record<string, string>, domains: { id: string; name: string }[],
): { id: string; name: string }[] {
  const present = new Set<string>();
  const add = (cap: string | null | undefined) => { if (cap && domainOf[cap]) present.add(domainOf[cap]); };
  for (const c of columns) {
    if (c.kind === 'capacite') add(c.id);
    c.capabilities.forEach((k) => add(k.id));
    Object.values(c.layers).forEach((items) => items.forEach((i) => add(i.capability)));
  }
  return domains.filter((d) => present.has(d.id));
}
