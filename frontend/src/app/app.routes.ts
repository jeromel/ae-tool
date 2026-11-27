import { CanActivateFn, Router, Routes } from '@angular/router';
import { inject } from '@angular/core';
import { Role, Session } from './session';
import { LoginPage } from './pages/login';
import { SetupPage } from './pages/setup';
import { AccountPage } from './pages/account';
import { HomePage } from './pages/home';
import { PortfolioPage } from './pages/portfolio';
import { ObjectsPage } from './pages/objects';
import { ObjectPage } from './pages/object';
import { ProjectsPage } from './pages/projects';
import { CapabilitiesPage } from './pages/capabilities';
import { ListSpacePage } from './pages/list-space';
import { ImpactPage } from './pages/impact';
import { QualityPage } from './pages/quality';
import { APPLICATIONS, DATA, FLOWS, INFRASTRUCTURE } from './spaces.config';
import { ADMIN_TABS, AdminShell } from './pages/admin';

/** Toute route exige une session ; sinon connexion, ou premier lancement tant qu'aucun compte n'existe. */
const authenticated: CanActivateFn = (_route, state) => {
  const session = inject(Session);
  const router = inject(Router);
  const user = session.user();
  if (!user) return router.parseUrl(session.setupRequired() ? '/premier-lancement' : '/connexion');
  // Mot de passe temporaire : seule la page du compte est utilisable, le serveur refuse le reste.
  if (user.mustChangePassword && !state.url.startsWith('/compte')) return router.parseUrl('/compte');
  return true;
};

/** Route réservée à un rôle minimal : une session est exigée d'abord, puis le rôle. */
const requires = (minimum: Role): CanActivateFn => (route, state) => {
  const session = inject(Session);
  const router = inject(Router);
  const ok = authenticated(route, state);
  if (ok !== true) return ok;
  return session.can(minimum) ? true : router.parseUrl('/');
};

/** Connexion et premier lancement : inutiles une fois connecté, et le premier lancement l'est dès qu'un compte existe. */
const guest = (page: 'connexion' | 'premier-lancement'): CanActivateFn => () => {
  const session = inject(Session);
  const router = inject(Router);
  if (session.user()) return router.parseUrl('/');
  if (page === 'premier-lancement' && !session.setupRequired()) return router.parseUrl('/connexion');
  if (page === 'connexion' && session.setupRequired()) return router.parseUrl('/premier-lancement');
  return true;
};

/** Entrée « Administration » : ouvre le premier onglet permis au rôle (le Journal seul pour un valideur). */
const firstAdminTab: CanActivateFn = (route, state) => {
  const session = inject(Session);
  const router = inject(Router);
  const ok = authenticated(route, state);
  if (ok !== true) return ok;
  const tab = ADMIN_TABS.find((t) => session.can(t.role));
  return router.parseUrl(tab ? tab.path : '/');
};

/**
 * Les espaces de lecture sont chargés d'emblée. Les pages d'administration et de saisie, moins fréquentées, ne le sont
 * qu'à leur première ouverture : le premier affichage reste rapide quand l'outil grossit.
 */
const protectedRoutes: Routes = [
  { path: '', component: HomePage },
  // Explorer : un espace par couche du système d'information.
  { path: 'capacites', component: CapabilitiesPage },
  { path: 'chaines', loadComponent: () => import('./pages/chains').then((m) => m.ChainsPage) },
  { path: 'applications', component: ListSpacePage, data: { config: APPLICATIONS } },
  { path: 'flux', component: ListSpacePage, data: { config: FLOWS } },
  { path: 'donnees', component: ListSpacePage, data: { config: DATA } },
  { path: 'infrastructure', component: ListSpacePage, data: { config: INFRASTRUCTURE } },
  { path: 'donnees-personnelles', loadComponent: () => import('./pages/personal-data').then((m) => m.PersonalDataPage) },
  { path: 'parcours/:id', loadComponent: () => import('./pages/lineage').then((m) => m.LineagePage) },
  // Décider.
  { path: 'portefeuille', component: PortfolioPage },
  { path: 'portefeuille/:id', loadComponent: () => import('./pages/assessment').then((m) => m.AssessmentPage) },
  { path: 'projets', component: ProjectsPage },
  { path: 'projets/:id', loadComponent: () => import('./pages/project').then((m) => m.ProjectPage) },
  { path: 'impact', component: ImpactPage },
  // Contribuer.
  { path: 'qualite', component: QualityPage },
  { path: 'mes-saisies', loadComponent: () => import('./pages/my-entries').then((m) => m.MyEntriesPage), canActivate: [requires('contributeur')] },
  { path: 'validations', loadComponent: () => import('./pages/validation').then((m) => m.ValidationPage), canActivate: [requires('valideur')] },
  // Les vues portent le tracé des liens (routing.ts) : chargées à leur première ouverture.
  { path: 'vues', loadComponent: () => import('./pages/views').then((m) => m.ViewsPage) },
  { path: 'vues/:id', loadComponent: () => import('./pages/views').then((m) => m.ViewsPage) },
  { path: 'import-drawio', loadComponent: () => import('./pages/drawio-import').then((m) => m.DrawioImportPage), canActivate: [requires('contributeur')] },
  { path: 'compte', component: AccountPage },
  // Hors menu : atteints par la recherche, par une fiche ou par un lien.
  { path: 'referentiel', component: ObjectsPage },
  { path: 'objets/nouveau', loadComponent: () => import('./pages/object-new').then((m) => m.ObjectNewPage), canActivate: [requires('contributeur')] },
  { path: 'objets/:id', component: ObjectPage },
  { path: 'matrices', loadComponent: () => import('./pages/matrix').then((m) => m.MatrixPage) },
  // Administration : une entrée de menu, ses pages en onglets ; les adresses de chaque page restent valides.
  { path: 'administration', canActivate: [firstAdminTab], children: [] },
  {
    path: '', component: AdminShell, children: [
      { path: 'imports', loadComponent: () => import('./pages/imports').then((m) => m.ImportsPage), canActivate: [requires('administrateur')] },
      { path: 'import-catalogue', loadComponent: () => import('./pages/catalogue-import').then((m) => m.CatalogueImportPage), canActivate: [requires('administrateur')] },
      { path: 'sources', loadComponent: () => import('./pages/sources').then((m) => m.SourcesPage), canActivate: [requires('administrateur')] },
      { path: 'sources/:key', loadComponent: () => import('./pages/sources').then((m) => m.SourcesPage), canActivate: [requires('administrateur')] },
      { path: 'metamodele', loadComponent: () => import('./pages/metamodel').then((m) => m.MetamodelPage), canActivate: [requires('administrateur')] },
      { path: 'metamodele/:onglet', loadComponent: () => import('./pages/metamodel').then((m) => m.MetamodelPage), canActivate: [requires('administrateur')] },
      { path: 'utilisateurs', loadComponent: () => import('./pages/users').then((m) => m.UsersPage), canActivate: [requires('administrateur')] },
      { path: 'journal', loadComponent: () => import('./pages/journal').then((m) => m.JournalPage), canActivate: [requires('valideur')] },
      { path: 'sauvegardes', loadComponent: () => import('./pages/backups').then((m) => m.BackupsPage), canActivate: [requires('administrateur')] },
    ],
  },
  { path: '**', redirectTo: '' },
];

export const routes: Routes = [
  { path: 'connexion', component: LoginPage, canActivate: [guest('connexion')] },
  { path: 'premier-lancement', component: SetupPage, canActivate: [guest('premier-lancement')] },
  { path: '', canActivateChild: [authenticated], children: protectedRoutes },
];
