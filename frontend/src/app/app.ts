import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <nav class="side">
      <div class="brand">Référentiel<br /><span>d'architecture</span></div>
      <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Accueil</a>
      <div class="group">Décider</div>
      <a routerLink="/portefeuille" routerLinkActive="on">Portefeuille applicatif</a>
      <a routerLink="/projets" routerLinkActive="on">Projets et adhérences</a>
      <div class="group">Explorer</div>
      <a routerLink="/referentiel" routerLinkActive="on">Référentiel</a>
      <a routerLink="/vues" routerLinkActive="on">Vues</a>
      <a routerLink="/matrices" routerLinkActive="on">Matrices</a>
      <div class="group">Administrer</div>
      <a routerLink="/metamodele" routerLinkActive="on">Métamodèle</a>
      <a routerLink="/imports" routerLinkActive="on">Imports et exports</a>
      <div class="foot">Prototype lot 1</div>
    </nav>
    <main><router-outlet /></main>
  `,
})
export class App {}
