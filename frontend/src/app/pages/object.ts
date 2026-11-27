import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { ScopeNote } from '../scope-note';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Mark } from '../projection';
import { Api, SOURCE_LABELS } from '../api';
import { Session } from '../session';
import { ObjectForm } from './object-form';
import { ObjectRelations } from './object-relations';
import { ObjectValidation } from '../object-validation';
import { HistoryRefresh, ObjectHistory } from '../object-history';
import { cardProperties } from '../object-card';
import { decisionTone } from '../decision-tone';

/**
 * Fiche d'un objet (maquette carte objet §6, addendum du 27/11) : titre, type et provenance, actions, propriétés de premier
 * plan et décision, puis sections repliées ; relations et historique. Le bloc d'analyse d'impact est retiré : la page
 * d'analyse répond mieux (règles de la 0.15.14). Le visuel propre à l'élément passe dans les réglages avancés.
 */
@Component({
  imports: [RouterLink, ObjectValidation, ObjectHistory, ObjectForm, ObjectRelations, Mark, ScopeNote],
  template: `
    @if (missing()) {
      <!-- Lien vers un objet hors de l'accès : le serveur répond comme pour un objet inexistant, sans trahir son existence
           (décision 012). L'écran ne peut donc pas trancher entre les deux, et le dit. -->
      <a routerLink="/referentiel">← Tous les objets</a>
      <h1>Objet non disponible</h1>
      <p>Cet objet n'existe pas, ou n'est pas dans votre accès.</p>
      <app-scope-note text="Votre accès couvre les processus, applications, flux et données." />
    }
    @if (d(); as d) {
      <a routerLink="/referentiel">← Tous les objets</a>
      <h1><span class="swatch" [style.background]="d.effectiveVisual.fill"></span>{{ d.brief.name || '(sans nom)' }}<app-mark [id]="d.brief.id" /></h1>
      <!-- Fiche (maquette carte objet §6, addendum du 27/11) : titre, type et provenance, puis les actions. -->
      <p class="lead" style="max-width: none">
        <span class="chip info">{{ d.brief.typeLabel }}</span>
        @if (d.brief.source === 'archi') {
          <span class="chip" title="Nom, description, propriétés et relations venus d'Archi : à modifier dans Archi">Maîtrisée dans Archi</span>
        } @else { <span class="chip">Source : {{ sources[d.brief.source] ?? d.brief.source }}</span> }
        <span class="chip">{{ d.brief.status }}</span>
        @if (d.object.archived) { <span class="chip bad">Archivé</span> }
        @if (updated(d.object); as u) { <span class="muted small" style="margin-left: 4px">{{ u }}</span> }
      </p>
      <div class="actions">
        @if (canEdit() && !editing() && !confirmArchive()) { <button type="button" (click)="editing.set(true)">Modifier</button> }
        <a class="btn ghost" [routerLink]="['/parcours', d.brief.id]">Voir le parcours</a>
        <a class="btn ghost" routerLink="/impact" [queryParams]="{ objet: d.brief.id }">Analyser l'impact</a>
        @if (isProcess(d.brief.typeKey)) { <a class="btn ghost" [routerLink]="['/processus', d.brief.id]">Voir les étapes</a> }
        @if (d.hasAssessment) { <a class="btn ghost" [routerLink]="['/portefeuille', d.brief.id]">Voir la notation</a> }
        @if (canEdit() && !editing() && !confirmArchive() && d.object.source !== 'archi') {
          <button type="button" class="ghost right" (click)="confirmArchive.set(true)">Archiver</button>
        }
      </div>
      <app-object-validation [objectId]="d.brief.id" (changed)="load()" />
      @if (notice()) { <div class="note" role="status">{{ notice() }}</div> }
      @if (confirmArchive()) {
        <div class="note" role="alert">
          Archiver cet objet ? Il ne sera plus proposé dans les listes ni dans les analyses, mais ses données restent conservées.
          <div class="confirm"><button type="button" (click)="archive()">Archiver l'objet</button> <button type="button" class="ghost" (click)="confirmArchive.set(false)">Annuler</button></div>
        </div>
      }
      <div class="card">
        @if (editing() && meta(); as m) {
          @if (d.object.source === 'archi') {
            <p class="note">Cet objet est maîtrisé dans Archi : son nom, sa description et les propriétés issues d'Archi s'y modifient, sinon le prochain import les écraserait. Son type, son responsable et les propriétés propres à l'outil restent modifiables ici.</p>
          }
          <app-object-form [meta]="m" [object]="d.object" (saved)="saved()" (cancelled)="editing.set(false)" />
        } @else {
          @if (d.missingRequired.length) {
            <p class="note warn-note">
              Propriété(s) obligatoire(s) à renseigner :
              @for (p of d.missingRequired; track p.key) { <span class="chip warn">{{ p.label }}</span> }
            </p>
          }
          @if (d.object.documentation) { <p class="doc">{{ d.object.documentation }}</p> }
          <dl class="kv">
            @if (d.object.owner) { <dt>Responsable</dt><dd>{{ d.object.owner }}</dd> }
            @for (p of props().front; track p.key) {
              <dt>{{ p.label }} @if (p.unlabeled) { <span class="muted small">(sans libellé)</span> }</dt><dd>{{ p.value }}</dd>
            }
            @if (d.decision) { <dt>Décision</dt><dd><span [class]="tone(d.decision)">{{ d.decision }}</span></dd> }
          </dl>
          <details>
            <summary>Autres propriétés ({{ props().rest.length + 1 + d.folders.length }})</summary>
            <dl class="kv">
              @for (p of props().rest; track p.key) {
                <dt>{{ p.label }} @if (p.unlabeled) { <span class="muted small">(sans libellé)</span> }</dt><dd>{{ p.value }}</dd>
              }
              @for (f of d.folders; track f) { <dt>Dossier</dt><dd>{{ f }}</dd> }
              <dt>Identifiant</dt><dd class="small muted">{{ d.brief.id }}</dd>
            </dl>
          </details>
          @if (d.projects.length) {
            <details>
              <summary>Projets qui la touchent ({{ d.projects.length }})</summary>
              @for (p of d.projects; track p.projectId) {
                <div><a [routerLink]="['/projets', p.projectId]">{{ p.projectName }}</a> <span class="chip">{{ p.nature }}</span></div>
              }
            </details>
          }
          @if (d.diagrams.length) {
            <details>
              <summary>{{ d.diagrams.length > 1 ? 'Présente dans ' + d.diagrams.length + ' vues' : 'Présente dans 1 vue' }}</summary>
              <div class="views">
                @for (v of d.diagrams; track v.id) { <div><a [routerLink]="['/vues', v.id]">{{ v.name }}</a></div> }
              </div>
            </details>
          }
        }
      </div>
      @if ($any(d).scopeTrimmed) { <app-scope-note text="Des relations vers l'infrastructure ne sont pas affichées." /> }
      @if (meta(); as m) {
        <app-object-relations [objectId]="d.brief.id" [objectName]="d.brief.name" [objectTypeKey]="d.brief.typeKey" [archived]="d.object.archived"
          [outgoing]="d.outgoing" [incoming]="d.incoming" [meta]="m" (changed)="changed()" />
      }
      <app-object-history [objectId]="d.brief.id" />
      <!-- O3 : le visuel propre à l'élément quitte la fiche, réservé au valideur et à l'administrateur. -->
      @if (session.can('valideur')) {
        <details class="card advanced">
          <summary>Réglages avancés</summary>
          <h2>Visuel de cet élément</h2>
          <p class="muted small">Par défaut, l'élément prend le visuel de son type. Un réglage ici ne vaut que pour lui, dans toutes les vues affichées avec les couleurs du métamodèle.</p>
          <dl class="kv" style="grid-template-columns: 110px 1fr">
            <dt>Couleur de fond</dt>
            <dd><input type="color" [value]="d.effectiveVisual.fill" (change)="setVisual({ fill: $any($event.target).value })" />
              @if (d.object.visualOverride?.fill) { <span class="chip warn">propre à l'élément</span> } @else { <span class="chip">du type</span> }</dd>
            <dt>Forme</dt>
            <dd>
              <select (change)="setVisual({ shape: $any($event.target).value })">
                @for (s of shapes; track s) { <option [selected]="d.effectiveVisual.shape === s">{{ s }}</option> }
              </select>
            </dd>
          </dl>
          @if (d.object.visualOverride) { <button class="ghost" style="margin-top: 10px" (click)="resetVisual()">Revenir au visuel du type</button> }
        </details>
      }
    }
  `,
  styles: `
    .actions { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: -4px 0 14px; }
    .actions .right { margin-left: auto; }
    .doc { white-space: pre-wrap; margin-top: 0; }
    details { margin-top: 10px; }
    summary { cursor: pointer; color: var(--accent); font-weight: 500; }
    .views { max-height: 180px; overflow: auto; margin-top: 6px; }
    .advanced summary { font-size: 14px; }
    .confirm { display: flex; gap: 8px; margin-top: 8px; }
    .warn-note { background: #fbe9cc; display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
  `,
})
export class ObjectPage {
  /** « Mis à jour le … par … » (exigence EF-05) ; rien tant que l'objet ne porte pas de date. */
  updated(o: { updatedAt?: string; updatedBy?: string | null }): string {
    const at = o.updatedAt ? new Date(o.updatedAt) : null;
    if (!at || at.getFullYear() < 2000) return '';
    return `Mis à jour le ${at.toLocaleDateString('fr-FR')}` + (o.updatedBy ? ` par ${o.updatedBy}` : '');
  }

