import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Mark } from '../projection';
import { Api, SOURCE_LABELS } from '../api';
import { Session } from '../session';
import { ObjectForm } from './object-form';
import { ObjectRelations } from './object-relations';
import { ObjectValidation } from '../object-validation';
import { HistoryRefresh, ObjectHistory } from '../object-history';

/** Fiche d'un objet : propriétés, relations, vues, projets, analyse d'impact et visuel propre à l'élément. */
@Component({
  imports: [RouterLink, ObjectValidation, ObjectHistory, ObjectForm, ObjectRelations, Mark],
  template: `
    @if (d(); as d) {
      <a routerLink="/referentiel">← Tous les objets</a>
      <h1><span class="swatch" [style.background]="d.effectiveVisual.fill"></span>{{ d.brief.name || '(sans nom)' }}<app-mark [id]="d.brief.id" /></h1>
      <p class="lead" style="max-width: none">
        <span class="chip info">{{ d.brief.typeLabel }}</span> <span class="chip">{{ d.brief.layerLabel }}</span>
        <span class="chip">Source : {{ sources[d.brief.source] ?? d.brief.source }}</span> <span class="chip">{{ d.brief.status }}</span>
        @if (d.object.archived) { <span class="chip bad">Archivé</span> }
        @if (updated(d.object); as u) { <span class="muted small" style="margin: 0 10px 0 4px">{{ u }}</span> }
        @if (d.hasAssessment) { <a [routerLink]="['/portefeuille', d.brief.id]" style="margin-right: 14px; white-space: nowrap">Voir la notation</a> }
        <a routerLink="/impact" [queryParams]="{ objet: d.brief.id }" style="white-space: nowrap">Analyser l'impact</a>
      </p>
      <app-object-validation [objectId]="d.brief.id" (changed)="load()" />
      @if (notice()) { <div class="note" role="status">{{ notice() }}</div> }
      <div class="row">
        <div class="card">
          <div class="tools">
            <h2 style="margin: 0">Description et propriétés</h2>
            @if (canEdit() && !editing() && !confirmArchive()) {
              <span class="right head-actions">
                <button type="button" (click)="editing.set(true)">Modifier</button>
                @if (d.object.source !== 'archi') { <button type="button" class="ghost" (click)="confirmArchive.set(true)">Archiver</button> }
              </span>
            }
          </div>
          @if (confirmArchive()) {
            <div class="note" role="alert">
              Archiver cet objet ? Il ne sera plus proposé dans les listes ni dans les analyses, mais ses données restent conservées.
              <div class="confirm"><button type="button" (click)="archive()">Archiver l'objet</button> <button type="button" class="ghost" (click)="confirmArchive.set(false)">Annuler</button></div>
            </div>
          }
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
            @if (d.object.documentation) { <p style="white-space: pre-wrap">{{ d.object.documentation }}</p> }
            <dl class="kv">
              @if (d.object.owner) { <dt>Responsable</dt><dd>{{ d.object.owner }}</dd> }
              @for (p of d.properties; track $index) { <dt>{{ p.label }}</dt><dd>{{ p.value }}</dd> }
              <dt>Identifiant</dt><dd class="small muted">{{ d.brief.id }}</dd>
              @for (f of d.folders; track f) { <dt>Dossier</dt><dd>{{ f }}</dd> }
            </dl>
          }
        </div>
        <div class="card" style="max-width: 380px">
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
          @if (d.projects.length) {
            <h2 style="margin-top: 18px">Projets qui touchent cet objet</h2>
            @for (p of d.projects; track p.projectId) {
              <div><a [routerLink]="['/projets', p.projectId]">{{ p.projectName }}</a> <span class="chip">{{ p.nature }}</span></div>
            }
          }
          @if (d.diagrams.length) {
            <h2 style="margin-top: 18px">Présent dans {{ d.diagrams.length }} vue(s)</h2>
            <div style="max-height: 180px; overflow: auto">
              @for (v of d.diagrams; track v.id) { <div><a [routerLink]="['/vues', v.id]">{{ v.name }}</a></div> }
            </div>
          }
        </div>
      </div>
      @if (meta(); as m) {
        <app-object-relations [objectId]="d.brief.id" [objectName]="d.brief.name" [objectTypeKey]="d.brief.typeKey" [archived]="d.object.archived"
          [outgoing]="d.outgoing" [incoming]="d.incoming" [meta]="m" (changed)="changed()" />
      }
      <app-object-history [objectId]="d.brief.id" />
      <div class="card">
        <div class="tools">
          <h2 style="margin: 0">Analyse d'impact</h2>
          <label>Profondeur
            <select (change)="depth.set(+$any($event.target).value); loadImpact()">
              @for (n of [1, 2, 3, 4]; track n) { <option [selected]="depth() === n">{{ n }}</option> }
            </select>
          </label>
          <span class="muted small">Aval : ce que l'objet sert, réalise ou alimente. Amont : ce dont il dépend.</span>
        </div>
        <div class="row">
          @for (dir of ['amont', 'aval']; track dir) {
            <div style="flex: 1; min-width: 280px">
              <b>{{ dir === 'amont' ? 'Amont' : 'Aval' }} ({{ side(dir).length }})</b>
              <div class="scroll" style="max-height: 300px">
                <table>
                  @for (r of side(dir); track r.id) {
                    <tr><td class="num muted" style="width: 30px">{{ r.depth }}</td>
                      <td><a [routerLink]="['/objets', r.id]">{{ r.name }}</a> <span class="muted small">{{ r.typeKey }} · via {{ r.via }}</span></td></tr>
                  } @empty { <tr><td class="muted">Rien à cette profondeur.</td></tr> }
                </table>
              </div>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: `
    .row { align-items: flex-start; }
    .head-actions { display: flex; gap: 8px; }
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
  private session = inject(Session);
  private changes = inject(HistoryRefresh);
  id = input.required<string>();
  d = signal<any>(null);
  meta = signal<any>(null);
  editing = signal(false);
  confirmArchive = signal(false);
  notice = signal('');
  canEdit = computed(() => this.session.can('contributeur') && !this.d()?.object.archived);
  impact = signal<any[]>([]);
  depth = signal(2);
  sources = SOURCE_LABELS;
  shapes = ['rectangle', 'arrondi', 'ellipse'];

  constructor() {
    this.api.get('/metamodel').then((m) => this.meta.set(m));
    effect(() => {
      this.id();
      this.editing.set(false);
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
    this.api.get(`/objects/${this.id()}`).then((d) => this.d.set(d)).catch(() => this.router.navigate(['/referentiel']));
    this.loadImpact();
  }

  loadImpact(): void {
    this.api.get(`/analysis/impact/${this.id()}?depth=${this.depth()}&direction=tous`).then((r) => this.impact.set(r));
  }

  side(direction: string): any[] {
    return this.impact().filter((r) => r.direction === direction);
  }

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
