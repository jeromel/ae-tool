import { Injectable, computed, signal } from '@angular/core';

/** Rôles de l'outil, du moins au plus habilité (exigence EF-50). */
export type Role = 'lecteur' | 'contributeur' | 'valideur' | 'administrateur';
const ORDER: Role[] = ['lecteur', 'contributeur', 'valideur', 'administrateur'];

export interface SessionUser { login: string; name: string; role: Role; }

/**
 * Utilisateur connecté et ce que son rôle autorise. Les écrans s'en servent pour masquer ce qu'il ne peut pas faire ;
 * le contrôle qui fait foi reste celui du serveur.
 * En attendant le branchement de l'authentification, la session est celle d'un administrateur anonyme.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  readonly user = signal<SessionUser | null>({ login: 'anonyme', name: 'Anonyme', role: 'administrateur' });
  readonly role = computed<Role | null>(() => this.user()?.role ?? null);

  /** Vrai si le rôle de l'utilisateur vaut au moins le rôle demandé. */
  can(minimum: Role): boolean {
    const role = this.role();
    return role !== null && ORDER.indexOf(role) >= ORDER.indexOf(minimum);
  }
}