  private api = inject(Api);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  session = inject(Session);
  /** Objet introuvable pour un accès sans l'infrastructure : on l'explique au lieu de renvoyer à la liste. */
  missing = signal(false);
  private changes = inject(HistoryRefresh);
  id = input.required<string>();
  d = signal<any>(null);
  meta = signal<any>(null);
  /** Un processus a son écran d'étapes (EV-06, revue de Camille §4.5). */
  isProcess(typeKey: string): boolean {
    return !!(this.meta()?.objectTypes as any[] | undefined)?.find((t) => t.key === typeKey)?.roles.includes('processus');
  }
  editing = signal(false);
  confirmArchive = signal(false);
  notice = signal('');
  // Droit serveur : AccessRules.cs, écriture sous /api/objects → contributeur.
  canEdit = computed(() => this.session.can('contributeur') && !this.d()?.object.archived);
  props = computed(() => cardProperties(this.d()?.properties ?? [], this.session.can('administrateur'), this.d()?.object.owner ? 1 : 0));
  sources = SOURCE_LABELS;
  shapes = ['rectangle', 'arrondi', 'ellipse'];

  constructor() {
    this.api.get('/metamodel').then((m) => this.meta.set(m));
    effect(() => {
      this.id();
      // « Corriger → » depuis Mes saisies ouvre la fiche en modification (`?modifier=1`).
      this.editing.set(this.route.snapshot.queryParamMap.get('modifier') === '1');
      this.confirmArchive.set(false);
      this.load();
    });
  }

