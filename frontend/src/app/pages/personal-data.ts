import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';
import { ScopeNote } from '../scope-note';
import { Session } from '../session';

interface RegisterFlow { id: string; label: string; sourceName: string; targetName: string; crossesItOt: boolean; status: string | null; }
interface Row { id: string; name: string; code: string | null; sensitivity: string | null; personal: string | null; owner: string | null; master: string | null;
  applications: string[]; flows: RegisterFlow[]; shares: string[]; personalAttributes: string[]; crossesItOt: boolean; }
interface Share { id: string; name: string; data: string[]; files: string[]; }
interface Register { rows: Row[]; shares: Share[]; }

type Question = 'toutes' | 'personnelles' | 'partages' | 'frontiere' | 'sans-owner';

/**
 * Registre des données personnelles et sensibles : où elles vivent, par quels flux elles passent, sur quels partages
 * elles sont déposées. Une base de travail avec le délégué à la protection des données et le responsable de la sécurité.
 */
@Component({
  imports: [RouterLink, ScopeNote],
  template: `
    <h1>Données personnelles et sensibles</h1>
    <p class="lead">Les objets de données qui contiennent des données personnelles ou dont la sensibilité demande une protection, avec les applications
      qui les tiennent, les flux qui les transportent et les partages de fichiers où elles sont déposées. Chaque ligne mène au parcours de la donnée.</p>
    <!-- Sans l'infrastructure, partages et frontière IT/OT ne disent rien de vrai : retirés, jamais « aucun » (question 4 de Haussman). -->
    @if (session.business()) { <app-scope-note text="Partages de fichiers et frontière IT/OT non affichés pour votre accès." /> }
    @if (register(); as r) {
      <div class="questions" role="group" aria-label="Questions">
        @for (q of shownQuestions(); track q.key) {
          <button type="button" class="question" [attr.aria-pressed]="question() === q.key" (click)="question.set(q.key)">{{ q.label }} ({{ count(q.key) }})</button>
        }
      </div>
      <div class="card flush">
        <div class="scroll tall" style="--above: 330px">
          <table>
            <tr><th>Donnée</th><th>Sensibilité</th><th>Personnelles</th><th>Maître</th><th>Applications</th><th>Flux</th>@if (!session.business()) { <th>Partages</th> }<th>Attributs personnels</th></tr>
            @for (x of rows(); track x.id) {
              <tr>
                <td><a [routerLink]="['/parcours', x.id]">{{ x.name }}</a>@if (x.code) { <div class="muted small">{{ x.code }}</div> }</td>
                <td>{{ x.sensitivity ?? '–' }}</td>
                <td>@if (x.personal === 'Oui') { <span class="chip bad">Oui</span> } @else { {{ x.personal ?? '–' }} }</td>
                <td>{{ x.master ?? '–' }}@if (x.owner) { <div class="muted small">owner : {{ x.owner }}</div> }</td>
                <td class="small">{{ x.applications.join(', ') || '–' }}</td>
                <td class="small">
                  @for (f of x.flows; track f.id) {
                    <div>{{ f.label }} <span class="muted">({{ f.sourceName }} → {{ f.targetName }})</span>@if (f.crossesItOt && !session.business()) { <span class="chip warn">IT/OT</span> }</div>
                  } @empty { – }
                </td>
                @if (!session.business()) { <td class="small">@for (s of x.shares; track s) { <span class="chip info">{{ s }}</span> } @empty { – }</td> }
                <td class="small">{{ x.personalAttributes.join(', ') || '–' }}</td>
              </tr>
            } @empty { <tr><td colspan="8" class="muted">Aucune donnée dans cette catégorie. Le registre se remplit avec l'import du catalogue des données et des flux.</td></tr> }
          </table>
        </div>
      </div>
      @if (!session.business()) {
      <div class="card">
        <h2>Partages de fichiers qui hébergent des données personnelles ou sensibles</h2>
        @if (r.shares.length) {
          <table>
            <tr><th>Partage</th><th>Données</th><th>Fichiers</th></tr>
            @for (s of r.shares; track s.id) {
              <tr><td><a [routerLink]="['/parcours', s.id]">{{ s.name }}</a></td><td>{{ s.data.join(', ') }}</td><td class="small muted">{{ s.files.join(', ') }}</td></tr>
            }
          </table>
        } @else {
          <p class="muted small">Aucun. Un partage apparaît ici quand un artefact (fichier) qui réalise une donnée à protéger lui est relié dans le référentiel.</p>
        }
      </div>
      }
    } @else {
      <p class="muted">Chargement…</p>
    }
  `,
})
export class PersonalDataPage {
  private api = inject(Api);
  readonly session = inject(Session);
  register = signal<Register | null>(null);
  question = signal<Question>('toutes');
  readonly questions: { key: Question; label: string }[] = [
    { key: 'toutes', label: 'Toutes' }, { key: 'personnelles', label: 'Avec données personnelles' }, { key: 'partages', label: 'Déposées sur un partage' },
    { key: 'frontiere', label: 'Qui traversent la frontière IT/OT' }, { key: 'sans-owner', label: 'Sans data owner' },
  ];

  /** Sans l'infrastructure, les questions « partage » et « frontière IT/OT » n'auraient que des réponses fausses. */
  shownQuestions = computed(() => this.session.business() ? this.questions.filter((q) => q.key !== 'partages' && q.key !== 'frontiere') : this.questions);

  rows = computed(() => (this.register()?.rows ?? []).filter((x) => this.matches(x, this.question())));

  constructor() {
    this.api.get<Register>('/catalogue/registre').then((r) => this.register.set(r));
  }

  private matches(x: Row, q: Question): boolean {
    switch (q) {
      case 'personnelles': return x.personal === 'Oui' || x.personalAttributes.length > 0;
      case 'partages': return x.shares.length > 0;
      case 'frontiere': return x.crossesItOt;
      case 'sans-owner': return !x.owner;
      default: return true;
    }
  }

  count(q: Question): number {
    return (this.register()?.rows ?? []).filter((x) => this.matches(x, q)).length;
  }
}
