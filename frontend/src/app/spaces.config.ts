import { Question } from './questions';

/** Colonne d'un espace en liste. « objet » rend un lien vers la fiche, d'après l'identifiant porté par « idKey ». */
export interface Column { key: string; label: string; kind?: 'objet' | 'nombre' | 'liste' | 'chip'; idKey?: string; }

/** Question d'un espace en liste : un filtre, un tri décroissant, ou les deux. */
export interface SpaceQuestion extends Question { filter?: (row: any) => boolean; sort?: string; }

/**
 * Un espace est une donnée : son titre, la projection qu'il lit, ses colonnes et ses questions.
 * Ajouter une question à un espace ne demande que d'ajouter une ligne ici.
 */
export interface SpaceConfig {
  title: string;
  lead: string;
  endpoint: string;
  noun: string;
  searchKeys: string[];
  columns: Column[];
  questions: SpaceQuestion[];
  /** Clé de l'identifiant d'objet d'une ligne : active les actions « Contexte » et « Impact ». */
  actionsIdKey?: string;
  /** Rôle fonctionnel des objets de l'espace : active le bouton de création, qui propose le premier type portant ce rôle. */
  createRole?: string;
  /** Libellé du bouton de création (« Nouvelle application »). */
  createLabel?: string;
  matrix?: { rows: string; cols: string; relation?: string; label: string };
}

export const APPLICATIONS: SpaceConfig = {
  title: 'Applications',
  lead: 'Chaque application avec ce qui la relie au reste du système d\'information : flux, socle technique, données, capacités et projets.',
  endpoint: '/spaces/applications',
  noun: 'application(s)',
  searchKeys: ['name', 'decision'],
  actionsIdKey: 'id',
  createRole: 'application',
  createLabel: 'Nouvelle application',
  matrix: { rows: 'role:application', cols: 'role:capacite', label: 'Applications × capacités' },
  columns: [
    { key: 'name', label: 'Application', kind: 'objet', idKey: 'id' },
    { key: 'flows', label: 'Flux', kind: 'nombre' },
    { key: 'technical', label: 'Socle technique', kind: 'nombre' },
    { key: 'data', label: 'Données', kind: 'nombre' },
    { key: 'capabilities', label: 'Capacités', kind: 'nombre' },
    { key: 'projects', label: 'Projets', kind: 'nombre' },
    { key: 'cm', label: 'Criticité métier', kind: 'nombre' },
    { key: 'decision', label: 'Décision', kind: 'chip' },
  ],
  questions: [
    { key: 'toutes', label: 'Toutes les applications' },
    { key: 'echanges', label: 'Lesquelles échangent le plus ?', sort: 'flows', filter: (r) => r.flows > 0 },
    { key: 'projets', label: 'Lesquelles sont touchées par un projet ?', sort: 'projects', filter: (r) => r.projects > 0 },
    { key: 'sans-capacite', label: 'Lesquelles ne servent aucune capacité connue ?', filter: (r) => r.capabilities === 0 },
    { key: 'isolees', label: 'Lesquelles ne sont reliées à rien ?', filter: (r) => r.relations === 0 },
    {
      key: 'acquisition', label: 'Lesquelles sont en SaaS, achetées ou développées ?',
      missing: 'Le mode d\'acquisition et d\'hébergement n\'est renseigné sur aucune application. Ajoutez cette propriété aux applications pour appliquer la règle « SaaS avant achat avant développement ».',
    },
  ],
};

