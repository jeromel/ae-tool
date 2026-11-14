import { Component, computed, input, model } from '@angular/core';

/**
 * Champ « colonne de la source » : liste des colonnes de l'aperçu quand il y en a un, saisie libre sinon.
 * Une valeur déjà enregistrée mais absente de l'aperçu reste affichée, pour ne rien perdre en silence.
 */
@Component({
  selector: 'app-column-pick',
  template: `
    @if (columns().length) {
      <select [attr.aria-label]="label()" [value]="value()" (change)="value.set($any($event.target).value)">
        @if (optional() || !value()) { <option value="" [selected]="!value()">{{ optional() ? 'Aucune' : 'Choisir une colonne…' }}</option> }
        @for (c of options(); track c) { <option [value]="c" [selected]="c === value()">{{ c }}{{ columns().includes(c) ? '' : ' (absente de la source)' }}</option> }
      </select>
    } @else {
      <input type="text" [attr.aria-label]="label()" [value]="value()" placeholder="Nom de la colonne" (input)="value.set($any($event.target).value)" />
    }
  `,
  styles: `select, input { width: 100%; min-width: 150px; }`,
})
export class ColumnPick {
  value = model('');
  columns = input<string[]>([]);
  label = input('Colonne');
  optional = input(false);
  options = computed(() => (this.value() && !this.columns().includes(this.value()) ? [this.value(), ...this.columns()] : this.columns()));
}

/** Choix d'un type du métamodèle, groupé par couche. */
@Component({
  selector: 'app-type-pick',
  template: `
    <select [attr.aria-label]="label()" [value]="value()" (change)="value.set($any($event.target).value)">
      <option value="" [selected]="!value()">{{ empty() }}</option>
      @for (g of groups(); track g.key) {
        <optgroup [label]="g.label">
          @for (t of g.types; track t.key) { <option [value]="t.key" [selected]="t.key === value()">{{ t.label }}</option> }
        </optgroup>
      }
    </select>
  `,
  styles: `select { width: 100%; min-width: 200px; }`,
})
export class TypePick {
  value = model('');
  label = input('Type du métamodèle');
  empty = input('Choisir un type…');
  layers = input<{ key: string; label: string }[]>([]);
  types = input<{ key: string; label: string; layerKey: string }[]>([]);
  groups = computed(() => {
    const known = this.layers().map((l) => ({ key: l.key, label: l.label, types: this.types().filter((t) => t.layerKey === l.key) }));
    const others = this.types().filter((t) => !this.layers().some((l) => l.key === t.layerKey));
    return [...known, { key: '', label: 'Autres', types: others }].filter((g) => g.types.length)
      .map((g) => ({ ...g, types: [...g.types].sort((a, b) => a.label.localeCompare(b.label, 'fr')) }));
  });
}
