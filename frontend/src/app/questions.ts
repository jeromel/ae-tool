import { Component, computed, input, output } from '@angular/core';

/**
 * Une question prête à l'emploi posée à un espace : elle règle le filtre, le tri ou la coloration.
 * « missing » signale une question à laquelle le référentiel ne sait pas encore répondre, et dit quoi renseigner.
 */
export interface Question { key: string; label: string; hint?: string; missing?: string; }

@Component({
  selector: 'app-questions',
  template: `
    <div class="questions" role="group" aria-label="Questions">
      @for (q of questions(); track q.key) {
        <button type="button" class="question" [class.missing]="q.missing" [attr.aria-pressed]="q.key === selected()" [title]="q.hint ?? ''" (click)="pick.emit(q.key)">
          {{ q.label }}
        </button>
      }
    </div>
    @if (current()?.missing; as m) { <div class="note"><b>Le référentiel ne sait pas encore répondre.</b> {{ m }}</div> }
  `,
})
export class QuestionBar {
  questions = input.required<Question[]>();
  selected = input.required<string>();
  pick = output<string>();
  current = computed(() => this.questions().find((q) => q.key === this.selected()));
}
