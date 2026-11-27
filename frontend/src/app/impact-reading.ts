/**
 * Lecture de l'analyse d'impact selon l'objet analysé (maquette « analyse d'impact selon l'objet, et qui sert qui ») :
 * quelle colonne se lit en premier, ses titres, ses compteurs, et le tableau où l'alignement remplace la flèche.
 * Module sans dépendance, testé hors navigateur.
 */

export interface ReachedItem {
  id: string; name: string; typeKey: string; layer: string; depth: number; via: string;
  direction: 'aval' | 'amont';
  /** Objets du niveau précédent par lesquels on atteint celui-ci (l'objet analysé au premier niveau). */
  from: string[];
}

/**
 * Nature d'un type pour lire l'analyse : d'après ses rôles, puis d'après le concept ArchiMate dans lequel il est transcodé.
 * Jamais d'après la clé de sa couche (décision 002) : une couche se renomme et un type peut en changer, et la lecture
 * d'impact casserait alors sans aucune erreur (revue de coût d'EV-06 de Colbert, §5).
 */
export type Nature = 'capacite' | 'metier' | 'fonction' | 'application' | 'technique' | 'donnee' | 'autre';

const TECHNICAL_ROLES = ['infrastructure', 'flux-technique', 'partage', 'poste', 'support-donnee'];
const STRATEGY_CONCEPTS = ['Capability', 'ValueStream', 'Resource', 'CourseOfAction'];
const BUSINESS_CONCEPTS = ['Contract', 'Representation', 'Product'];
const TECHNICAL_CONCEPTS = ['Node', 'Device', 'SystemSoftware', 'Path', 'CommunicationNetwork', 'Artifact', 'Equipment', 'Facility',
  'DistributionNetwork', 'Material'];

export function natureOf(roles: string[], concept: string | null | undefined): Nature {
  if (roles.includes('application')) return 'application';
  if (TECHNICAL_ROLES.some((r) => roles.includes(r))) return 'technique';
  if (roles.includes('donnee')) return 'donnee';
  if (roles.includes('capacite') || roles.includes('chaine-valeur')) return 'capacite';
  if (roles.includes('processus') || roles.includes('qui')) return 'metier';
  const c = concept ?? '';
  if (c === 'DataObject' || c === 'BusinessObject') return 'donnee';
  if (STRATEGY_CONCEPTS.includes(c)) return 'capacite';
  if (c.startsWith('Business') || BUSINESS_CONCEPTS.includes(c)) return 'metier';
  if (c === 'ApplicationFunction') return 'fonction';
  if (c.startsWith('Application')) return 'application';
  if (c.startsWith('Technology') || TECHNICAL_CONCEPTS.includes(c)) return 'technique';
  return 'autre';
}

/** Lecture de l'objet analysé selon sa nature. */
export type Subject = 'porte' | 'application' | 'technique' | 'donnee' | 'autre';

export function subjectOf(nature: Nature): Subject {
  switch (nature) {
    case 'application': return 'application';
    case 'capacite': case 'metier': case 'fonction': return 'porte';
    case 'technique': return 'technique';
    case 'donnee': return 'donnee';
    default: return 'autre';
  }
}

export interface Reading {
  /** Sens lu en premier (colonne de gauche). */
  first: 'aval' | 'amont';
  titles: Record<'aval' | 'amont', string>;
  hints: Record<'aval' | 'amont', string>;
  /** Tableau « qui sert qui » dans la première colonne : lignes et cellules, ou null pour une liste par couche. */
  table: { rows: 'application'; cells: 'technique' | 'capacite'; rowsLabel: string; cellsLabel: string } | null;
}

