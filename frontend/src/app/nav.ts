import { Injectable, computed, signal } from '@angular/core';
import { readPref, writePref } from './prefs';

/**
 * Menu latéral déplié ou réduit à un rail. L'utilisateur le replie à la main (choix mémorisé), et une page qui a besoin
 * de toute la largeur, comme une vue ouverte, le replie le temps de son affichage sans toucher à ce choix.
 */
@Injectable({ providedIn: 'root' })
export class NavState {
  private folded = signal(readPref('menu.replie', false));
  private forced = signal(false);
  closed = computed(() => this.folded() || this.forced());

  toggle(): void {
    const next = !this.closed();
    this.forced.set(false);
    this.folded.set(next);
    writePref('menu.replie', next);
  }

  /** Replie le menu en rail pour la page en cours (false : rend la main au choix de l'utilisateur). */
  force(rail: boolean): void {
    this.forced.set(rail);
  }
}
