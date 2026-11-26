import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api } from '../api';
import { ROLES, Role, Session } from '../session';

interface UserRow {
  id: string;
  login: string;
  name: string;
  role: Role;
  disabled: boolean;
  origin: 'local' | 'entra';
  lastLoginAt: string | null;
  lockedUntil: string | null;
  mustChangePassword: boolean;
  /** Accès en lecture appliqué (décision 012) : « metier » = sans l'infrastructure ; ne se règle que pour un lecteur. */
  readScope: 'tout' | 'metier';
}

/** Administration des comptes : création, rôle, désactivation, réinitialisation du mot de passe. */
@Component({
  imports: [DatePipe],
  template: `
    <h1>Utilisateurs</h1>
    <p class="lead">
      @if (session.mode() === 'entra') {
        L'annuaire authentifie, l'outil habilite : un compte est créé à la première connexion Microsoft avec le rôle par défaut. Vous pouvez aussi le préparer ici.
      } @else {
        Comptes de l'outil et rôles. Le dernier administrateur actif ne peut être ni rétrogradé ni désactivé.
      }
    </p>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    @if (temporary(); as t) {
      <div class="note">
        Mot de passe temporaire de <b>{{ t.login }}</b> : <code>{{ t.password }}</code>
        <button type="button" class="ghost" style="margin-left: 10px" (click)="copy(t.password)">{{ copied() ? 'Copié' : 'Copier' }}</button>
        <button type="button" class="ghost" style="margin-left: 6px" (click)="temporary.set(null)">Fermer</button>
        <div class="small muted">Il ne sera plus affiché : transmettez-le à l'utilisateur, qui devra le changer à sa première connexion.</div>
      </div>
    }
    <form class="card" (submit)="create($event)">
      <h2>Nouveau compte</h2>
      <div class="tools" style="margin-bottom: 0">
        <input type="text" placeholder="Identifiant" aria-label="Identifiant" required [value]="login()" (input)="login.set($any($event.target).value)" />
        <input type="text" placeholder="Nom affiché" aria-label="Nom affiché" required [value]="name()" (input)="name.set($any($event.target).value)" />
        <select aria-label="Rôle" [value]="role()" (change)="role.set($any($event.target).value)">
          @for (r of roles; track r) { <option [value]="r" [selected]="r === role()">{{ r }}</option> }
        </select>
        @if (role() === 'lecteur') {
          <select aria-label="Accès en lecture" (change)="readScope.set($any($event.target).value)">
            @for (s of scopes; track s.key) { <option [value]="s.key" [selected]="s.key === readScope()">{{ s.label }}</option> }
          </select>
        }
        <button type="submit" [disabled]="busy() || !login() || !name()">Créer le compte</button>
      </div>
    </form>
    <div class="card flush">
      <div class="scroll">
        <table>
          <tr><th>Identifiant</th><th>Nom</th><th>Rôle</th><th>Accès en lecture</th><th>Origine</th><th>État</th><th>Dernière connexion</th><th></th></tr>
          @for (u of users(); track u.id) {
            <tr>
              <td>{{ u.login }}</td>
              <td>{{ u.name }}</td>
              <td>
                <select [attr.aria-label]="'Rôle de ' + u.login" (change)="setRole(u, $any($event.target).value)">
                  @for (r of roles; track r) { <option [value]="r" [selected]="r === u.role">{{ r }}</option> }
                </select>
              </td>
              <td>
                <!-- Le périmètre n'existe que pour un lecteur (décision 012) : pour les autres rôles, un texte fixe. -->
                @if (u.role === 'lecteur') {
                  <select [attr.aria-label]="'Accès en lecture de ' + u.login" (change)="setScope(u, $any($event.target).value)">
                    @for (s of scopes; track s.key) { <option [value]="s.key" [selected]="s.key === u.readScope">{{ s.label }}</option> }
                  </select>
                } @else { <span class="muted small">Tout le référentiel</span> }
              </td>
              <td class="muted small">{{ u.origin === 'entra' ? 'Microsoft Entra ID' : 'Local' }}</td>
              <td>
                @if (u.disabled) { <span class="chip bad">Désactivé</span> }
                @else if (u.lockedUntil) { <span class="chip warn">Verrouillé</span> }
                @else if (u.mustChangePassword) { <span class="chip info">Mot de passe temporaire</span> }
                @else { <span class="chip ok">Actif</span> }
              </td>
              <td class="muted small">{{ u.lastLoginAt ? (u.lastLoginAt | date: 'dd/MM/yyyy HH:mm') : 'jamais' }}</td>
              <td class="actions">
                @if (u.origin === 'local') {
                  <button type="button" class="ghost" (click)="reset(u)">Réinitialiser le mot de passe</button>
                }
                <button type="button" class="ghost" style="margin-left: 6px" (click)="toggle(u)">{{ u.disabled ? 'Réactiver' : 'Désactiver' }}</button>
              </td>
            </tr>
          }
        </table>
      </div>
    </div>
  `,
})
export class UsersPage {
  private api = inject(Api);
  session = inject(Session);
  readonly roles = ROLES;

