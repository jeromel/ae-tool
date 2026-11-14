import { CanActivateFn, Router, Routes } from '@angular/router';
import { inject } from '@angular/core';
import { Role, Session } from './session';
import { LoginPage } from './pages/login';
import { SetupPage } from './pages/setup';
import { AccountPage } from './pages/account';
import { UsersPage } from './pages/users';
import { HomePage } from './pages/home';
import { PortfolioPage } from './pages/portfolio';
import { AssessmentPage } from './pages/assessment';
import { ObjectsPage } from './pages/objects';
import { ObjectPage } from './pages/object';
import { ViewsPage } from './pages/views';
import { ProjectsPage } from './pages/projects';
import { ProjectPage } from './pages/project';
import { MatrixPage } from './pages/matrix';
import { MetamodelPage } from './pages/metamodel';
import { ImportsPage } from './pages/imports';
import { CapabilitiesPage } from './pages/capabilities';
import { ListSpacePage } from './pages/list-space';
import { ImpactPage } from './pages/impact';
import { QualityPage } from './pages/quality';
import { ObjectNewPage } from './pages/object-new';
import { ValidationPage } from './pages/validation';
import { JournalPage } from './pages/journal';
import { BackupsPage } from './pages/backups';
import { SourcesPage } from './pages/sources';
import { APPLICATIONS, DATA, FLOWS, INFRASTRUCTURE } from './spaces.config';

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

const protectedRoutes: Routes = [
  { path: '', component: HomePage },
  // Comprendre : un espace par couche du système d'information.
  { path: 'capacites', component: CapabilitiesPage },
  { path: 'applications', component: ListSpacePage, data: { config: APPLICATIONS } },
  { path: 'flux', component: ListSpacePage, data: { config: FLOWS } },
  { path: 'donnees', component: ListSpacePage, data: { config: DATA } },
  { path: 'infrastructure', component: ListSpacePage, data: { config: INFRASTRUCTURE } },
  // Décider.
  { path: 'portefeuille', component: PortfolioPage },
  { path: 'portefeuille/:id', component: AssessmentPage },
  { path: 'projets', component: ProjectsPage },
  { path: 'projets/:id', component: ProjectPage },
  { path: 'impact', component: ImpactPage },
  // Tenir à jour.
  { path: 'qualite', component: QualityPage },
  { path: 'validations', component: ValidationPage, canActivate: [requires('valideur')] },
  { path: 'vues', component: ViewsPage },
  { path: 'vues/:id', component: ViewsPage },
  { path: 'imports', component: ImportsPage, canActivate: [requires('administrateur')] },
  { path: 'sources', component: SourcesPage, canActivate: [requires('administrateur')] },
  { path: 'sources/:key', component: SourcesPage, canActivate: [requires('administrateur')] },
  // Administrer.
  { path: 'metamodele', component: MetamodelPage, canActivate: [requires('administrateur')] },
  { path: 'metamodele/:onglet', component: MetamodelPage, canActivate: [requires('administrateur')] },
  { path: 'utilisateurs', component: UsersPage, canActivate: [requires('administrateur')] },
  { path: 'journal', component: JournalPage, canActivate: [requires('valideur')] },
  { path: 'sauvegardes', component: BackupsPage, canActivate: [requires('administrateur')] },
  { path: 'compte', component: AccountPage },
  // Hors menu : atteints par la recherche, par une fiche ou par un lien.
  { path: 'referentiel', component: ObjectsPage },
  { path: 'objets/nouveau', component: ObjectNewPage, canActivate: [requires('contributeur')] },
  { path: 'objets/:id', component: ObjectPage },
  { path: 'matrices', component: MatrixPage },
  { path: '**', redirectTo: '' },
];

export const routes: Routes = [
  { path: 'connexion', component: LoginPage, canActivate: [guest('connexion')] },
  { path: 'premier-lancement', component: SetupPage, canActivate: [guest('premier-lancement')] },
  { path: '', canActivateChild: [authenticated], children: protectedRoutes },
];
