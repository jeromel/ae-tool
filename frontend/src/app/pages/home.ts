import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

@Component({
  imports: [RouterLink, DatePipe],
  template: `
    <h1>{{ data()?.modelName ?? 'Référentiel' }}</h1>
    <p class="lead">Vision partagée du système d'information : objets, relations, vues, projets et portefeuille applicatif.</p>
    @if (data(); as d) {
      @if (d.objects === 0) {
        <div class="note">Le référentiel est vide. Commencez par <a routerLink="/imports">importer un modèle Archi</a>.</div>
      }
      <div class="stats">
        <a class="stat" routerLink="/referentiel"><b>{{ d.objects }}</b><span>objets</span></a>
        <div class="stat"><b>{{ d.relationships }}</b><span>relations</span></div>
        <a class="stat" routerLink="/vues"><b>{{ d.diagrams }}</b><span>vues</span></a>
        <a class="stat" routerLink="/projets"><b>{{ d.projects }}</b><span>projets</span></a>
        <a class="stat" routerLink="/portefeuille"><b>{{ d.applications }}</b><span>applications notées</span></a>
      </div>
      <div class="row">
        <div class="card">
          <h2>Objets par couche</h2>
          <table>
            @for (l of d.layers; track l.key) {
              @if (l.count > 0) {
                <tr>
                  <td style="width: 210px"><a routerLink="/referentiel" [queryParams]="{ couche: l.key }">{{ l.label }}</a></td>
                  <td class="num" style="width: 60px"><b>{{ l.count }}</b></td>
                  <td class="muted small">
                    @for (t of l.types; track t.key; let last = $last) {
                      {{ t.label }} ({{ t.count }}){{ last ? '' : ' · ' }}
                    }
                  </td>
                </tr>
              }
            }
          </table>
        </div>
        <div class="card" style="max-width: 420px">
          <h2>Dernières opérations</h2>
          @for (j of d.journal; track $index) {
            <div class="small" style="margin-bottom: 6px">
              <span class="muted">{{ j.at | date: 'dd/MM HH:mm' }} · {{ j.user }}</span><br />{{ j.action }}
            </div>
          } @empty {
            <span class="muted">Aucune opération.</span>
          }
        </div>
      </div>
      <div class="card">
        <h2>Qualité des données par type</h2>
        <div class="scroll" style="max-height: 320px">
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
    }
  `,
  styles: `a.stat { text-decoration: none; color: inherit; }`,
})
export class HomePage {
  private api = inject(Api);
  data = signal<any>(null);
  quality = signal<any[]>([]);

  constructor() {
    this.api.get('/summary').then((d) => this.data.set(d));
    this.api.get('/quality').then((q) => this.quality.set(q));
  }

  pct(part: number, total: number): string {
    return total ? Math.round((100 * part) / total) + ' %' : '–';
  }
}
