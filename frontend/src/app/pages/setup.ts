import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../api';
import { Session } from '../session';

/** Premier lancement : création de l'administrateur, protégée par le code d'installation affiché sur le serveur. */
@Component({
  template: `
    <div class="auth-box">
      <h1>Premier lancement</h1>
      <p class="lead">
        Aucun compte n'existe encore. Le code d'installation est affiché dans la console du serveur et écrit dans le fichier
        <b>code-installation.txt</b> du dossier de données ; il ne sert qu'une fois.
      </p>
      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      <form class="card form" (submit)="submit($event)">
        <label>Code d'installation
          <input type="text" name="code" autocomplete="off" required [value]="code()" (input)="code.set($any($event.target).value)" />
        </label>
        <label>Identifiant de l'administrateur
          <input type="text" name="login" autocomplete="username" required [value]="login()" (input)="login.set($any($event.target).value)" />
        </label>
        <label>Nom affiché
          <input type="text" name="name" autocomplete="name" required [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
        <label>Mot de passe <span class="muted small">(12 caractères au moins)</span>
          <input type="password" name="password" autocomplete="new-password" required [value]="password()" (input)="password.set($any($event.target).value)" />
        </label>
        <label>Confirmation du mot de passe
          <input type="password" name="confirm" autocomplete="new-password" required [value]="confirm()" (input)="confirm.set($any($event.target).value)" />
        </label>
        @if (mismatch()) { <div class="small" style="color: var(--bad)">Les deux mots de passe diffèrent.</div> }
        <button type="submit" [disabled]="busy() || !ready()">Créer l'administrateur</button>
      </form>
    </div>
  `,
})
export class SetupPage {
  private api = inject(Api);
  private session = inject(Session);
  private router = inject(Router);

  code = signal('');
  login = signal('');
  name = signal('');
  password = signal('');
  confirm = signal('');
  busy = signal(false);
  error = signal('');
  mismatch = computed(() => this.confirm() !== '' && this.confirm() !== this.password());
  ready = computed(() => !!(this.code() && this.login() && this.name() && this.password()) && this.password() === this.confirm());

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.post('/auth/setup', { code: this.code().trim(), login: this.login().trim(), name: this.name().trim(), password: this.password() });
      this.password.set('');
      this.confirm.set('');
      await this.session.refresh();
      await this.router.navigate(['/connexion'], { queryParams: { installe: 1 } });
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
