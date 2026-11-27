import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api, Brief, SOURCE_LABELS } from '../api';
import { FieldChange, FieldChanges } from '../object-history';
import { Session } from '../session';
import { type ArchiPendingSummary, citedLine, exclusionLine, pendingState, pendingTotals, viewsLine } from '../archi-pending';

interface PendingObject { brief: Brief; validatedBy: string | null; validatedAt: string | null; }
interface PendingRelation { id: string; name: string | null; typeLabel: string; source: string; from: string; to: string; }
interface ArchiPending extends ArchiPendingSummary { objects: PendingObject[]; relations: PendingRelation[]; }

interface PendingItem {
  brief: Brief;
  proposedBy: string | null;
  proposedAt: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  recent: { at: string; user: string; action: string; changes: FieldChange[] | null }[] | null;
}

/** File des objets proposés à la validation : filtre, validation en lot, refus avec commentaire. */
@Component({
  imports: [RouterLink, DatePipe, FieldChanges],
  template: `
    <h1>Validations</h1>
    <p class="lead">Objets proposés par les contributeurs et en attente d'un valideur. Un refus renvoie l'objet en brouillon avec votre commentaire.</p>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    @if (message()) { <div class="note" role="status">{{ message() }}</div> }

    <div class="card flush" id="file">
      <div class="tools">
        <select aria-label="Filtrer par type" (change)="type.set($any($event.target).value)">
          <option value="">Tous les types</option>
          @for (t of types(); track t) { <option [value]="t" [selected]="t === type()">{{ t }}</option> }
        </select>
        <select aria-label="Filtrer par auteur de la proposition" (change)="author.set($any($event.target).value)">
          <option value="">Tous les auteurs</option>
          @for (a of authors(); track a) { <option [value]="a" [selected]="a === author()">{{ a }}</option> }
        </select>
        <span class="muted small">{{ filtered().length }} objet(s) à valider</span>
        <button type="button" class="right" [disabled]="busy() || selectedIds().length === 0" (click)="validateSelection()">
          Valider la sélection ({{ selectedIds().length }})
        </button>
      </div>
      @if (filtered().length === 0) {
        <p class="empty muted">{{ loaded() ? 'Aucun objet en attente de validation.' : 'Chargement…' }}</p>
      } @else {
        <div class="scroll">
          <table>
            <tr>
              <th><input type="checkbox" aria-label="Tout sélectionner" [checked]="allSelected()" (change)="toggleAll($any($event.target).checked)" /></th>
              <th>Objet</th><th>Type</th><th>Source</th><th>Proposé par</th><th>Le</th><th>Dernière modification</th><th></th>
            </tr>
            @for (i of filtered(); track i.brief.id) {
              <tr>
                <td><input type="checkbox" [attr.aria-label]="'Sélectionner ' + i.brief.name" [checked]="selected().has(i.brief.id)" (change)="toggle(i.brief.id)" /></td>
                <td><a [routerLink]="['/objets', i.brief.id]">{{ i.brief.name || '(sans nom)' }}</a></td>
                <td><span class="chip info">{{ i.brief.typeLabel }}</span></td>
                <td class="small">{{ sources[i.brief.source] ?? i.brief.source }}</td>
                <td>{{ i.proposedBy ?? '—' }}</td>
                <td class="small">{{ i.proposedAt ? (i.proposedAt | date: 'dd/MM/yyyy HH:mm') : '—' }}</td>
                <td class="small">
                  @if (i.recent?.length) {
                    <button type="button" class="link" (click)="toggleDetail(i.brief.id)">
                      {{ i.recent![0].action }} par {{ i.recent![0].user }} {{ detail() === i.brief.id ? '▾' : '▸' }}
                    </button>
                  } @else { <span class="muted">aucune</span> }
                </td>
                <td class="actions">
                  <button type="button" class="ghost" [disabled]="busy()" (click)="startReject(i.brief.id)">Refuser…</button>
                </td>
              </tr>
              @if (detail() === i.brief.id && i.recent) {
                <tr class="sub"><td></td><td colspan="7">
                  @for (r of i.recent; track $index) {
                    <div class="small"><b>{{ r.action }}</b> par {{ r.user }} le {{ r.at | date: 'dd/MM/yyyy HH:mm' }}</div>
                    @if (r.changes?.length) { <app-field-changes [changes]="r.changes!" /> }
                  }
                </td></tr>
              }
              @if (rejecting() === i.brief.id) {
                <tr class="sub"><td></td><td colspan="7">
                  <form (submit)="reject($event, i.brief.id)">
                    <label [attr.for]="'c-' + i.brief.id">Commentaire du refus (obligatoire)</label>
                    <textarea [id]="'c-' + i.brief.id" rows="2" required [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
                    <div class="row-actions">
                      <button type="submit" [disabled]="busy() || !comment().trim()">Refuser la proposition</button>
                      <button type="button" class="ghost" (click)="rejecting.set(null)">Annuler</button>
                    </div>
                  </form>
                </td></tr>
              }
            }
          </table>
        </div>
      }
    </div>

    @if (!session.business()) {
      <section class="card archi" aria-labelledby="archi-title">
        <h2 id="archi-title">Pas encore dans Archi</h2>
        @switch (archiState()) {
          @case ('chargement') { <p class="muted">Calcul de ce qui manque dans Archi…</p> }
          @case ('erreur') { <div class="error" role="alert">{{ archiError() }}</div> }
          @case ('jamais') {
            <p>Aucun modèle Archi n'a encore été importé : tout le référentiel est nouveau pour Archi.</p>
            <p class="muted small">Utilisez l'export complet (Administration › Imports › Exports), puis importez le modèle Archi en suivant le guide de prise en main (§ 2).</p>
          }
          @case ('vide') {
            <p>Rien à reporter : tout ce qui a été créé et validé ici est déjà dans Archi (dernier import le {{ archi()!.lastImport | date: 'dd/MM/yyyy à HH:mm' }}).</p>
            @if (exclusion()) { <p class="warn small">{{ exclusion() }} <a routerLink="." fragment="file">À valider d'abord.</a></p> }
          }
          @case ('liste') {
            <p class="muted small">{{ lead() }}</p>
            <div class="totals">
              <strong>{{ totals() }}</strong>
              <a class="btn" [href]="api.url('/export/a-reporter/fichier')" download>Télécharger pour Archi</a>
            </div>
            <details>
              <summary>Voir la liste</summary>
              @if (archi()!.objects.length) {
                <table>
                  <tr><th>Objet</th><th>Type</th><th>Source</th><th>Validé par</th></tr>
                  @for (o of archi()!.objects; track o.brief.id) {
                    <tr>
                      <td><a [routerLink]="['/objets', o.brief.id]">{{ o.brief.name || '(sans nom)' }}</a></td>
                      <td><span class="chip info">{{ o.brief.typeLabel }}</span></td>
                      <td class="small">{{ sources[o.brief.source] ?? o.brief.source }}</td>
                      <td class="small">{{ o.validatedBy ?? '—' }}</td>
                    </tr>
                  }
                </table>
              }
              @if (archi()!.relations.length) {
                <table>
                  <tr><th>Relation</th><th>Type</th><th>Source</th></tr>
                  @for (r of archi()!.relations; track r.id) {
                    <tr>
                      <td>{{ r.from }} → {{ r.to }} @if (r.name) { <span class="muted small">« {{ r.name }} »</span> }</td>
                      <td><span class="chip info">{{ r.typeLabel }}</span></td>
                      <td class="small">{{ sources[r.source] ?? r.source }}</td>
                    </tr>
                  }
                </table>
              }
            </details>
            @if (exclusion()) { <p class="warn small">⚠ {{ exclusion() }} <a routerLink="." fragment="file">À valider d'abord.</a></p> }
            @if (cited()) { <p class="small">{{ cited() }}</p> }
            @if (views()) { <p class="small">{{ views() }}</p> }
            <details>
              <summary>Comment faire la fusion dans Archi (4 étapes)</summary>
              <ol class="small">
                <li>Dans Archi, <i>Fichier › Importer › Open Exchange XML Model</i>, et choisir le fichier téléchargé.</li>
                <li>Enregistrer ce modèle en <code>.archimate</code>.</li>
                <li>Ouvrir le modèle maître, le sélectionner, puis <i>Fichier › Importer › Another Model into Selected Model</i> :
                  <strong>cocher « Update existing objects and folders »</strong>, puis
                  <strong>décocher « Update model information and top-level folders »</strong>.</li>
                <li>Ranger les nouveaux objets : Archi les a posés dans les dossiers par défaut de leur type.</li>
              </ol>
              <p class="muted small">Le détail est dans le guide de prise en main, § 2.</p>
            </details>
            <p class="small">Après la fusion, réimportez le modèle Archi : cette liste se videra.</p>
          }
        }
      </section>
    }
  `,
  styles: `
    .archi { margin-top: 16px; }
    .archi h2 { margin-top: 0; }
    .archi .totals { display: flex; align-items: center; gap: 16px; margin: 8px 0; }
    .archi details { margin: 8px 0; }
    .archi summary { cursor: pointer; color: var(--accent); font-weight: 500; }
    .archi table { margin: 8px 0; }
    .archi ol { margin: 6px 0; padding-left: 20px; }
    .archi .warn { color: var(--warn, #8a5a00); }
    .empty { padding: 20px 16px; margin: 0; }
    .sub td { background: #f6f8f8; }
    form { display: flex; flex-direction: column; gap: 6px; max-width: 640px; }
    textarea { width: 100%; box-sizing: border-box; }
    .row-actions { display: flex; gap: 8px; }
    button.link { background: none; border: 0; padding: 0; color: var(--accent); font-weight: 500; text-decoration: underline; cursor: pointer; }
    button.link:hover { background: none; }
  `,
})
export class ValidationPage {
  api = inject(Api);
  session = inject(Session);
  sources = SOURCE_LABELS;

