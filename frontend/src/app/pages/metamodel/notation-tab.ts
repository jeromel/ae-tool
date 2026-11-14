import { Component, inject } from '@angular/core';
import { MetaStore } from './store';

/** Grille de notation d'urbanisation : critères, poids et seuils, repris du classeur d'urbanisation à chaque import. */
@Component({
  selector: 'app-notation-tab',
  template: `
    @let s = store.m().scoring;
    <div class="row">
      <div class="card">
        <h2>Grille de notation</h2>
        <table>
          <tr><th scope="col">Critère</th><th scope="col">Noté par</th><th scope="col" class="num">Sous-critères</th><th scope="col" class="num">Total des poids</th></tr>
          @for (c of s.criteria; track c.code) {
            <tr><td>{{ c.code }} · {{ c.label }}</td><td>{{ c.scoredBy }}</td><td class="num">{{ c.subCriteria.length }}</td><td class="num">{{ total(c) }}</td></tr>
          }
        </table>
        <p class="muted small">La grille et les seuils sont repris du classeur d'urbanisation à chaque import, ou modifiables par l'API (PUT /api/metamodel/scoring).</p>
      </div>
      <div class="card">
        <h2>Seuils</h2>
        <dl class="kv">
          <dt>Note 2 / 3 / 4 à partir de</dt><dd>{{ pct(s.thresholds.note2) }} / {{ pct(s.thresholds.note3) }} / {{ pct(s.thresholds.note4) }}</dd>
          <dt>Maîtrise technique</dt><dd>moyenne de {{ s.masteryCriteria.join(' et ') }}, seuil {{ s.thresholds.mastery }}</dd>
          <dt>Priorité</dt><dd>haute si U &gt; {{ s.thresholds.priorityHigh }}, moyenne si U ≥ {{ s.thresholds.priorityMedium }}</dd>
          <dt>Règle de garde</dt><dd>CM ≥ {{ s.thresholds.guardCm }} et besoin non maintenu : à instruire</dd>
        </dl>
      </div>
    </div>
  `,
})
export class NotationTab {
  protected readonly store = inject(MetaStore);

  protected total(criterion: any): number {
    return criterion.subCriteria.reduce((sum: number, s: any) => sum + s.weight, 0);
  }

  protected pct(value: number): string {
    return Math.round(value * 100) + ' %';
  }
}
