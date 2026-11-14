import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api, Brief, SOURCE_LABELS } from '../api';
import { FieldChange, FieldChanges } from '../object-history';

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

    <div class="card flush">
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
  `,
  styles: `
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
  private api = inject(Api);
  sources = SOURCE_LABELS;

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
