import { Component, input, output } from '@angular/core';

/** Emplacement réservé sur la fiche d'objet : historique (lot « validation »). */
@Component({
  selector: 'app-object-history',
  template: ``,
})
export class ObjectHistory {
  objectId = input.required<string>();
  /** Émis quand l'état de l'objet a changé : la fiche se recharge. */
  changed = output<void>();
}