  saved(): void {
    this.editing.set(false);
    this.say('Modifications enregistrées.');
    this.changed();
  }

  /** Après une écriture faite depuis la fiche : elle se relit, et prévient le bandeau de validation et l'historique. */
  changed(): void {
    this.changes.bump();
    this.load();
  }

  async archive(): Promise<void> {
    try {
      await this.api.delete(`/objects/${this.id()}`);
      this.router.navigate(['/referentiel']);
    } catch (e) {
      this.confirmArchive.set(false);
      this.say(e instanceof Error ? e.message : 'L\'archivage a échoué.');
    }
  }

  private say(message: string): void {
    this.notice.set(message);
    setTimeout(() => this.notice.set(''), 5000);
  }

  load(): void {
    this.missing.set(false);
    this.api.get(`/objects/${this.id()}`).then((d) => this.d.set(d)).catch(() => {
      if (this.session.business()) this.missing.set(true);
      else this.router.navigate(['/referentiel']);
    });
  }

  tone = decisionTone;

  async setVisual(change: Record<string, string>): Promise<void> {
    const current = this.d().object.visualOverride ?? {};
    await this.api.put(`/objects/${this.id()}`, { visualOverride: { ...current, ...change } });
    this.changed();
  }

  async resetVisual(): Promise<void> {
    await this.api.put(`/objects/${this.id()}`, { clearVisualOverride: true });
    this.changed();
  }
}
