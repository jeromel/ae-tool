import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

@Component({
  imports: [RouterLink],
  template: `
    <h1>Matrices</h1>
    <p class="lead">
      Croisement de deux types d'objets. <b>R</b> : une relation directe les lie. <b>V</b> : l'un est imbriqué dans l'autre dans au moins une
      vue (lecture de type POS). Les lignes et colonnes vides sont masquées.
    </p>
    <div class="card">
      <div class="tools">
        <label>Lignes
          <select (change)="rows.set($any($event.target).value); load()">
            @for (t of types(); track t.key) { <option [value]="t.key" [selected]="rows() === t.key">{{ t.label }} ({{ t.count }})</option> }
          </select>
        </label>
        <label>Colonnes
          <select (change)="cols.set($any($event.target).value); load()">
            @for (t of types(); track t.key) { <option [value]="t.key" [selected]="cols() === t.key">{{ t.label }} ({{ t.count }})</option> }
          </select>
        </label>
        <label><input type="checkbox" checked (change)="nesting.set($any($event.target).checked); load()" /> Tenir compte de l'imbrication dans les vues</label>
        @if (m(); as m) { <span class="muted right">{{ m.rows.length }} × {{ m.columns.length }}</span> }
      </div>
      @if (m(); as m) {
        @if (m.rows.length === 0) { <span class="muted">Aucun lien entre ces deux types.</span> }
        <div class="scroll matrix">
          <table>
            <tr><th></th>@for (c of m.columns; track c.id) { <th class="col"><div [title]="c.name">{{ c.name }}</div></th> }</tr>
            @for (r of m.rows; track r.id; let i = $index) {
              <tr>
                <th class="rowh"><a [routerLink]="['/objets', r.id]">{{ r.name }}</a></th>
                @for (cell of m.cells[i]; track $index) { <td class="cell" [class.r]="cell.includes('R')" [class.v]="cell === 'V'">{{ cell }}</td> }
              </tr>
            }
          </table>
        </div>
      }
    </div>
  `,
})
export class MatrixPage {
  private api = inject(Api);
  types = signal<any[]>([]);
  rows = signal('Capability');
  cols = signal('ApplicationComponent');
  nesting = signal(true);
  m = signal<any>(null);

  constructor() {
    this.api.get('/summary').then((s) => {
      this.types.set(s.layers.flatMap((l: any) => l.types));
      this.load();
    });
  }

  load(): void {
    this.api.get(`/analysis/matrix?rows=${this.rows()}&cols=${this.cols()}&nesting=${this.nesting()}`).then((m) => this.m.set(m));
  }
}
