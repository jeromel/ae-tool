import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';
import { LAYER_LABELS, type StepItem, noPlaceText, viaText } from '../chains-reading';

interface Column { id: string; name: string; kind: string; layers: Record<string, StepItem[]>; }
interface Place { kind: string; parentId: string; parentName: string; columnId: string; columnName: string; via: string | null; }
interface Detail {
  id: string; name: string; hasSteps: boolean; ordered: boolean; columns: Column[]; places: Place[];
  offChain: { nature: string | null; topCapability: string | null } | null;
}

/**
 * Un processus sur son propre écran (EV-06, revue de Camille du 27/11 §4.5 ; maquette `maquette-processus-chaine-valeur`
 * §4 et §5). Ses étapes en colonnes, ou une seule colonne s'il n'est pas découpé ; trois lignes : qui, données avec le
 * sens, applications. En tête, où il se place : « Fait partie de : {chaîne} › {étape} », cliquable vers la chaîne.
 */
@Component({
  imports: [RouterLink],
  template: `
    @if (detail(); as d) {
      <h1>{{ d.name }}</h1>
      <p class="place">
        @if (d.places.length) {
          Fait partie de :
          @for (p of d.places; track p.parentId + p.columnId; let last = $last) {
            @if (p.kind === 'chaine') {
              <a routerLink="/chaines" [queryParams]="{ chaine: p.parentId }">{{ p.parentName }}</a> › {{ p.columnName }}
            } @else {
              <a [routerLink]="['/processus', p.parentId]">{{ p.parentName }}</a> › {{ p.columnName }}
            }
            @if (p.via) { <span class="muted">, via {{ p.via }}</span> }
            @if (!last) { <span class="muted"> · </span> }
          }
        } @else {
          {{ noPlace().text }}@if (noPlace().attach) { · <a [routerLink]="['/objets', d.id]">Rattacher</a> }
        }
      </p>
      <p class="links"><a [routerLink]="['/objets', d.id]">Ouvrir la carte</a></p>
      @if (!d.hasSteps) { <p class="muted small">Étapes non renseignées : ce processus est décrit d'un seul bloc.</p> }
      @else if (!d.ordered) { <p class="muted small">Ordre incomplet : l'ordre affiché n'est pas un enchaînement.</p> }

      <section class="card grid-card">
        <div class="scroll-x">
          <table [class.band]="d.hasSteps && d.ordered">
            @if (d.hasSteps) {
              <thead><tr><th scope="col" class="corner"></th>
                @for (s of d.columns; track s.id; let first = $first) {
                  <th scope="col">
                    @if (d.ordered && !first) { <span class="arrow" aria-hidden="true"></span><span class="sr">puis </span> }
                    <a class="step" [class.chevron]="d.ordered" [routerLink]="['/processus', s.id]">{{ s.name }}</a>
                  </th>
                }
              </tr></thead>
            }
            <tbody>
              @for (l of rows; track l) {
                <tr><th scope="row">{{ label(l) }}</th>
                  @for (s of d.columns; track s.id) {
                    <td>
                      @for (i of s.layers[l]; track i.id + (i.via ?? '')) {
                        <div class="item"><a [routerLink]="['/objets', i.id]">{{ i.name }}</a>@if (via(i, l); as v) { <span class="via">{{ v }}</span> }</div>
                      } @empty { <span class="gap">Non renseigné · <a [routerLink]="['/objets', s.id]">Renseigner</a></span> }
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
        <!-- Sous 640 px : une carte par étape, l'une sous l'autre, ▼ entre deux étapes (maquette §4). -->
        <div class="narrow">
          @for (s of d.columns; track s.id; let last = $last) {
            <div class="ncard">
              @if (d.hasSteps) { <a class="step" [routerLink]="['/processus', s.id]">{{ s.name }}</a> }
              @for (l of rows; track l) {
                <div class="nlabel">{{ label(l) }}</div>
                @for (i of s.layers[l]; track i.id + (i.via ?? '')) {
                  <div class="item"><a [routerLink]="['/objets', i.id]">{{ i.name }}</a>@if (via(i, l); as v) { <span class="via">{{ v }}</span> }</div>
                } @empty { <span class="gap">Non renseigné · <a [routerLink]="['/objets', s.id]">Renseigner</a></span> }
              }
            </div>
            @if (d.ordered && !last) { <div class="down" aria-hidden="true">▼</div> }
          }
        </div>
      </section>
    } @else if (error()) {
      <h1>Processus</h1>
      <div class="error" role="alert">{{ error() }}</div>
    } @else {
      <div class="card skeleton" aria-busy="true" aria-label="Chargement du processus">
        @for (k of [1, 2, 3]; track k) { <div class="row-bars">@for (b of [1, 2, 3, 4]; track b) { <div class="bar"></div> }</div> }
      </div>
    }
  `,
  styles: `
    .place { margin: 0 0 4px; }
    .links { margin: 0 0 10px; }
    .grid-card { padding: 12px 14px; }
    .scroll-x { overflow-x: auto; }
    table { border-collapse: separate; border-spacing: 8px 4px; width: 100%; }
    table.band { border-spacing: 22px 4px; }
    th, td { vertical-align: top; text-align: left; padding: 0; white-space: normal; }
    thead th { position: relative; height: 1px; }
    th.corner, tbody th { width: 92px; font-size: 12.5px; color: var(--muted); font-weight: 600; padding-top: 4px; position: sticky; left: 0; background: var(--card); z-index: 1; }
    .arrow { position: absolute; left: -16px; top: calc(50% - 7px); width: 10px; height: 14px; background: var(--accent); clip-path: polygon(0 0, 100% 50%, 0 100%); }
    .step { display: block; height: 100%; box-sizing: border-box; font-weight: 700; font-size: 13.5px; color: var(--ink); background: var(--accent-soft, #e0eef2); padding: 8px 12px; min-height: 48px; text-decoration: none; border-radius: 8px; line-height: 1.25; }
    .step.chevron { border-radius: 0; clip-path: polygon(0 0, calc(100% - 18px) 0, 100% 50%, calc(100% - 18px) 100%, 0 100%, 18px 50%); padding: 8px 24px 8px 26px; }
    .step:hover { text-decoration: underline; }
    .item { font-size: 13px; margin-bottom: 4px; }
    .via { display: block; color: var(--muted); font-size: 11.5px; }
    .gap { color: var(--muted); font-size: 12.5px; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .skeleton { padding: 14px; }
    .skeleton .row-bars { display: flex; gap: 8px; margin-bottom: 8px; }
    .skeleton .bar { background: #e6eced; border-radius: 6px; height: 40px; flex: 1; }
    .narrow { display: none; }
    .ncard { border: 1px solid var(--line); border-radius: 8px; padding: 8px; }
    .nlabel { font-size: 12px; color: var(--muted); font-weight: 600; margin: 6px 0 2px; }
    .down { text-align: center; color: var(--muted); line-height: 1.6; }
    @media (max-width: 640px) { .scroll-x { display: none; } .narrow { display: block; } }
    @media print { .step { print-color-adjust: exact; } .narrow { display: none !important; } .scroll-x { display: block !important; } }
  `,
})
export class ProcessPage {
  private api = inject(Api);
  id = input<string>();
  detail = signal<Detail | null>(null);
  error = signal('');
  readonly rows = ['qui', 'donnees', 'applications'];
  noPlace = computed(() => noPlaceText(this.detail()?.offChain?.nature));

  constructor() {
    effect(() => {
      const id = this.id();
      this.detail.set(null);
      this.error.set('');
      if (!id) return;
      this.api.get(`/processus/${encodeURIComponent(id)}`).then((d) => this.detail.set(d))
        .catch((e) => this.error.set((e as Error).message));
    });
  }

  label(l: string): string { return LAYER_LABELS[l] ?? l; }
  /** Données : le sens, en symbole et en mot (« ✎ écrit », « ◉ lu ») ; applications : « via le service … ». */
  via(i: StepItem, layer: string): string | null {
    if (layer === 'donnees') return i.via ? (i.via === 'écrit' ? '✎ ' : '◉ ') + i.via : null;
    return viaText(i);
  }
}
