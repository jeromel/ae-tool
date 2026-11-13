import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from './api';

/**
 * Matrice croisée entre deux types d'objets : un mode d'affichage commun aux espaces.
 * R : une relation directe lie les deux objets. V : l'un est imbriqué dans l'autre dans au moins une vue.
 */
@Component({
  selector: 'app-matrix-view',
  imports: [RouterLink],
  template: `
    @if (m(); as m) {
      @if (m.rows.length === 0) { <p class="muted" style="padding: 14px">Aucun lien entre ces deux types.</p> }
      <div class="scroll tall matrix" [style.--above]="above()">
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
      <p class="muted small" style="padding: 8px 14px; margin: 0">
        {{ m.rows.length }} × {{ m.columns.length }}, lignes et colonnes vides masquées.
        @if (relation()) { Ligne : source de la relation. Colonne : cible. } @else { R : relation directe. V : imbrication dans une vue. }
      </p>
    }
  `,
})
export class MatrixView {
  private api = inject(Api);
  rows = input.required<string>();
  cols = input.required<string>();
  nesting = input(true);
  /** Type ArchiMate auquel limiter la matrice (par exemple « Flow ») ; vide = toutes les relations. */
  relation = input('');
  above = input('330px');
  m = signal<any>(null);

  constructor() {
    effect(() => {
      const p = new URLSearchParams({ rows: this.rows(), cols: this.cols(), nesting: String(this.nesting()), relation: this.relation() });
      this.api.get(`/analysis/matrix?${p}`).then((m) => this.m.set(m));
    });
  }
}