  archi = signal<ArchiPending | null>(null);
  archiError = signal('');
  archiState = computed(() => pendingState(this.archi(), this.archiError()));
  lead = computed(() => {
    const at = this.archi()?.lastImport;
    const date = at ? new Date(at) : null;
    const when = date ? ` (importé le ${date.toLocaleDateString('fr-FR')} à ${date.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })})` : '';
    return `Objets validés créés ici et absents du dernier import du modèle Archi${when}, avec leurs relations.`;
  });
  totals = computed(() => (this.archi() ? pendingTotals(this.archi()!) : ''));
  exclusion = computed(() => (this.archi() ? exclusionLine(this.archi()!) : null));
  cited = computed(() => (this.archi() ? citedLine(this.archi()!) : null));
  views = computed(() => (this.archi() ? viewsLine(this.archi()!) : null));

  items = signal<PendingItem[]>([]);
  loaded = signal(false);
  error = signal('');
  message = signal('');
  busy = signal(false);
  type = signal('');
  author = signal('');
  selected = signal<Set<string>>(new Set());
  detail = signal<string | null>(null);
  rejecting = signal<string | null>(null);
  comment = signal('');

  types = computed(() => [...new Set(this.items().map((i) => i.brief.typeLabel))].sort((a, b) => a.localeCompare(b, 'fr')));
  authors = computed(() => [...new Set(this.items().map((i) => i.proposedBy).filter((a): a is string => !!a))].sort((a, b) => a.localeCompare(b, 'fr')));
  filtered = computed(() => this.items().filter((i) => (!this.type() || i.brief.typeLabel === this.type()) && (!this.author() || i.proposedBy === this.author())));
  /** La sélection ne garde que ce qui est affiché : un filtre ne doit pas valider des objets qu'on ne voit plus. */
  selectedIds = computed(() => this.filtered().map((i) => i.brief.id).filter((id) => this.selected().has(id)));
  allSelected = computed(() => this.filtered().length > 0 && this.selectedIds().length === this.filtered().length);

