import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api } from '../api';
import { FieldChanges, JournalItem } from '../object-history';

const PAGE = 50;

/** Journal des modifications : qui, quand, quoi, avec l'avant et l'après. Filtrable et paginé. */
@Component({
  imports: [DatePipe, RouterLink, FieldChanges],
  template: `
    <h1>Journal</h1>
    <p class="lead">Toutes les actions enregistrées, de la plus récente à la plus ancienne. Cliquez sur une ligne pour voir l'avant et l'après.</p>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    <div class="card flush">
      <form class="tools" (submit)="apply($event)">
        <select aria-label="Auteur" [value]="user()" (change)="user.set($any($event.target).value)">
          <option value="">Tous les auteurs</option>
          @for (u of users(); track u) { <option [value]="u" [selected]="u === user()">{{ u }}</option> }
        </select>
        <select aria-label="Nature" [value]="kind()" (change)="kind.set($any($event.target).value)">
          <option value="">Toutes les natures</option>
          @for (k of kinds(); track k) { <option [value]="k" [selected]="k === kind()">{{ k }}</option> }
        </select>
        <input type="search" placeholder="Texte (action, champ, valeur…)" aria-label="Texte recherché" [value]="text()" (input)="text.set($any($event.target).value)" />
        <label class="small">Du <input type="date" [value]="from()" (change)="from.set($any($event.target).value)" /></label>
        <label class="small">au <input type="date" [value]="to()" (change)="to.set($any($event.target).value)" /></label>
        <button type="submit">Filtrer</button>
        <button type="button" class="ghost" (click)="reset()">Réinitialiser</button>
      </form>
      <div class="scroll tall">
        <table>
          <tr><th></th><th>Date</th><th>Auteur</th><th>Nature</th><th>Action</th><th>Cible</th></tr>
          @for (e of items(); track $index) {
            <tr class="jrow" [class.open]="open() === $index" (click)="toggle($index)">
              <td class="caret" [attr.aria-label]="open() === $index ? 'Replier' : 'Déplier'">
                <button type="button" class="caret-btn" [attr.aria-expanded]="open() === $index" (click)="toggle($index); $event.stopPropagation()">{{ open() === $index ? '▾' : '▸' }}</button>
              </td>
              <td class="small nowrap">{{ e.at | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
              <td>{{ e.user }}</td>
              <td>@if (e.targetKind) { <span class="chip">{{ e.targetKind }}</span> }</td>
              <td>{{ e.action }}</td>
              <td class="small">
                @if (e.targetKind === 'objet' && e.targetId) { <a [routerLink]="['/objets', e.targetId]" (click)="$event.stopPropagation()">{{ e.detail || e.targetId }}</a> }
                @else { {{ e.detail }} }
              </td>
            </tr>
            @if (open() === $index) {
              <tr class="sub"><td></td><td colspan="5">
                @if (e.changes?.length) { <app-field-changes [changes]="e.changes!" /> }
                @else { <span class="muted small">Aucun champ modifié pour cette action. {{ e.detail ? 'Détail : ' + e.detail : '' }}</span> }
              </td></tr>
            }
          }
          @if (items().length === 0) { <tr><td colspan="6" class="muted">{{ loaded() ? 'Aucune entrée ne correspond à ces critères.' : 'Chargement…' }}</td></tr> }
        </table>
      </div>
      <div class="tools pager">
        <span class="muted small">{{ total() === 0 ? 'Aucune entrée' : (offset() + 1) + ' à ' + (offset() + items().length) + ' sur ' + total() }}</span>
        <button type="button" class="ghost" [disabled]="offset() === 0" (click)="go(offset() - size)">Précédentes</button>
        <button type="button" class="ghost" [disabled]="offset() + size >= total()" (click)="go(offset() + size)">Suivantes</button>
      </div>
    </div>
  `,
  styles: `
    .nowrap { white-space: nowrap; }
    .jrow { cursor: pointer; }
    .jrow:hover td, .jrow.open td { background: #f1f6f7; }
    .caret { width: 1%; padding: 0 0 0 6px; }
    .caret-btn { background: none; border: 0; color: var(--muted); padding: 2px 6px; font-weight: 400; }
    .caret-btn:hover { background: none; color: var(--ink); }
    .sub td { background: #f6f8f8; }
    .pager { border-top: 1px solid var(--line); border-bottom: 0; }
    .pager span { margin-right: auto; }
    input[type=date] { font: inherit; padding: 5px 8px; border: 1px solid #b9c5ca; border-radius: 6px; }
  `,
})
export class JournalPage {
  private api = inject(Api);
  readonly size = PAGE;

  items = signal<JournalItem[]>([]);
  users = signal<string[]>([]);
  kinds = signal<string[]>([]);
  total = signal(0);
  offset = signal(0);
  loaded = signal(false);
  error = signal('');
  open = signal<number | null>(null);

  user = signal('');
  kind = signal('');
  text = signal('');
  from = signal('');
  to = signal('');

  constructor() {
    this.load();
  }

  apply(event: Event): void {
    event.preventDefault();
    this.go(0);
  }

  reset(): void {
    [this.user, this.kind, this.text, this.from, this.to].forEach((s) => s.set(''));
    this.go(0);
  }

  toggle(i: number): void {
    this.open.set(this.open() === i ? null : i);
  }

  go(offset: number): void {
    this.offset.set(Math.max(0, offset));
    this.load();
  }

  async load(): Promise<void> {
    const q = new URLSearchParams({ limit: String(PAGE), offset: String(this.offset()) });
    for (const [k, v] of [['user', this.user()], ['kind', this.kind()], ['q', this.text().trim()], ['from', this.from()], ['to', this.to()]]) if (v) q.set(k, v);
    try {
      const r = await this.api.get(`/journal?${q}`);
      this.items.set(r.items);
      this.total.set(r.total);
      this.users.set(r.users);
      this.kinds.set(r.kinds);
      this.open.set(null);
      this.error.set('');
    } catch (e) {
      this.error.set((e as Error).message);
    }
    this.loaded.set(true);
  }
}