export const FLOWS: SpaceConfig = {
  title: 'Flux',
  lead: 'Les échanges entre applications et équipements, avec leur protocole, leur chiffrement et les données qu\'ils transportent.',
  endpoint: '/spaces/flows',
  noun: 'flux',
  searchKeys: ['name', 'sourceName', 'targetName', 'protocol'],
  matrix: { rows: 'role:application', cols: 'role:application', relation: 'role:flux', label: 'Émetteurs × récepteurs' },
  columns: [
    { key: 'sourceName', label: 'Émetteur', kind: 'objet', idKey: 'sourceId' },
    { key: 'targetName', label: 'Récepteur', kind: 'objet', idKey: 'targetId' },
    { key: 'name', label: 'Flux' },
    { key: 'protocol', label: 'Protocole', kind: 'chip' },
    { key: 'encryption', label: 'Chiffrement' },
    { key: 'carried', label: 'Données transportées', kind: 'liste' },
  ],
  questions: [
    { key: 'tous', label: 'Tous les flux' },
    { key: 'sans-protocole', label: 'Lesquels n\'ont pas de protocole renseigné ?', filter: (r) => !r.protocol },
    { key: 'sans-chiffrement', label: 'Lesquels n\'ont pas de chiffrement renseigné ?', filter: (r) => !r.encryption, hint: 'Non renseigné ne veut pas dire non chiffré.' },
    { key: 'avec-donnees', label: 'Lesquels transportent des données identifiées ?', filter: (r) => r.carried.length > 0 },
    {
      key: 'frontiere', label: 'Lesquels traversent la frontière IT/OT ?',
      missing: 'La zone (IT ou OT) n\'est pas portée par les objets. Ajoutez une propriété de zone aux applications et aux équipements pour faire ressortir les flux qui la traversent.',
    },
  ],
};

export const DATA: SpaceConfig = {
  title: 'Données',
  lead: 'Les objets métier et objets de données, avec les applications qui les utilisent, directement ou par un artefact qui les réalise.',
  endpoint: '/spaces/data',
  noun: 'objet(s) de données',
  searchKeys: ['name', 'typeLabel'],
  actionsIdKey: 'id',
  createRole: 'donnee',
  createLabel: 'Nouvelle donnée',
  columns: [
    { key: 'name', label: 'Donnée', kind: 'objet', idKey: 'id' },
    { key: 'typeLabel', label: 'Type' },
    { key: 'applications', label: 'Applications qui l\'utilisent', kind: 'nombre' },
    { key: 'artifacts', label: 'Artefacts', kind: 'nombre' },
  ],
  questions: [
    { key: 'toutes', label: 'Toutes les données' },
    { key: 'partagees', label: 'Lesquelles sont les plus partagées ?', sort: 'applications', filter: (r) => r.applications > 0 },
    { key: 'orphelines', label: 'Lesquelles ne sont utilisées par aucune application ?', filter: (r) => r.applications === 0 },
    {
      key: 'maitre', label: 'Quelle application fait référence pour chaque donnée ?',
      missing: 'Le référentiel maître d\'une donnée n\'est pas modélisé. Les accès existent, mais rien ne distingue l\'application qui fait foi de celles qui lisent.',
    },
  ],
};

export const INFRASTRUCTURE: SpaceConfig = {
  title: 'Infrastructure',
  lead: 'Nœuds, équipements, logiciels système et réseaux, IT comme OT, avec les applications qu\'ils portent jusqu\'à trois relations de distance.',
  endpoint: '/spaces/infrastructure',
  noun: 'élément(s) technique(s)',
  searchKeys: ['name', 'typeLabel'],
  actionsIdKey: 'id',
  createRole: 'infrastructure',
  createLabel: 'Nouvel élément technique',
  columns: [
    { key: 'name', label: 'Élément', kind: 'objet', idKey: 'id' },
    { key: 'typeLabel', label: 'Type' },
    { key: 'applications', label: 'Applications portées', kind: 'nombre' },
    { key: 'relations', label: 'Relations', kind: 'nombre' },
  ],
  questions: [
    { key: 'tout', label: 'Toute l\'infrastructure' },
    { key: 'portant', label: 'Qu\'est-ce qui porte le plus d\'applications ?', sort: 'applications', filter: (r) => r.applications > 0 },
    { key: 'sans-application', label: 'Qu\'est-ce qui ne porte aucune application ?', filter: (r) => r.applications === 0 },
    {
      key: 'fin-support', label: 'Qu\'est-ce qui arrive en fin de support ?',
      missing: 'Les versions et dates de fin de support ne sont pas dans le référentiel. Elles viendront de la CMDB EasyVista (lot 2).',
    },
  ],
};
