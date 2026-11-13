import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

/** Notation d'une application : preuves par sous-critère, déclencheurs rouges, décision retenue. Le résultat est calculé par le serveur. */
@Component({
  imports: [RouterLink],
  template: `
    <a routerLink="/portefeuille">← Portefeuille</a>
    <h1>{{ name() }}</h1>
    <p class="lead"><a [routerLink]="['/objets', id()]">Voir la fiche de l'objet</a></p>
    @if (error()) { <div class="error">{{ error() }}</div> }
    @if (a(); as a) {
      @if (result(); as r) {
        <div class="stats">
          @for (c of grid().criteria; track c.code) {
            <div class="stat">
              <b>{{ r.notes[c.code] ?? '–' }}</b><span>{{ c.code }} · {{ c.label }}
                @if (r.rates[c.code] !== null && r.rates[c.code] !== undefined) { ({{ pct(r.rates[c.code]) }}) }</span>
            </div>
          }
          <div class="stat"><b>{{ r.mastery ?? '–' }}</b><span>maîtrise technique</span></div>
          <div class="stat" [class.alert]="r.computedDecision === 'À instruire'"><b style="font-size: 17px; padding: 5px 0">{{ r.computedDecision ?? '–' }}</b><span>décision calculée</span></div>
          <div class="stat" [class.alert]="r.priority === 'Haute'"><b style="font-size: 17px; padding: 5px 0">{{ r.priority ?? '–' }}</b><span>priorité @if (r.urgency !== null) { (U = {{ r.urgency }}) }</span></div>
        </div>
        <div class="card">
          <b>Contrôle :</b> {{ r.control }}
          @if (r.missing.length) { · <span class="muted">à collecter : {{ r.missing.join(' ; ') }}</span> }
          @if (r.securityAction) { · action sécurité : <span class="chip warn">{{ r.securityAction }}</span> }
          @if (r.heritageAction) { · action patrimoine : <span class="chip info">{{ r.heritageAction }}</span> }
        </div>
      }
      <div class="card">
        <h2>Validation métier et décision</h2>
        <dl class="kv">
          <dt>Criticité métier (CM)</dt>
          <dd>
            <select (change)="a.cm = num($any($event.target).value); touch()">
              <option value="" [selected]="a.cm === null">À valider</option>
              @for (v of [1, 2, 3, 4]; track v) { <option [value]="v" [selected]="a.cm === v">{{ v }}</option> }
            </select>
            <span class="muted small"> 1 confort · 2 support opérationnel · 3 impact financier, réglementaire ou client sous 48 h · 4 sécurité ou continuité du transport</span>
          </dd>
          <dt>Besoin maintenu ?</dt>
          <dd>
            <select (change)="a.need = $any($event.target).value; touch()">
              @for (v of ['Oui', 'Non', 'À confirmer']; track v) { <option [selected]="a.need === v">{{ v }}</option> }
            </select>
          </dd>
          <dt>Enjeu patrimonial à traiter ?</dt>
          <dd>
            <select (change)="a.heritageStake = $any($event.target).value; touch()">
              @for (v of ['Oui', 'Non', 'À confirmer']; track v) { <option [selected]="a.heritageStake === v">{{ v }}</option> }
            </select>
          </dd>
          <dt>Décision retenue en comité</dt>
          <dd>
            <select (change)="a.retainedDecision = $any($event.target).value || null; touch()">
              <option value="" [selected]="!a.retainedDecision">Non décidée</option>
              @for (v of decisions(); track v) { <option [selected]="a.retainedDecision === v">{{ v }}</option> }
            </select>
          </dd>
          <dt>Justification</dt>
          <dd><textarea (input)="a.justification = $any($event.target).value; touch()">{{ a.justification }}</textarea></dd>
        </dl>
      </div>
      @for (c of grid().criteria; track c.code) {
        <div class="card">
          <div class="tools">
            <h2 style="margin: 0">{{ c.code }} · {{ c.label }}</h2>
            <span class="muted small">noté par : {{ c.scoredBy }}</span>
            <span class="right">Déclencheur rouge
              <select (change)="a.redTriggers[c.code] = $any($event.target).value; touch()">
                @for (v of ['NC', 'Non', 'Oui']; track v) { <option [selected]="a.redTriggers[c.code] === v">{{ v }}</option> }
              </select>
            </span>
          </div>
          <p class="muted small" style="margin: 0 0 8px">Rouge si : {{ c.redTrigger }}</p>
          <table>
            <tr><th>Sous-critère</th><th class="num">Poids</th><th>Preuve attendue</th><th>Preuve</th><th style="width: 34%">Référence / justification (obligatoire)</th></tr>
            @for (s of c.subCriteria; track s.label) {
              @if (evidence(c.code, s.label); as e) {
                <tr>
                  <td>{{ s.label }}</td><td class="num">{{ s.weight }}</td><td class="muted">{{ s.expectedEvidence }}</td>
                  <td>
                    <select (change)="e.value = $any($event.target).value; touch()">
                      @for (v of values; track v.value) { <option [value]="v.value" [selected]="e.value === v.value">{{ v.label }}</option> }
                    </select>
                  </td>
                  <td><input type="text" style="width: 100%" [value]="e.reference ?? ''" (input)="e.reference = $any($event.target).value; touch()" /></td>
                </tr>
              }
            }
          </table>
        </div>
      }
      <div class="tools" style="position: sticky; bottom: 0; background: var(--bg); padding: 10px 0">
        <button [disabled]="!dirty()" (click)="save()">Enregistrer et recalculer</button>
        @if (saved()) { <span class="chip ok">Enregistré</span> }
        @if (dirty()) { <span class="muted">Modifications non enregistrées</span> }
      </div>
    }
  `,
})
export class AssessmentPage {
  private api = inject(Api);
  id = input.required<string>();
  name = signal('');
  a = signal<any>(null);
  result = signal<any>(null);
  grid = signal<any>({ criteria: [] });
  decisions = signal<string[]>([]);
  dirty = signal(false);
  saved = signal(false);
  error = signal('');

  values = [
    { value: 'NC', label: 'NC · non connu' },
    { value: '0', label: '0 · absent, non conforme, non testé' },
    { value: '0.5', label: '0,5 · partiel, ancien, non validé' },
    { value: '1', label: '1 · présent, à jour, démontré' },
    { value: 'N/A', label: 'N/A · non applicable' },
  ];

  constructor() {
    effect(() => {
      const id = this.id();
      this.api.get(`/portfolio/${id}`).then((d) => {
        this.a.set(d.assessment);
        this.result.set(d.result);
        this.grid.set(d.grid);
        this.decisions.set(d.grid.decisions);
      });
      this.api.get(`/objects/${id}`).then((o) => this.name.set(o.brief.name));
    });
  }

  evidence(code: string, label: string): any {
    return this.a().evidences.find((e: any) => e.code === code && e.subCriterion === label);
  }

  num(v: string): number | null {
    return v === '' ? null : Number(v);
  }

  pct(rate: number): string {
    return Math.round(rate * 100) + ' %';
  }

  touch(): void {
    this.dirty.set(true);
    this.saved.set(false);
  }

  async save(): Promise<void> {
    this.error.set('');
    try {
      const d = await this.api.put(`/portfolio/${this.id()}`, this.a());
      this.a.set(d.assessment);
      this.result.set(d.result);
      this.dirty.set(false);
      this.saved.set(true);
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
}
