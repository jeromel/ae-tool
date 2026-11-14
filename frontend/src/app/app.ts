import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Brief } from './api';
import { Session } from './session';
import { ObjectSearch } from './object-search';
import { readPref, writePref } from './prefs';

/**
 * Le menu suit l'intention, pas les objets de l'outil : comprendre le système d'information couche par couche,
 * décider en comité, tenir le référentiel à jour. Les cas d'usage vivent dans chaque espace, sous forme de questions.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ObjectSearch],
  host: { '[class.nav-closed]': 'closed()' },
  template: `
    @if (session.user()) {
    <nav class="side" aria-label="Navigation principale">
      <button type="button" class="fold" [title]="closed() ? 'Déplier le menu' : 'Replier le menu'" [attr.aria-expanded]="!closed()" (click)="toggle()">
        {{ closed() ? '»' : '«' }}
      </button>
      @if (!closed()) {
        <div class="brand">Référentiel<br /><span>d'architecture</span></div>
        <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">Accueil</a>
        <div class="find"><app-object-search placeholder="Rechercher un objet" (picked)="open($event)" (all)="openAll($event)" /></div>

        <div class="group">Comprendre</div>
        <a routerLink="/capacites" routerLinkActive="on">Capacités</a>
        <a routerLink="/applications" routerLinkActive="on">Applications</a>
        <a routerLink="/flux" routerLinkActive="on">Flux</a>
        <a routerLink="/donnees" routerLinkActive="on">Données</a>
        <a routerLink="/infrastructure" routerLinkActive="on">Infrastructure</a>

        <div class="group">Décider</div>
        <a routerLink="/portefeuille" routerLinkActive="on">Portefeuille</a>
        <a routerLink="/projets" routerLinkActive="on">Projets</a>
        <a routerLink="/impact" routerLinkActive="on">Analyse d'impact</a>

        <div class="group">Tenir à jour</div>
        <a routerLink="/qualite" routerLinkActive="on">Qualité du référentiel</a>
        <a routerLink="/vues" routerLinkActive="on">Vues Archi</a>
        @if (session.can('administrateur')) {
          <a routerLink="/imports" routerLinkActive="on">Imports et exports</a>
          <a routerLink="/metamodele" routerLinkActive="on">Métamodèle</a>
          <a routerLink="/utilisateurs" routerLinkActive="on">Utilisateurs</a>
        }
        @if (session.user(); as u) {
          <div class="me">
            <div class="who">{{ u.name }}</div>
            <div class="role">{{ u.role }}</div>
            @if (session.mode() !== 'ouvert') {
              <a routerLink="/compte" routerLinkActive="on">Mon compte</a>
              <button type="button" class="link" (click)="logout()">Se déconnecter</button>
            }
          </div>
        }
        <div class="foot">Prototype lot 1</div>
      }
    </nav>
    }
    <main>
      @if (session.mode() === 'ouvert') { <div class="open-banner">Mode ouvert : aucune authentification</div> }
      <router-outlet />
    </main>
  `,
})
export class App {
  private router = inject(Router);
  session = inject(Session);
  /** Menu replié : la place gagnée va aux vues et aux tableaux. */
  closed = signal(readPref('menu.replie', false));

  toggle(): void {
    this.closed.update((c) => !c);
    writePref('menu.replie', this.closed());
  }

  logout(): void {
    this.session.logout();
  }

  open(o: Brief): void {
    this.router.navigate(['/objets', o.id]);
  }

  openAll(text: string): void {
    this.router.navigate(['/referentiel'], { queryParams: { q: text } });
  }
}
