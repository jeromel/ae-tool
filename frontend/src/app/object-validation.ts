import { Component, input, output } from '@angular/core';

/** Emplacement réservé sur la fiche d'objet : bandeau de validation (lot « validation »). */
@Component({
  selector: 'app-object-validation',
  template: ``,
})
export class ObjectValidation {
  objectId = input.required<string>();
  /** Émis quand l'état de l'objet a changé : la fiche se recharge. */
  changed = output<void>();
}
