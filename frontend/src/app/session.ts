import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from './api';

/** Rôles de l'outil, du moins au plus habilité (exigence EF-50). */
export type Role = 'lecteur' | 'contributeur' | 'valideur' | 'administrateur';
export const ROLES: Role[] = ['lecteur', 'contributeur', 'valideur', 'administrateur'];

export type AuthMode = 'local' | 'entra' | 'ouvert';

export interface SessionUser {
  login: string;
  name: string;
  role: Role;
  mustChangePassword: boolean;
}

interface SessionPayload {
  mode: AuthMode;
  setupRequired: boolean;
  authenticated: boolean;
  user: SessionUser | null;
  csrfToken: string | null;
}

/**
 * Utilisateur connecté et ce que son rôle autorise. Les écrans s'en servent pour masquer ce qu'il ne peut pas faire ;
 * le contrôle qui fait foi reste celui du serveur. L'état vient de GET /api/auth/session, chargé au démarrage.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private api = inject(Api);
  private router = inject(Router);

  readonly user = signal<SessionUser | null>(null);
  readonly mode = signal<AuthMode>('local');
  readonly setupRequired = signal(false);
  readonly role = computed<Role | null>(() => this.user()?.role ?? null);

  constructor() {
    // Session expirée en cours d'usage : on oublie l'utilisateur et on renvoie vers la connexion.
    this.api.onUnauthorized = () => {
      this.user.set(null);
      this.api.csrfToken = null;
      this.router.navigate(['/connexion']);
    };
  }

  /** Vrai si le rôle de l'utilisateur vaut au moins le rôle demandé. */
  can(minimum: Role): boolean {
    const role = this.role();
    return role !== null && ROLES.indexOf(role) >= ROLES.indexOf(minimum);
  }

  /** Relit la session auprès du serveur (démarrage, après déconnexion, après changement de mot de passe). */
  async refresh(): Promise<void> {
    this.apply(await this.api.get<SessionPayload>('/auth/session'));
  }

  async login(login: string, password: string): Promise<void> {
    this.apply(await this.api.post<SessionPayload>('/auth/login', { login, password }));
  }

  async logout(): Promise<void> {
    await this.api.post('/auth/logout', {});
    await this.refresh();
    this.router.navigate(['/connexion']);
  }

  private apply(payload: SessionPayload): void {
    this.mode.set(payload.mode);
    this.setupRequired.set(payload.setupRequired);
    this.user.set(payload.authenticated ? payload.user : null);
    this.api.csrfToken = payload.csrfToken;
  }
}
