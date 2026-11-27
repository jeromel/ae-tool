/**
 * Filtres « aucune relation », « aucune application » des espaces et de la page Qualité. Vrais seulement si le périmètre
 * de lecture n'a rien retiré à la ligne : pour un accès sans l'infrastructure, une application reliée à son seul serveur
 * paraîtrait isolée, et le lecteur lirait une absence qui ne tient qu'à son accès (recette de Haussman, question 4).
 * Module sans dépendance, testé hors navigateur.
 */
export const isIsolated = (r: { relations: number; scopeTrimmed?: boolean }) => r.relations === 0 && !r.scopeTrimmed;
export const isOrphan = (r: { applications: number; scopeTrimmed?: boolean }) => r.applications === 0 && !r.scopeTrimmed;
