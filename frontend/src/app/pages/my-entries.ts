import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api, Brief } from '../api';

interface Entry {
  brief: Brief;
  touchedAt: string | null;
  proposedAt: string | null;
  validatedAt: string | null;
  validatedBy: string | null;
  validationComment: string | null;
  refusedBy: string | null;
  refusedAt: string | null;
}
interface Entries { refused: Entry[]; toPropose: Entry[]; pending: Entry[]; recentlyValidated: Entry[]; }

/**
 * « Mes saisies » (maquette de Camille, 27/11) : ce que le contributeur a créé ou modifié, et où en est la validation.
 * Ce qui demande une action vient d'abord : refusées, puis à proposer, puis en attente ; les validées sont repliées.
 * Un groupe vide ne s'affiche pas.
 */
@Component({
  imports: [RouterLink, DatePipe],
  styles: `
    .group { margin-bottom: 16px; }
    .group h2 { margin: 0 0 2px; }
    .group .why { margin: 0 0 8px; }
    .line { display: flex; gap: 10px; align-items: baseline; flex-wrap: wrap; padding: 8px 0; border-top: 1px solid var(--line); }
    .line:first-of-type { border-top: 0; }
    .line .what { flex: 1; min-width: 260px; }
    .line .acts { display: flex; gap: 10px; align-items: baseline; }
    .comment { margin: 4px 0 0; font-style: italic; }
    .refused .what b::before { content: '⚠ '; }
    summary { cursor: pointer; color: var(--accent); font-weight: 600; }
    button.link { background: none; border: 0; padding: 0; color: var(--accent); font-weight: 500; text-decoration: underline; cursor: pointer; }
    button.link:hover { background: none; }
  `,
  template: `
    <div class="tools">
      <h1 style="margin: 0">Mes saisies</h1>
      <a class="btn right" routerLink="/objets/nouveau">Nouvel objet</a>
    </div>
    <p class="lead">Ce que vous avez créé ou modifié, et où en est la validation.</p>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    @if (message()) { <div class="note" role="status">{{ message() }}</div> }

    @if (!data()) {
      @if (!error()) { <p class="muted">Chargement de vos saisies…</p> }
    } @else if (empty()) {
      <div class="card">
        <p>Vous n'avez encore rien saisi.</p>
        <a class="btn" routerLink="/objets/nouveau">Nouvel objet</a>
      </div>
    } @else {
      @if (data()!.refused.length) {
        <section class="card group refused">
          <h2>Refusées ({{ data()!.refused.length }})</h2>
          <p class="muted small why">Un valideur vous demande une correction.</p>
          @for (e of data()!.refused; track e.brief.id) {
            <div class="line">
              <div class="what">
                <b>{{ e.brief.name || '(sans nom)' }}</b> · {{ e.brief.typeLabel }}
                @if (e.refusedAt) { · <span class="muted small">refusée le {{ e.refusedAt | date: 'dd/MM/yyyy' }}@if (e.refusedBy) { par {{ e.refusedBy }} }</span> }
                <p class="comment">« {{ e.validationComment }} »</p>
              </div>
              <div class="acts"><a [routerLink]="['/objets', e.brief.id]" [queryParams]="{ modifier: 1 }">Corriger →</a></div>
            </div>
          }
        </section>
      }
      @if (data()!.toPropose.length) {
        <section class="card group">
          <h2>À proposer ({{ data()!.toPropose.length }})</h2>
          <p class="muted small why">Brouillons que personne ne voit encore.</p>
          @for (e of data()!.toPropose; track e.brief.id) {
            <div class="line">
              <div class="what"><b>{{ e.brief.name || '(sans nom)' }}</b> · {{ e.brief.typeLabel }}
                @if (e.touchedAt) { · <span class="muted small">modifiée le {{ e.touchedAt | date: 'dd/MM/yyyy' }}</span> }</div>
              <div class="acts">
                <button type="button" class="link" [disabled]="busy()" (click)="propose(e)">Proposer</button>
                <a [routerLink]="['/objets', e.brief.id]">Ouvrir</a>
              </div>
            </div>
          }
        </section>
      }
      @if (data()!.pending.length) {
        <section class="card group">
          <h2>En attente d'un valideur ({{ data()!.pending.length }})</h2>
          @for (e of data()!.pending; track e.brief.id) {
            <div class="line">
              <div class="what"><b>{{ e.brief.name || '(sans nom)' }}</b> · {{ e.brief.typeLabel }}
                @if (e.proposedAt) { · <span class="muted small">proposée le {{ e.proposedAt | date: 'dd/MM/yyyy' }}</span> }</div>
              <div class="acts"><a [routerLink]="['/objets', e.brief.id]">Ouvrir</a></div>
            </div>
          }
        </section>
      }
      @if (data()!.recentlyValidated.length) {
        <details class="card group">
          <summary>Validées récemment ({{ data()!.recentlyValidated.length }} {{ data()!.recentlyValidated.length > 1 ? 'dernières' : 'dernière' }})</summary>
          @for (e of data()!.recentlyValidated; track e.brief.id) {
            <div class="line">
              <div class="what"><b>{{ e.brief.name || '(sans nom)' }}</b> · {{ e.brief.typeLabel }}
                @if (e.validatedAt) { · <span class="muted small">validée le {{ e.validatedAt | date: 'dd/MM/yyyy' }}@if (e.validatedBy) { par {{ e.validatedBy }} }</span> }</div>
              <div class="acts"><a [routerLink]="['/objets', e.brief.id]">Ouvrir</a></div>
            </div>
          }
        </details>
      }
    }
  `,
})
export class MyEntriesPage {
  private api = inject(Api);
  data = signal<Entries | null>(null);
  error = signal('');
  message = signal('');
  busy = signal(false);
  empty = computed(() => {
    const d = this.data();
    return !!d && d.refused.length + d.toPropose.length + d.pending.length + d.recentlyValidated.length === 0;
  });

  constructor() {
    this.load();
  }

  async load(): Promise<void> {
    try {
      this.data.set(await this.api.get('/mes-saisies'));
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  /** « Proposer » sur la ligne, sans ouvrir la fiche. */
  async propose(e: Entry): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await this.api.post(`/objects/${e.brief.id}/propose`, {});
      this.message.set(`« ${e.brief.name} » est proposée : un valideur va la relire.`);
      await this.load();
    } catch (err) {
      this.error.set((err as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
