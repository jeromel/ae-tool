import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Role, Session } from '../session';

/**
 * Onglets de l'administration. Les adresses de chaque page restent les mêmes : un ancien lien ouvre son onglet.
 * Rôle de chaque onglet : celui qu'exige le serveur (AccessRules.cs : /api/users, /api/admin, /api/sources et toute écriture
 * non listée, dont imports et métamodèle → administrateur ; lecture de /api/journal → valideur).
 */
export const ADMIN_TABS: { label: string; path: string; role: Role; also?: string[] }[] = [
  { label: 'Imports', path: '/imports', role: 'administrateur', also: ['/import-catalogue'] },
  { label: 'Sources externes', path: '/sources', role: 'administrateur' },
  { label: 'Métamodèle', path: '/metamodele', role: 'administrateur' },
  { label: 'Utilisateurs', path: '/utilisateurs', role: 'administrateur' },
  { label: 'Journal', path: '/journal', role: 'valideur' },
  { label: 'Sauvegardes', path: '/sauvegardes', role: 'administrateur' },
];

/** Vrai si l'adresse appartient à l'administration (entrée de menu active). */
export function isAdminUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path === '/administration' || ADMIN_TABS.some((t) => [t.path, ...(t.also ?? [])].some((p) => path === p || path.startsWith(p + '/')));
}

/**
 * Administration : une seule entrée de menu, ses pages en onglets. Un valideur n'a que le Journal : la barre d'onglets
 * disparaît, l'entrée ouvre directement la page.
 */
@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    @if (tabs().length > 1) {
      <nav class="tabs" aria-label="Administration">
        @for (t of tabs(); track t.path) {
          <a [routerLink]="t.path" routerLinkActive="on" [class.on]="isAlso(t)">{{ t.label }}</a>
        }
      </nav>
    }
    <router-outlet />
  `,
  styles: `
    .tabs { display: flex; flex-wrap: wrap; gap: 4px; margin: 0 0 18px; border-bottom: 1px solid var(--line); }
    .tabs a { padding: 8px 14px; border: 1px solid transparent; border-bottom: 0; border-radius: 8px 8px 0 0; color: var(--muted);
      text-decoration: none; font-weight: 600; margin-bottom: -1px; }
    .tabs a:hover { color: var(--ink); background: var(--accent-soft); }
    .tabs a.on { color: var(--ink); background: var(--card); border-color: var(--line); }
  `,
})
export class AdminShell {
  private session = inject(Session);
  private router = inject(Router);
  tabs = computed(() => ADMIN_TABS.filter((t) => this.session.can(t.role)));

  isAlso(t: (typeof ADMIN_TABS)[number]): boolean {
    const path = this.router.url.split(/[?#]/)[0];
    return (t.also ?? []).some((p) => path === p || path.startsWith(p + '/'));
  }
}