export function readingOf(subject: Subject): Reading {
  switch (subject) {
    case 'porte':
      return {
        first: 'amont',
        titles: { amont: 'Ce qui la porte', aval: 'Ce qui en dépend' },
        hints: { amont: 'Les applications qui la portent, et l\'infrastructure qui sert chacune.', aval: 'Ce qu\'elle sert ou réalise.' },
        table: { rows: 'application', cells: 'technique', rowsLabel: 'Application', cellsLabel: 'Infrastructure qui la sert' },
      };
    case 'application':
      return {
        first: 'aval',
        titles: { aval: 'Ce qui est touché si elle tombe ou change', amont: 'Ce dont elle dépend' },
        hints: { aval: 'Capacités, processus et flux qu\'elle sert ou alimente.', amont: 'L\'infrastructure et ce qui la sert.' },
        table: null,
      };
    case 'technique':
      return {
        first: 'aval',
        titles: { aval: 'Ce qui est touché s\'il tombe ou change', amont: 'Ce dont il dépend' },
        hints: { aval: 'Les applications qu\'il sert, et les capacités que porte chacune.', amont: 'Ce qui le sert ou le contient.' },
        table: { rows: 'application', cells: 'capacite', rowsLabel: 'Application', cellsLabel: 'Capacités qu\'elle porte' },
      };
    case 'donnee':
      return {
        first: 'amont',
        titles: { amont: 'Qui l\'utilise', aval: 'D\'où elle vient' },
        hints: { amont: 'Les applications et processus qui la lisent ou l\'écrivent.', aval: 'Ce qu\'elle réalise ou alimente.' },
        table: null,
      };
    default:
      return {
        first: 'aval',
        titles: { aval: 'Ce qui est touché s\'il tombe ou change', amont: 'Ce dont il dépend' },
        hints: { aval: 'Aval : ce que l\'objet sert, réalise ou alimente.', amont: 'Amont : ce qui le sert, le réalise ou l\'alimente.' },
        table: null,
      };
  }
}

/** Ce qu'il faut savoir des types pour lire l'analyse : application ? capacité ? quelle nature ? */
export interface TypeRoles { isApplication(typeKey: string): boolean; isCapability(typeKey: string): boolean; natureOf(typeKey: string): Nature; }

/** « infra » : compteur d'infrastructure, masqué pour un accès sans elle (il afficherait un 0 trompeur). */
export interface Counter { value: number; label: string; alert?: boolean; infra?: boolean; }

/**
 * Natures par lesquelles une application porte l'objet analysé sans être une simple voisine : pour une capacité, le
 * processus ou le bloc fonctionnel qu'elle sert ; pour un serveur, le logiciel ou la machine qui l'héberge.
 */
export function bridgesOf(subject: Subject): Nature[] {
  return subject === 'porte' ? ['metier', 'fonction'] : subject === 'technique' ? ['technique'] : [];
}

/**
 * Applications qui forment les lignes du tableau : atteintes depuis l'objet analysé lui-même, ou par une couche de
 * passage (bridgesOf). Une application atteinte seulement par le flux d'une autre application, ou par la capacité
 * parente, ne porte pas l'objet : elle reste dans « aussi dans ce sens ».
 */
export function carriers(subjectId: string, subject: Subject, reached: ReachedItem[], t: TypeRoles): Set<string> {
  const nature = new Map(reached.map((r) => [r.id, t.natureOf(r.typeKey)]));
  const bridges = bridgesOf(subject);
  return new Set(reached.filter((r) => t.isApplication(r.typeKey) && r.from.some((f) => f === subjectId || bridges.includes(nature.get(f) ?? 'autre')))
    .map((r) => r.id));
}

