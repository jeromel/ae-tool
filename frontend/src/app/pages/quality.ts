import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

/** Qualité du référentiel : ce qui manque pour que les espaces répondent, avec un lien vers chaque liste à traiter. */
@Component({
  imports: [RouterLink],
  template: `
    <h1>Qualité du référentiel</h1>
    <p class="lead">Une vue générée ne vaut que par les relations et les propriétés du référentiel. Cette page liste ce qui manque, par ordre d'effet sur les espaces.</p>
    <div class="card">
      <h2>À traiter</h2>
      <table>
        <tr><th class="num">Nombre</th><th>Constat</th><th>Ce que cela empêche</th><th></th></tr>
        @for (t of todo(); track t.label) {
          <tr>
            <td class="num"><b [style.color]="t.count ? 'var(--bad)' : 'var(--ok)'">{{ t.count }}</b> <span class="muted small">/ {{ t.total }}</span></td>
            <td>{{ t.label }}</td><td class="muted">{{ t.effect }}</td>
            <td><a [routerLink]="t.to" [queryParams]="{ question: t.question }">Voir la liste</a></td>
          </tr>
        }
      </table>
    </div>
    <div class="card flush">
      <div class="tools"><h2 style="margin: 0">Complétude par type d'objet</h2></div>
      <div class="scroll tall" style="--above: 520px">
        <table>
          <tr><th>Type</th><th class="num">Objets</th><th class="num">Documentés</th><th class="num">Avec propriétés</th><th class="num">Sans relation</th><th class="num">Dans aucune vue</th></tr>
          @for (q of quality(); track q.typeKey) {
            <tr>
              <td>{{ q.label }}</td><td class="num">{{ q.count }}</td>
              <td class="num">{{ pct(q.documented, q.count) }}</td><td class="num">{{ pct(q.withProperties, q.count) }}</td>
              <td class="num">{{ q.orphans }}</td><td class="num">{{ q.notInDiagram }}</td>
            </tr>
          }
        </table>
      </div>
    </div>
  `,
})
export class QualityPage {
  private api = inject(Api);
  quality = signal<any[]>([]);
  private applications = signal<any[]>([]);
  private flows = signal<any[]>([]);
  private data = signal<any[]>([]);

  todo = computed(() => {
    const apps = this.applications();
    const flows = this.flows();
    const data = this.data();
    return [
      { count: apps.filter((a) => a.relations === 0).length, total: apps.length, label: 'Applications sans aucune relation',
        effect: 'Elles n\'apparaissent dans aucune analyse d\'impact ni aucun contexte.', to: '/applications', question: 'isolees' },
      { count: apps.filter((a) => a.capabilities === 0).length, total: apps.length, label: 'Applications rattachées à aucune capacité',
        effect: 'La carte des capacités ne les compte pas, et les doublons ne ressortent pas.', to: '/applications', question: 'sans-capacite' },
      { count: flows.filter((f) => !f.protocol).length, total: flows.length, label: 'Flux sans protocole renseigné',
        effect: 'Pas de pastille de protocole sur les vues, pas de lecture cybersécurité des échanges.', to: '/flux', question: 'sans-protocole' },
      { count: flows.filter((f) => !f.encryption).length, total: flows.length, label: 'Flux sans chiffrement renseigné',
        effect: 'Impossible de distinguer un flux en clair d\'un flux non documenté.', to: '/flux', question: 'sans-chiffrement' },
      { count: data.filter((d) => d.applications === 0).length, total: data.length, label: 'Données utilisées par aucune application',
        effect: 'On ne sait pas où la donnée est produite ni lue.', to: '/donnees', question: 'orphelines' },
    ];
  });

  constructor() {
    this.api.get('/quality').then((q) => this.quality.set(q));
    this.api.get('/spaces/applications').then((r) => this.applications.set(r));
    this.api.get('/spaces/flows').then((r) => this.flows.set(r));
    this.api.get('/spaces/data').then((r) => this.data.set(r));
  }

  pct(part: number, total: number): string {
    return total ? Math.round((100 * part) / total) + ' %' : '–';
  }
}
