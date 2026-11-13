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

export const routes: Routes = [
  { path: '', component: HomePage },
  { path: 'portefeuille', component: PortfolioPage },
  { path: 'portefeuille/:id', component: AssessmentPage },
  { path: 'referentiel', component: ObjectsPage },
  { path: 'objets/:id', component: ObjectPage },
  { path: 'vues', component: ViewsPage },
  { path: 'vues/:id', component: ViewsPage },
  { path: 'projets', component: ProjectsPage },
  { path: 'projets/:id', component: ProjectPage },
  { path: 'matrices', component: MatrixPage },
  { path: 'metamodele', component: MetamodelPage },
  { path: 'imports', component: ImportsPage },
  { path: '**', redirectTo: '' },
];
