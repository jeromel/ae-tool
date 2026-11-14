import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Session } from '../session';

/** Connexion : identifiant et mot de passe en mode local, bouton Microsoft en mode Entra ID. */
@Component({
  template: `
    <div class="auth-box">
      <h1>Référentiel d'architecture</h1>
      <p class="lead">Connexion</p>
      @if (installed) { <div class="note">Administrateur créé : vous pouvez maintenant vous connecter.</div> }
      @if (failed) { <div class="error">La connexion Microsoft a échoué, ou le compte est désactivé. Réessayez ou contactez un administrateur.</div> }
      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      @if (session.mode() === 'entra') {
        <p><a class="btn" href="/api/auth/entra/login">Se connecter avec Microsoft</a></p>
      } @else {
        <form class="card form" (submit)="submit($event)">
          <label>Identifiant
            <input type="text" name="login" autocomplete="username" required [value]="login()" (input)="login.set($any($event.target).value)" />
          </label>
          <label>Mot de passe
            <input type="password" name="password" autocomplete="current-password" required [value]="password()" (input)="password.set($any($event.target).value)" />
          </label>
          <button type="submit" [disabled]="busy() || !login() || !password()">Se connecter</button>
        </form>
      }
    </div>
  `,
})
export class LoginPage {
  session = inject(Session);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  login = signal('');
  password = signal('');
  busy = signal(false);
  error = signal('');
  installed = this.route.snapshot.queryParamMap.has('installe');
  failed = this.route.snapshot.queryParamMap.has('erreur');

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.error.set('');
    try {
      await this.session.login(this.login().trim(), this.password());
      this.password.set('');
      await this.router.navigate([this.session.user()?.mustChangePassword ? '/compte' : '/']);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