  constructor() {
    this.load();
  }

  async load(): Promise<void> {
    try {
      this.items.set((await this.api.get('/validation/pending')).items);
    } catch (e) {
      this.error.set((e as Error).message);
    }
    this.loaded.set(true);
    if (this.session.business()) return;
    try {
      this.archiError.set('');
      this.archi.set(await this.api.get('/export/a-reporter'));
    } catch (e) {
      this.archiError.set((e as Error).message);
    }
  }

  toggle(id: string): void {
    this.selected.update((s) => {
      const next = new Set(s);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  toggleAll(on: boolean): void {
    this.selected.set(on ? new Set(this.filtered().map((i) => i.brief.id)) : new Set());
  }

  toggleDetail(id: string): void {
    this.detail.set(this.detail() === id ? null : id);
  }

  startReject(id: string): void {
    this.comment.set('');
    this.rejecting.set(id);
  }

  async validateSelection(): Promise<void> {
    const ids = this.selectedIds();
    await this.run(async () => {
      const r = await this.api.post('/validation/validate', { ids });
      const refused = r.refused.length;
      this.message.set(`${r.validated} objet(s) validé(s)` + (refused ? `, ${refused} refusé(s) par le serveur (déjà validés ou introuvables).` : '.'));
      this.selected.set(new Set());
    });
  }

  async reject(event: Event, id: string): Promise<void> {
    event.preventDefault();
    await this.run(async () => {
      await this.api.post(`/validation/objects/${id}/reject`, { comment: this.comment() });
      this.message.set('Proposition refusée : l\'objet est repassé en brouillon avec votre commentaire.');
      this.rejecting.set(null);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await action();
      await this.load();
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
