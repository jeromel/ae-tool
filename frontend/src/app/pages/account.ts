import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../api';
import { Session } from '../session';

/** Compte de l'utilisateur connecté : identité, rôle et changement de mot de passe. */
@Component({
  template: `
    <h1>Mon compte</h1>
    @if (session.user(); as u) {
      <p class="lead">{{ u.name }} · <span class="chip info">{{ u.role }}</span> <span class="muted small">identifiant {{ u.login }}</span></p>
      <p class="small">Accès en lecture : {{ u.readScope === 'metier' ? 'sans l\'infrastructure' : 'tout le référentiel' }}</p>
      @if (u.mustChangePassword) { <div class="note">Votre mot de passe est temporaire : choisissez-en un nouveau pour utiliser l'outil.</div> }
      @if (session.mode() === 'local') {
        <form class="card form" style="max-width: 460px" (submit)="submit($event)">
          <h2>Changer mon mot de passe</h2>
          @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
          @if (done()) { <div class="note">Mot de passe modifié.</div> }
          <label>Mot de passe actuel
            <input type="password" name="current" autocomplete="current-password" required [value]="current()" (input)="current.set($any($event.target).value)" />
          </label>
          <label>Nouveau mot de passe <span class="muted small">(12 caractères au moins)</span>
            <input type="password" name="next" autocomplete="new-password" required [value]="next()" (input)="next.set($any($event.target).value)" />
          </label>
          <label>Confirmation du nouveau mot de passe
            <input type="password" name="confirm" autocomplete="new-password" required [value]="confirm()" (input)="confirm.set($any($event.target).value)" />
          </label>
          @if (mismatch()) { <div class="small" style="color: var(--bad)">Les deux mots de passe diffèrent.</div> }
          <button type="submit" [disabled]="busy() || !ready()">Changer le mot de passe</button>
        </form>
      } @else if (session.mode() === 'entra') {
        <p class="muted">Votre mot de passe est géré par l'annuaire de l'entreprise (Microsoft Entra ID).</p>
      } @else {
        <p class="muted">Mode ouvert : aucune authentification, donc aucun mot de passe.</p>
      }
    }
  `,
})
export class AccountPage {
  private api = inject(Api);
  private router = inject(Router);
  session = inject(Session);

  current = signal('');
  next = signal('');
  confirm = signal('');
  busy = signal(false);
  error = signal('');
  done = signal(false);
  mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.next());
  ready = computed(() => !!(this.current() && this.next()) && this.next() === this.confirm());

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.error.set('');
    this.done.set(false);
    try {
      const wasTemporary = this.session.user()?.mustChangePassword;
      await this.api.post('/auth/password', { current: this.current(), next: this.next() });
      this.current.set('');
      this.next.set('');
      this.confirm.set('');
      await this.session.refresh();
      this.done.set(true);
      if (wasTemporary) await this.router.navigate(['/']);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