/** Compteurs de la couche : seulement ceux qui ont un sens pour elle (pas de « 0 application » par construction). */
export function countersOf(subject: Subject, reached: ReachedItem[], t: TypeRoles, rows?: Set<string>, cells?: Set<string>): Counter[] {
  const side = (d: 'aval' | 'amont') => reached.filter((r) => r.direction === d);
  const apps = (d: 'aval' | 'amont') => side(d).filter((r) => t.isApplication(r.typeKey) && (!rows || rows.has(r.id))).length;
  // Avec un tableau, le second compteur compte ce que montrent ses cellules, pas tout un côté de l'analyse.
  const caps = (d: 'aval' | 'amont') => side(d).filter((r) => t.isCapability(r.typeKey) && (!cells || cells.has(r.id))).length;
  const tech = (d: 'aval' | 'amont') => side(d).filter((r) => t.natureOf(r.typeKey) === 'technique' && (!cells || cells.has(r.id))).length;
  switch (subject) {
    case 'porte': return [{ value: apps('amont'), label: 'applications qui la portent' }, { value: tech('amont'), label: 'serveurs et réseaux en dessous', infra: true }];
    case 'application': return [{ value: caps('aval'), label: 'capacités touchées', alert: caps('aval') > 0 }, { value: tech('amont'), label: 'serveurs dont elle dépend', infra: true }];
    case 'technique': return [{ value: apps('aval'), label: 'applications touchées', alert: apps('aval') > 0 }, { value: caps('aval'), label: 'capacités touchées' }];
    case 'donnee': return [{ value: apps('amont'), label: 'applications qui la lisent ou l\'écrivent' }];
    default: return [{ value: side('aval').length, label: 'objets touchés en aval', alert: side('aval').length > 0 }, { value: side('amont').length, label: 'dépendances en amont' }];
  }
}

export interface CellItem { id: string; name: string; /** Autres lignes que cet objet sert aussi (noms). */ also: string[]; }
export interface ServesRow { id: string; name: string; direct: CellItem[]; below: CellItem[]; }

/**
 * Tableau « qui sert qui » : une ligne par application atteinte dans le sens donné ; dans la cellule, les objets de la
 * colonne atteints à partir d'elle (« direct » : juste après elle ; « below » : plus loin, montrés repliés). Un objet
 * partagé apparaît sous chaque ligne qu'il sert, avec « aussi : … » : c'est l'information utile en cas de panne.
 */
export function servesTable(reached: ReachedItem[], direction: 'aval' | 'amont', isRow: (r: ReachedItem) => boolean,
  isCell: (r: ReachedItem) => boolean): ServesRow[] {
  const side = reached.filter((r) => r.direction === direction);
  const children = new Map<string, ReachedItem[]>();
  for (const r of side) for (const f of r.from) children.set(f, [...(children.get(f) ?? []), r]);
  const rows = side.filter(isRow).sort((a, b) => a.depth - b.depth || a.name.localeCompare(b.name, 'fr'));

  // Pour chaque ligne : les objets de cellule qui en descendent, et à quelle distance d'elle.
  const reach = new Map<string, Map<string, number>>();
  for (const row of rows) {
    const found = new Map<string, number>();
    let frontier = [row.id];
    const seen = new Set(frontier);
    for (let step = 1; frontier.length; step++) {
      const next: string[] = [];
      for (const id of frontier)
        for (const c of children.get(id) ?? []) {
          if (seen.has(c.id) || isRow(c)) continue; // on ne traverse pas une autre ligne : elle a sa propre cellule
          seen.add(c.id);
          if (isCell(c)) found.set(c.id, step);
          // Un composant partagé (un logiciel système installé sur dix serveurs) n'élargit pas le périmètre : il se
          // montre, mais on ne passe pas par lui. Même règle que le parcours d'une donnée.
          if ((children.get(c.id)?.length ?? 0) <= 1) next.push(c.id);
        }
      frontier = next;
    }
    reach.set(row.id, found);
  }
  const byId = new Map(side.map((r) => [r.id, r]));
  const name = (id: string) => byId.get(id)?.name ?? id;
  return rows.map((row) => {
    const found = reach.get(row.id)!;
    const item = (id: string): CellItem => ({
      id, name: name(id),
      also: rows.filter((o) => o.id !== row.id && reach.get(o.id)!.has(id)).map((o) => o.name),
    });
    const sorted = [...found.entries()].sort((a, b) => a[1] - b[1] || name(a[0]).localeCompare(name(b[0]), 'fr'));
    // Premier niveau montré : le plus proche de la ligne où la colonne apparaît ; le reste est replié (« + N en dessous »).
    const nearest = sorted.length ? sorted[0][1] : 0;
    return {
      id: row.id, name: row.name,
      direct: sorted.filter(([, d]) => d === nearest).map(([id]) => item(id)),
      below: sorted.filter(([, d]) => d > nearest).map(([id]) => item(id)),
    };
  });
}