  users = signal<UserRow[]>([]);
  error = signal('');
  busy = signal(false);
  login = signal('');
  name = signal('');
  role = signal<Role>('lecteur');
  /** Un nouveau lecteur est créé sans l'infrastructure (décision 012, point 2). */
  readScope = signal<'tout' | 'metier'>('metier');
  /** Libellés de la maquette : ce que voit la personne, pas le nom interne du réglage. */
  readonly scopes = [{ key: 'metier', label: 'Sans l\'infrastructure' }, { key: 'tout', label: 'Tout le référentiel' }] as const;
  temporary = signal<{ login: string; password: string } | null>(null);
  copied = signal(false);

  constructor() {
    this.load();
  }

  private async load(): Promise<void> {
    await this.run(async () => this.users.set(await this.api.get<UserRow[]>('/users')));
  }

  /** Exécute une action en affichant l'erreur du serveur telle quelle (message déjà rédigé pour l'utilisateur). */
  private async run(action: () => Promise<unknown>): Promise<void> {
    this.error.set('');
    try {
      await action();
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  async create(event: Event): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    await this.run(async () => {
      const r = await this.api.post<{ user: UserRow; temporaryPassword: string | null }>('/users', {
        login: this.login().trim(),
        name: this.name().trim(),
        role: this.role(),
        readScope: this.role() === 'lecteur' ? this.readScope() : null,
      });
      this.readScope.set('metier');
      this.temporary.set(r.temporaryPassword ? { login: r.user.login, password: r.temporaryPassword } : null);
      this.login.set('');
      this.name.set('');
      await this.load();
    });
    this.busy.set(false);
  }

  async setRole(u: UserRow, role: Role): Promise<void> {
    // Passer un lecteur sans l'infrastructure à un autre rôle élargit son accès : l'écran le dit (maquette périmètre, §5).
    if (u.role === 'lecteur' && u.readScope === 'metier' && role !== 'lecteur'
        && !confirm(`Un ${role} voit tout le référentiel, infrastructure comprise. Donner ce rôle à ${u.login} ?`)) { await this.load(); return; }
    await this.update(u, { role });
  }

  async setScope(u: UserRow, readScope: 'tout' | 'metier'): Promise<void> {
    await this.update(u, { readScope });
  }

  async toggle(u: UserRow): Promise<void> {
    await this.update(u, { disabled: !u.disabled });
  }

  private async update(u: UserRow, change: Partial<Pick<UserRow, 'role' | 'disabled' | 'readScope'>>): Promise<void> {
    // Le périmètre n'est envoyé que s'il change : un compte repassé lecteur retrouve le sien, ou « sans l'infrastructure ».
    await this.run(() => this.api.put('/users/' + u.id, { name: u.name, role: u.role, disabled: u.disabled, readScope: null, ...change }));
    await this.load(); // relit aussi après un refus, pour rétablir la valeur affichée dans la liste
  }

  async reset(u: UserRow): Promise<void> {
    if (!confirm(`Réinitialiser le mot de passe de ${u.login} ? L'ancien mot de passe cessera de fonctionner.`)) return;
    await this.run(async () => {
      const r = await this.api.post<{ temporaryPassword: string }>(`/users/${u.id}/reset-password`, {});
      this.temporary.set({ login: u.login, password: r.temporaryPassword });
      this.copied.set(false);
      await this.load();
    });
  }

  async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
    } catch {
      this.error.set('Copie impossible : sélectionnez le mot de passe et copiez-le à la main.');
    }
  }
}
