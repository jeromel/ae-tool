import { Component, Injectable, computed, inject, input, signal } from '@angular/core';
import { Api, enterEtat } from './api';

export interface EtatItem { id: string; kind: string; name: string; typeLabel: string; layerLabel: string; mark: string; }
export interface EtatSummary {
  label: string;
  scenario: { id: string; name: string; status: string; projectId: string };
  step: { id: string; name: string; order: number };
  marks: Record<string, string>;
  items: EtatItem[];
  issues: { severity: string; code: string; message: string }[];
}

/**
 * État du SI regardé dans l'interface. L'existant par défaut ; sinon une étape d'un scénario, et alors ce qui y change
 * (ajouté, modifié, retiré) pour marquer les objets dans tous les écrans.
 */
@Injectable({ providedIn: 'root' })
export class ProjectionState {
  private api = inject(Api);
  readonly etat = this.api.etat;
  readonly summary = signal<EtatSummary | null>(null);
  readonly error = signal('');
  readonly removed = computed(() => (this.summary()?.items ?? []).filter((i) => i.mark === 'retiré'));

  constructor() {
    if (this.etat) {
      this.api.get<EtatSummary>(`/trajectoires/etat?scenario=${encodeURIComponent(this.etat.scenarioId)}&step=${encodeURIComponent(this.etat.stepId)}`)
        .then((s) => this.summary.set(s))
        .catch((e) => this.error.set((e as Error).message));
    }
  }

  mark(id: string | null | undefined): string | null {
    return id ? this.summary()?.marks[id] ?? null : null;
  }

  enter(scenarioId: string, stepId: string, label: string, goto?: string): void {
    enterEtat({ scenarioId, stepId, label }, goto);
  }

  leave(): void {
    enterEtat(null);
  }
}

/** Pastille « ajouté », « modifié » ou « retiré » à côté d'un objet, quand on regarde un état projeté. */
@Component({
  selector: 'app-mark',
  template: `@if (state.mark(id()); as m) { <span class="mark" [class]="m" [title]="'À cette étape : ' + m">{{ m }}</span> }`,
  styles: `
    .mark { display: inline-block; margin-left: 6px; padding: 0 7px; border-radius: 8px; font-size: 11px; font-weight: 650; line-height: 17px; vertical-align: 1px; }
    .mark.ajouté { background: #d6f0dc; color: #1d6b35; }
    .mark.modifié { background: #fbe7c4; color: #7a4a00; }
    .mark.retiré { background: #f6d2cd; color: #8c2a1c; text-decoration: line-through; }
  `,
})
export class Mark {
  state = inject(ProjectionState);
  id = input<string | null | undefined>(null);
}
