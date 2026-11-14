import { Component, Injectable, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api } from './api';

/** Un champ modifié : valeur avant et valeur après (exigence EF-52). */
export interface FieldChange {
  field: string;
  before: string | null;
  after: string | null;
}

/** Une entrée du journal, telle que l'API la renvoie. */
export interface JournalItem {
  at: string;
  user: string;
  action: string;
  targetKind: string | null;
  targetId: string | null;
  detail: string | null;
  changes: FieldChange[] | null;
}

/** Tableau des champs modifiés d'une entrée : le champ, sa valeur avant et sa valeur après. Réutilisé par le journal. */
@Component({
  selector: 'app-field-changes',
  template: `
    <table class="changes">
      <tr><th>Champ</th><th>Avant</th><th>Après</th></tr>
      @for (c of changes(); track $index) {
        <tr>
          <td>{{ c.field }}</td>
          <td [class.none]="c.before === null"><span class="before">{{ c.before ?? 'vide' }}</span></td>
          <td [class.none]="c.after === null"><span class="after">{{ c.after ?? 'vide' }}</span></td>
        </tr>
      }
    </table>
  `,
  styles: `
    .changes { font-size: 13px; table-layout: fixed; width: 100%; }
    .changes th:first-child { width: 24%; }
    .changes th:nth-child(2), .changes th:nth-child(3) { width: 38%; }
    .changes th, .changes td { padding: 4px 10px; vertical-align: top; }
    .changes td:first-child { color: var(--muted); overflow-wrap: anywhere; }
    .changes td { white-space: pre-wrap; overflow-wrap: anywhere; }
    .before { background: #f9dedb; border-radius: 3px; padding: 1px 5px; }
    .after { background: #dcefe3; border-radius: 3px; padding: 1px 5px; }
    .none span { background: none; color: var(--muted); font-style: italic; }
  `,
})
export class FieldChanges {
  changes = input.required<FieldChange[]>();
}

/** Signal partagé : la bannière de validation le déclenche pour que l'historique se relise après une transition. */
@Injectable({ providedIn: 'root' })
export class HistoryRefresh {
  readonly tick = signal(0);
  bump(): void {
    this.tick.update((n) => n + 1);
  }
}

const PAGE = 8;

/** Carte « Historique » de la fiche d'objet : qui, quand, quoi, avec l'avant et l'après de chaque champ. */
@Component({
  selector: 'app-object-history',
  imports: [DatePipe, FieldChanges],
  template: `
    <div class="card">
      <div class="head">
        <h2>Historique</h2>
        <button type="button" class="ghost" (click)="load()">Actualiser</button>
      </div>
      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      @if (!error() && items().length === 0) { <p class="muted">{{ loaded() ? 'Aucune modification enregistrée pour cet objet.' : 'Chargement…' }}</p> }
      @for (e of shown(); track $index) {
        <div class="entry">
          <div class="line">
            <b>{{ e.action }}</b>
            <span class="muted small">par {{ e.user }} le {{ e.at | date: 'dd/MM/yyyy à HH:mm' }}</span>
          </div>
          @if (e.changes?.length) { <app-field-changes [changes]="e.changes!" /> }
          @else if (e.detail) { <div class="muted small">{{ e.detail }}</div> }
        </div>
      }
      @if (items().length > limit()) {
        <button type="button" class="ghost" (click)="limit.set(limit() + 20)">Voir plus ({{ items().length - limit() }} entrée(s) plus ancienne(s))</button>
      }
    </div>
  `,
  styles: `
    .head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px; }
    .head h2 { margin: 0; }
    .entry { padding: 10px 0; border-top: 1px solid var(--line); }
    .entry:first-of-type { border-top: 0; }
    .line { display: flex; gap: 12px; align-items: baseline; flex-wrap: wrap; margin-bottom: 4px; }
  `,
})
export class ObjectHistory {
  private api = inject(Api);
  private refresh = inject(HistoryRefresh);

  objectId = input.required<string>();
  /** Émis quand l'état de l'objet a changé : la fiche se recharge. L'historique, lui, ne modifie rien. */
  changed = output<void>();

  items = signal<JournalItem[]>([]);
  error = signal('');
  loaded = signal(false);
  limit = signal(PAGE);

  constructor() {
    // Relue quand on change d'objet, ou quand une transition de validation vient d'avoir lieu.
    effect(() => {
      this.objectId();
      this.refresh.tick();
      untracked(() => this.load());
    });
  }

  shown(): JournalItem[] {
    return this.items().slice(0, this.limit());
  }

  async load(): Promise<void> {
    try {
      this.items.set(await this.api.get<JournalItem[]>(`/objects/${this.objectId()}/history`));
      this.error.set('');
    } catch (e) {
      this.error.set((e as Error).message);
    }
    this.loaded.set(true);
  }
}
