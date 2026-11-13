import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../api';

/** Portefeuille applicatif : une ligne par application, notation recalculée par l'outil selon la grille du métamodèle. */
@Component({
  template: `
    <h1>Portefeuille applicatif</h1>
    <p class="lead">
      Notation d'urbanisation : criticité métier (CM) et critères notés sur preuves. Une preuve « NC » ne vaut jamais zéro : elle bloque la
      note, donc la décision calculée.
    </p>
    <div class="stats">
      <div class="stat"><b>{{ rows().length }}</b><span>applications recensées</span></div>
      <div class="stat"><b>{{ active().length }}</b><span>actives</span></div>
      <div class="stat" [class.alert]="keyPerson() > 0"><b>{{ keyPerson() }}</b><span>dépendent d'une personne clé</span></div>
      <div class="stat"><b>{{ incomplete() }}</b><span>notations incomplètes</span></div>
      <div class="stat"><b>{{ toInstruct() }}</b><span>« À instruire »</span></div>
      <div class="stat"><b>{{ notInArchi() }}</b><span>absentes du modèle Archi</span></div>
    </div>

    <div class="card">
      <h2>Quadrant d'urbanisation</h2>
      <div class="quadrant">
        @for (q of quadrants; track q.key) {
          <div>
            <h3>{{ q.key }} <span class="muted">· {{ q.hint }}</span></h3>
            @for (r of inQuadrant(q.key); track r.id) {
              <span class="chip" style="margin: 0 4px 4px 0">{{ r.name }}</span>
            } @empty {
              <span class="muted small">Aucune application placée.</span>
            }
          </div>
        }
      </div>
      @if (incomplete() > 0) {
        <p class="muted small" style="margin: 10px 0 0">
          {{ incomplete() }} application(s) ne sont pas placées : il leur manque des preuves, le besoin métier ou les déclencheurs rouges.
        </p>
      }
    </div>

    <div class="card">
      <div class="tools">
        <input type="search" placeholder="Filtrer par nom ou domaine" (input)="filter.set($any($event.target).value)" />
        <label><input type="checkbox" (change)="onlyIncomplete.set($any($event.target).checked)" /> Notations incomplètes seulement</label>
        <span class="muted right">{{ shown().length }} ligne(s) · cliquer une ligne pour noter</span>
      </div>
      <div class="scroll">
        <table>
          <tr>
            <th>ID</th><th>Application</th><th>Domaine</th><th>État</th><th class="num">CM</th><th>Besoin</th>
            @for (c of grid()?.criteria ?? []; track c.code) { <th class="num" [title]="c.label">{{ c.code }}</th> }
            <th class="num">Maîtrise</th><th>Décision calculée</th><th>Décision retenue</th><th>Priorité</th><th>Contrôle</th>
          </tr>
          @for (r of shown(); track r.id) {
            <tr class="click" (click)="open(r.id)">
              <td>{{ r.urbaId }}</td>
              <td><b>{{ r.name }}</b> @if (!r.inArchi) { <span class="chip warn" title="Absente du modèle Archi">hors Archi</span> }</td>
              <td>{{ r.domain }}</td><td>{{ r.state }}</td><td class="num">{{ r.cm ?? '–' }}</td><td>{{ r.need }}</td>
              @for (c of grid()?.criteria ?? []; track c.code) { <td class="num">{{ r.result.notes[c.code] ?? '–' }}</td> }
              <td class="num">{{ r.result.mastery ?? '–' }}</td>
              <td>@if (r.result.computedDecision) { <span class="chip" [class.bad]="r.result.computedDecision === 'À instruire'">{{ r.result.computedDecision }}</span> }</td>
              <td>{{ r.retainedDecision }}</td>
              <td>@if (r.result.priority) { <span class="chip" [class.bad]="r.result.priority === 'Haute'" [class.warn]="r.result.priority === 'Moyenne'">{{ r.result.priority }}</span> }</td>
              <td class="small" [title]="r.result.missing.join(' ; ')">
                <span class="chip" [class.ok]="r.result.control === 'Conforme au calcul'" [class.warn]="r.result.control !== 'Conforme au calcul' && r.result.control !== 'Sans objet'">{{ r.result.control }}</span>
              </td>
            </tr>
          }
        </table>
      </div>
    </div>
  `,
})
export class PortfolioPage {
  private api = inject(Api);
  private router = inject(Router);
  rows = signal<any[]>([]);
  grid = signal<any>(null);
  filter = signal('');
  onlyIncomplete = signal(false);

  quadrants = [
    { key: 'Moderniser / Migrer', hint: 'besoin maintenu, maîtrise faible' },
    { key: 'Maintenir', hint: 'besoin maintenu, maîtrise suffisante' },
    { key: 'Éliminer', hint: 'besoin non maintenu, maîtrise faible' },
    { key: 'Tolérer', hint: 'besoin non maintenu, maîtrise suffisante' },
  ];

  active = computed(() => this.rows().filter((r) => !r.result.retired));
  keyPerson = computed(() => this.active().filter((r) => (r.keyPerson ?? '').startsWith('Oui')).length);
  incomplete = computed(() => this.active().filter((r) => r.result.control === 'Calcul incomplet').length);
  toInstruct = computed(() => this.active().filter((r) => r.result.computedDecision === 'À instruire').length);
  notInArchi = computed(() => this.rows().filter((r) => !r.inArchi).length);
  shown = computed(() => {
    const f = this.filter().toLowerCase();
    return this.rows().filter(
      (r) =>
        (!f || `${r.name} ${r.domain ?? ''} ${r.urbaId ?? ''}`.toLowerCase().includes(f)) &&
        (!this.onlyIncomplete() || r.result.control === 'Calcul incomplet'),
    );
  });

  constructor() {
    this.api.get('/portfolio').then((d) => {
      this.rows.set(d.rows);
      this.grid.set(d.grid);
    });
  }

  inQuadrant(key: string): any[] {
    return this.active().filter((r) => r.result.quadrant === key);
  }

  open(id: string): void {
    this.router.navigate(['/portefeuille', id]);
  }
}
