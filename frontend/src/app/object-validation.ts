import { Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api } from './api';
import { Session } from './session';
import { HistoryRefresh } from './object-history';

interface ValidationState {
  status: string;
  archived: boolean;
  proposedBy: string | null;
  proposedAt: string | null;
  validatedBy: string | null;
  validatedAt: string | null;
  comment: string | null;
}

/** Bandeau d'état de validation d'un objet, avec les actions que le rôle de l'utilisateur permet. Le serveur reste le garant. */
@Component({
  selector: 'app-object-validation',
  imports: [DatePipe],
  template: `
    @if (state(); as s) {
      <div class="band" [class.ok]="s.status === 'Validé'" [class.wait]="s.status === 'Proposé'">
        <div class="text">
          @switch (s.status) {
            @case ('Validé') {
              <span class="chip ok">Validé</span>
              <span>Validé par <b>{{ s.validatedBy }}</b>{{ s.validatedAt ? ' le ' + (s.validatedAt | date: 'dd/MM/yyyy à HH:mm') : '' }}.</span>
            }
            @case ('Proposé') {
              <span class="chip warn">Proposé</span>
              <span>Proposé par <b>{{ s.proposedBy }}</b>{{ s.proposedAt ? ' le ' + (s.proposedAt | date: 'dd/MM/yyyy à HH:mm') : '' }} : en attente d'un valideur.</span>
            }
            @default {
              <span class="chip">Brouillon</span>
              <span>Cet objet n'a pas encore été proposé à la validation.</span>
            }
          }
        </div>
        @if (s.comment) { <div class="refusal"><b>Proposition refusée :</b> {{ s.comment }}</div> }
        @if (!s.archived) {
          <div class="actions">
            @if (s.status === 'Brouillon' && session.can('contributeur')) {
              <button type="button" [disabled]="busy()" (click)="propose()">Proposer à la validation</button>
            }
            @if (s.status !== 'Validé' && session.can('valideur')) {
              <button type="button" [disabled]="busy()" (click)="validate()">Valider</button>
            }
            @if (s.status === 'Proposé' && session.can('valideur') && !rejecting()) {
              <button type="button" class="ghost" [disabled]="busy()" (click)="rejecting.set(true)">Refuser…</button>
            }
          </div>
        }
        @if (rejecting()) {
          <form class="reject" (submit)="reject($event)">
            <label for="refus-{{ objectId() }}">Commentaire du refus (obligatoire) : dites à l'auteur ce qu'il doit corriger.</label>
            <textarea id="refus-{{ objectId() }}" rows="3" required [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
            <div>
              <button type="submit" [disabled]="busy() || !comment().trim()">Refuser la proposition</button>
              <button type="button" class="ghost" (click)="cancel()">Annuler</button>
            </div>
          </form>
        }
      </div>
    }
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
  `,
  styles: `
    .band { display: flex; flex-wrap: wrap; align-items: center; gap: 10px 16px; padding: 10px 14px; margin-bottom: 14px; background: #f6f8f8; border: 1px solid var(--line); border-left: 4px solid #9aa9b0; border-radius: 6px; }
    .band.ok { border-left-color: var(--ok); }
    .band.wait { border-left-color: var(--warn); background: #fffaf0; }
    .text { display: flex; align-items: center; gap: 10px; flex: 1; min-width: 260px; }
    .actions { display: flex; gap: 8px; }
    .refusal { flex-basis: 100%; background: #f9dedb; color: var(--bad); padding: 6px 10px; border-radius: 6px; white-space: pre-wrap; }
    .reject { flex-basis: 100%; display: flex; flex-direction: column; gap: 6px; }
    .reject textarea { width: 100%; box-sizing: border-box; }
    .reject div { display: flex; gap: 8px; }
  `,
})
export class ObjectValidation {
  private api = inject(Api);
  private history = inject(HistoryRefresh);
  session = inject(Session);

  objectId = input.required<string>();
  /** Émis quand l'état de l'objet a changé : la fiche se recharge. */
  changed = output<void>();

  state = signal<ValidationState | null>(null);
  error = signal('');
  busy = signal(false);
  rejecting = signal(false);
  comment = signal('');

  constructor() {
    // Relu au changement d'objet, et quand la fiche signale une modification : un objet validé que l'on modifie
    // sur le fond repasse à « proposé », le bandeau doit le montrer sans recharger la page.
    effect(() => {
      this.objectId();
      this.history.tick();
      untracked(() => this.load());
    });
  }

  async load(): Promise<void> {
    try {
      const d = await this.api.get(`/objects/${this.objectId()}`);
      const o = d.object;
      this.state.set({
        status: o.validationStatus, archived: o.archived, proposedBy: o.proposedBy, proposedAt: o.proposedAt,
        validatedBy: o.validatedBy, validatedAt: o.validatedAt, comment: o.validationComment,
      });
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  propose(): Promise<void> {
    return this.run(`/objects/${this.objectId()}/propose`, {});
  }

  validate(): Promise<void> {
    return this.run(`/validation/objects/${this.objectId()}/validate`, {});
  }

  async reject(event: Event): Promise<void> {
    event.preventDefault();
    await this.run(`/validation/objects/${this.objectId()}/reject`, { comment: this.comment() });
    if (!this.error()) this.cancel();
  }

  cancel(): void {
    this.rejecting.set(false);
    this.comment.set('');
  }

  private async run(path: string, body: unknown): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.post(path, body);
      // Le signal partagé fait relire ce bandeau et l'historique ; la fiche, prévenue, relit le reste.
      this.history.bump();
      this.changed.emit();
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
