import { Routes } from '@angular/router';
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
import { APPLICATIONS, DATA, FLOWS, INFRASTRUCTURE } from './spaces.config';

export const routes: Routes = [
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
  { path: 'vues', component: ViewsPage },
  { path: 'vues/:id', component: ViewsPage },
  { path: 'imports', component: ImportsPage },
  { path: 'metamodele', component: MetamodelPage },
  // Hors menu : atteints par la recherche, par une fiche ou par un lien.
  { path: 'referentiel', component: ObjectsPage },
  { path: 'objets/:id', component: ObjectPage },
  { path: 'matrices', component: MatrixPage },
  { path: '**', redirectTo: '' },
];
