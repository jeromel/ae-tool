/**
 * Pictogrammes des types d'objets, dessinés au trait dans une case de 16 × 16.
 * La clé vient du métamodèle (visuel du type ou de l'élément) ; une clé inconnue prend le pictogramme par défaut.
 */
export const GLYPHS: Record<string, string> = {
  application: 'M3.5 3h9A1.5 1.5 0 0 1 14 4.5v7a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 11.5v-7A1.5 1.5 0 0 1 3.5 3zM2 6.5h12',
  service: 'M5 4.5h6a3.5 3.5 0 0 1 0 7H5a3.5 3.5 0 0 1 0-7z',
  fonction: 'M3 13V7l5-4 5 4v6l-5-3.5z',
  processus: 'M2 6h7V3.5L14 8l-5 4.5V10H2z',
  acteur: 'M8 7.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM3 13.5c0-2.5 2.2-4 5-4s5 1.5 5 4',
  role: 'M6 7.5a2.3 2.3 0 1 0 0-4.6 2.3 2.3 0 0 0 0 4.6zM1.5 13c0-2.3 2-3.6 4.5-3.6s4.5 1.3 4.5 3.6M10.5 3.2a2.2 2.2 0 0 1 0 4.2M12 9.7c1.6.4 2.5 1.6 2.5 3.3',
  objet: 'M3.5 2.5h9v11h-9zM3.5 5.5h9',
  donnee: 'M3 4.5c0-1.1 2.2-2 5-2s5 .9 5 2v7c0 1.1-2.2 2-5 2s-5-.9-5-2zM3 4.5c0 1.1 2.2 2 5 2s5-.9 5-2M3 8c0 1.1 2.2 2 5 2s5-.9 5-2',
  artefact: 'M4 2.5h5l3 3v8H4zM9 2.5v3h3',
  noeud: 'M3 3h10v4H3zM3 9h10v4H3zM5.2 5h.6M5.2 11h.6',
  equipement: 'M2.5 3h11v7.5h-11zM6 13.5h4M8 10.5v3',
  logiciel: 'M8 14A6 6 0 1 0 8 2a6 6 0 0 0 0 12zM8 9.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z',
  reseau: 'M8 5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM3.5 13.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM12.5 13.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM8 5v3M8 8l-3.4 2.7M8 8l3.4 2.7',
  capacite: 'M2 13.5V9.5h4v-4h4v-3h4v11z',
  lieu: 'M8 14s4.5-4.2 4.5-7.5a4.5 4.5 0 0 0-9 0C3.5 9.8 8 14 8 14zM8 8a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z',
  projet: 'M2.5 4h6M5 8h8.5M3.5 12h5',
  objectif: 'M8 14A6 6 0 1 0 8 2a6 6 0 0 0 0 12zM8 10.8a2.8 2.8 0 1 0 0-5.6 2.8 2.8 0 0 0 0 5.6zM8 8h.01',
  exigence: 'M5 4h9l-3 8H2z',
  interface: 'M2 8h6M11 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  evenement: 'M2 4h8l4 4-4 4H2l3-4z',
  zone: 'M2.5 5.5h4v-2h-4zM2.5 5.5h11v7h-11z',
  defaut: 'M4 3h8a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z',
};

/** Libellés proposés dans le paramétrage du métamodèle. */
export const GLYPH_KEYS = Object.keys(GLYPHS);

export function glyphPath(key: string | null | undefined): string {
  return GLYPHS[key ?? ''] ?? GLYPHS['defaut'];
}
