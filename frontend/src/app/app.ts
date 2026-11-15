import { Component, effect, inject, signal, untracked } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, Brief } from './api';
import { Session } from './session';
import { ObjectSearch } from './object-search';
import { readPref, writePref } from './prefs';

/**
 * Le menu suit l'intention, pas les objets de l'outil : comprendre le système d'information couche par couche,
 * décider en comité, tenir le référentiel à jour, administrer l'outil. Les cas d'usage vivent dans chaque espace, sous forme de questions.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ObjectSearch],
  host: { '[class.nav-closed]': 'closed()' },
  styles: `
    .count { display: inline-block; margin-left: 8px; min-width: 18px; padding: 0 6px; border-radius: 9px; background: var(--signal); color: #fff; font-size: 11.5px; font-weight: 700; text-align: center; line-height: 18px; }
  `,
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
        @if (session.can('valideur')) { <a routerLink="/validations" routerLinkActive="on">Validations@if (toValidate() > 0) { <span class="count" aria-label="objets à valider">{{ toValidate() }}</span> }</a> }
        <a routerLink="/vues" routerLinkActive="on">Vues</a>
        @if (session.can('contributeur')) { <a routerLink="/import-drawio" routerLinkActive="on">Importer un dessin</a> }
        @if (session.can('administrateur')) {
          <a routerLink="/imports" routerLinkActive="on">Imports et exports</a>
          <a routerLink="/sources" routerLinkActive="on">Sources externes</a>
        }

        @if (session.can('valideur')) {
          <div class="group">Administrer</div>
          @if (session.can('administrateur')) {
            <a routerLink="/metamodele" routerLinkActive="on">Métamodèle</a>
            <a routerLink="/utilisateurs" routerLinkActive="on">Utilisateurs</a>
          }
          <a routerLink="/journal" routerLinkActive="on">Journal</a>
          @if (session.can('administrateur')) { <a routerLink="/sauvegardes" routerLinkActive="on">Sauvegardes</a> }
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
        <div class="foot">@if (version()) { Version {{ version() }} }</div>
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
  private api = inject(Api);
  session = inject(Session);
  /** Objets proposés et en attente de validation, affichés à côté de « Validations ». Relu à chaque navigation. */
  toValidate = signal(0);
  /** Version livrée, lue auprès du serveur : utile au support (« quelle version avez-vous ? »). */
  version = signal('');
  /** Menu replié : la place gagnée va aux vues et aux tableaux. */
  closed = signal(readPref('menu.replie', false));

  constructor() {
    // Au démarrage la session arrive après la première navigation : on relit le compteur dès qu'un utilisateur est connu.
    effect(() => {
      this.session.user();
      untracked(() => this.refreshCount());
    });
    this.router.events.subscribe((e) => {
      if (e instanceof NavigationEnd) this.refreshCount();
    });
    this.api.get('/health').then((h) => this.version.set(h.version)).catch(() => undefined);
  }

  private async refreshCount(): Promise<void> {
    // Tant que le mot de passe temporaire n'est pas changé, le serveur refuse tout sauf la page du compte.
    if (!this.session.can('valideur') || this.session.user()?.mustChangePassword) return;
    try {
      this.toValidate.set((await this.api.get('/summary')).toValidate ?? 0);
    } catch {
      /* le compteur est un confort : une erreur ne doit rien bloquer */
    }
  }

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
