import { Component, inject, signal } from '@angular/core';
import { Api } from '../api';
import { MatrixView } from '../matrix-view';

@Component({
  imports: [MatrixView],
  template: `
    <h1>Matrices</h1>
    <p class="lead">
      Croisement de deux types d'objets. <b>R</b> : une relation directe les lie. <b>V</b> : l'un est imbriqué dans l'autre dans au moins une
      vue (lecture de type POS). Les lignes et colonnes vides sont masquées.
    </p>
    <div class="card flush">
      <div class="tools">
        <label>Lignes
          <select (change)="rows.set($any($event.target).value)">
            @for (t of types(); track t.key) { <option [value]="t.key" [selected]="rows() === t.key">{{ t.label }} ({{ t.count }})</option> }
          </select>
        </label>
        <label>Colonnes
          <select (change)="cols.set($any($event.target).value)">
            @for (t of types(); track t.key) { <option [value]="t.key" [selected]="cols() === t.key">{{ t.label }} ({{ t.count }})</option> }
          </select>
        </label>
        <label><input type="checkbox" checked (change)="nesting.set($any($event.target).checked)" /> Tenir compte de l'imbrication dans les vues</label>
      </div>
      <app-matrix-view [rows]="rows()" [cols]="cols()" [nesting]="nesting()" above="260px" />
    </div>
  `,
})
export class MatrixPage {
  private api = inject(Api);
  types = signal<any[]>([]);
  rows = signal('Capability');
  cols = signal('ApplicationComponent');
  nesting = signal(true);

  constructor() {
    this.api.get('/summary').then((s) => this.types.set(s.layers.flatMap((l: any) => l.types)));
  }
}
