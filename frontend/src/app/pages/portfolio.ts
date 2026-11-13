import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../api';
import { NoteGauge } from '../note-gauge';

/** Portefeuille applicatif : une ligne par application, notation recalculée par l'outil selon la grille du métamodèle. */
@Component({
  imports: [NoteGauge],
  styles: `
    td.app { white-space: nowrap; }
    td.nowrap { white-space: nowrap; }
    th .sub { font-weight: 400; font-size: 11.5px; }
  `,
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

    <div class="tabs" role="tablist">
      <button type="button" role="tab" [attr.aria-selected]="tab() === 'tableau'" (click)="tab.set('tableau')">Tableau</button>
      <button type="button" role="tab" [attr.aria-selected]="tab() === 'quadrant'" (click)="tab.set('quadrant')">Quadrant d'urbanisation</button>
    </div>

    @if (tab() === 'quadrant') {
      <div class="quadrant">
        @for (q of quadrants; track q.key) {
          <div>
            <h3>{{ q.key }} <span class="muted">{{ q.hint }}</span></h3>
            @for (r of inQuadrant(q.key); track r.id) {
              <a class="chip" [class]="tone(q.key)" style="margin: 0 4px 4px 0; text-decoration: none" [href]="'/portefeuille/' + r.id" (click)="open(r.id); $event.preventDefault()">{{ r.name }}</a>
            } @empty {
              <span class="muted small">Aucune application placée.</span>
            }
          </div>
        }
      </div>
      @if (incomplete() > 0) {
        <p class="muted" style="margin: 10px 0 0">
          {{ incomplete() }} application(s) ne sont pas placées : il leur manque des preuves, le besoin métier ou les déclencheurs rouges.
        </p>
      }
    } @else {
      <div class="card flush">
        <div class="tools">
          <input type="search" placeholder="Filtrer par nom ou domaine" (input)="filter.set($any($event.target).value)" />
          <label><input type="checkbox" (change)="onlyIncomplete.set($any($event.target).checked)" /> Notations incomplètes seulement</label>
          <span class="muted right">{{ shown().length }} ligne(s), cliquer une ligne pour noter</span>
        </div>
        <div class="scroll tall" style="--above: 330px">
          <table>
            <tr>
              <th>Application</th><th>Domaine</th><th>État</th><th>CM</th><th>Besoin</th>
              @for (c of grid()?.criteria ?? []; track c.code) { <th [title]="c.label">{{ c.code }}<div class="sub">{{ c.label }}</div></th> }
              <th>Maîtrise</th><th>Décision calculée</th><th>Décision retenue</th><th>Priorité</th><th>Contrôle</th>
            </tr>
            @for (r of shown(); track r.id) {
              <tr class="click" (click)="open(r.id)">
                <td class="app"><b>{{ r.name }}</b>
                  <div class="muted small">{{ r.urbaId }} @if (!r.inArchi) { <span class="chip warn" title="Absente du modèle Archi">hors Archi</span> }</div></td>
                <td>{{ r.domain }}</td><td>{{ r.state }}</td>
                <td><app-note-gauge [value]="r.cm" /></td><td class="nowrap">{{ r.need }}</td>
                @for (c of grid()?.criteria ?? []; track c.code) { <td><app-note-gauge [value]="r.result.notes[c.code]" [lowUnder]="2" /></td> }
                <td><app-note-gauge [value]="r.result.mastery" [lowUnder]="2.5" /></td>
                <td>@if (r.result.computedDecision) { <span class="chip" [class]="tone(r.result.computedDecision)">{{ r.result.computedDecision }}</span> }</td>
                <td>@if (r.retainedDecision) { <span class="chip" [class]="tone(r.retainedDecision)">{{ r.retainedDecision }}</span> }</td>
                <td>@if (r.result.priority) { <span class="chip" [class.bad]="r.result.priority === 'Haute'" [class.warn]="r.result.priority === 'Moyenne'">{{ r.result.priority }}</span> }</td>
                <td [title]="r.result.missing.join(' ; ')">
                  <span class="chip" [class.ok]="r.result.control === 'Conforme au calcul'" [class.warn]="r.result.control !== 'Conforme au calcul' && r.result.control !== 'Sans objet'">{{ r.result.control }}</span>
                </td>
              </tr>
            }
          </table>
        </div>
      </div>
    }
  `,
})
export class PortfolioPage {
  private api = inject(Api);
  private router = inject(Router);
  rows = signal<any[]>([]);
  grid = signal<any>(null);
  tab = signal<'tableau' | 'quadrant'>('tableau');
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

  /** Teinte d'une décision : la même dans le tableau et dans le quadrant. */
  tone(decision: string): string {
    if (decision.startsWith('Maintenir')) return 'chip ok';
    if (decision.startsWith('Moderniser')) return 'chip warn';
    if (decision.startsWith('Éliminer') || decision.startsWith('À instruire')) return 'chip bad';
    if (decision.startsWith('Tolérer')) return 'chip info';
    return 'chip';
  }

  open(id: string): void {
    this.router.navigate(['/portefeuille', id]);
  }
}
