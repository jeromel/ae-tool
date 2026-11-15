import { Component, effect, inject, signal, untracked } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Api, Brief } from './api';
import { Session } from './session';
import { ObjectSearch } from './object-search';
import { readPref, writePref } from './prefs';
import { ProjectionState } from './projection';

/**
 * Le menu suit l'intention, pas les objets de l'outil : comprendre le système d'information couche par couche,
 * décider en comité, tenir le référentiel à jour, administrer l'outil. Les cas d'usage vivent dans chaque espace, sous forme de questions.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ObjectSearch],
  host: { '[class.nav-closed]': 'closed()' },
  styles: `
    .etat { margin: 4px 12px 10px; }
    .etat label { display: block; font-size: 11.5px; color: #a9c0c7; margin-bottom: 3px; }
    .etat select { width: 100%; font-size: 12.5px; }
    .projected { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 8px 18px; background: #fff4d6; border-bottom: 1px solid #ecd28a; font-size: 13.5px; }
    .projected b { font-weight: 650; }
    .projected details { position: relative; }
    .projected details > div { position: absolute; z-index: 30; top: 26px; left: 0; width: 560px; max-height: 60vh; overflow: auto; background: #fff;
      border: 1px solid #d9e2e4; border-radius: 8px; padding: 10px 14px; box-shadow: 0 8px 24px rgba(0,0,0,.12); }
    .projected .issue { color: #8c2a1c; }
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
        @if (states().length > 0 || projection.etat) {
          <div class="etat">
            <label for="etat-si">État du SI regardé</label>
            <select id="etat-si" (change)="choose($any($event.target).value)">
              <option value="" [selected]="!projection.etat">Aujourd'hui (existant)</option>
              @for (s of states(); track s.scenarioId) {
                <optgroup [label]="s.projectName + ' · ' + s.scenarioName + (s.status === 'Brouillon' ? ' (brouillon)' : '')">
                  @for (st of s.steps; track st.id) {
                    <option [value]="s.scenarioId + ':' + st.id" [selected]="projection.etat?.stepId === st.id">{{ st.order }}. {{ st.name }}</option>
                  }
                </optgroup>
              }
            </select>
          </div>
        }

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
      @if (projection.etat; as e) {
        <div class="projected" role="status">
          <span>SI projeté : <b>{{ e.label }}</b>. Lecture seule.</span>
          @if (projection.summary(); as s) {
            <details>
              <summary>{{ count(s, 'ajouté') }} ajouté(s), {{ count(s, 'modifié') }} modifié(s), {{ count(s, 'retiré') }} retiré(s)@if (s.issues.length) {, <span class="issue">{{ s.issues.length }} point(s) à revoir</span>}</summary>
              <div>
                @for (i of s.issues; track $index) { <p class="small issue">{{ i.message }}</p> }
                <table>
                  <tr><th>Changement</th><th>Élément</th><th>Type</th></tr>
                  @for (i of s.items; track i.id) {
                    <tr><td>{{ i.mark }}</td><td>@if (i.kind === 'objet' && i.mark !== 'retiré') { <a [routerLink]="['/objets', i.id]">{{ i.name }}</a> } @else { {{ i.name }} }</td><td class="small">{{ i.typeLabel }}</td></tr>
                  }
                </table>
              </div>
            </details>
            <a [routerLink]="['/projets', s.scenario.projectId]">Voir le projet</a>
          }
          @if (projection.error()) { <span class="issue">{{ projection.error() }}</span> }
          <button type="button" class="ghost" (click)="projection.leave()">Revenir à l'existant</button>
        </div>
      }
      <router-outlet />
    </main>
  `,
})
export class App {
  private router = inject(Router);
  private api = inject(Api);
  session = inject(Session);
  projection = inject(ProjectionState);
  /** Étapes des scénarios visibles, proposées au sélecteur d'état. */
  states = signal<any[]>([]);
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
      untracked(() => this.refreshStates());
    });
    this.router.events.subscribe((e) => {
      if (e instanceof NavigationEnd) { this.refreshCount(); this.refreshStates(); }
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

  private async refreshStates(): Promise<void> {
    if (!this.session.user() || this.session.user()?.mustChangePassword) return;
    try {
      this.states.set(await this.api.get('/trajectoires/etats'));
    } catch {
      /* le sélecteur est un confort */
    }
  }

  choose(value: string): void {
    if (!value) return this.projection.leave();
    const [scenarioId, stepId] = value.split(':');
    const s = this.states().find((x) => x.scenarioId === scenarioId);
    const st = s?.steps.find((x: any) => x.id === stepId);
    this.projection.enter(scenarioId, stepId, `${s.projectName} › ${s.scenarioName} › ${st.name}`);
  }

  count(s: { items: { mark: string }[] }, mark: string): number {
    return s.items.filter((i) => i.mark === mark).length;
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
