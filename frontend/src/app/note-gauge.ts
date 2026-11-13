import { Component, computed, input } from '@angular/core';

/**
 * Note sur une échelle courte (1 à 4 par défaut), dessinée en tronçons pleins ou vides.
 * Une note absente reste lisible comme telle : tronçons vides et tiret, jamais un zéro.
 */
@Component({
  selector: 'app-note-gauge',
  template: `
    <span class="gauge" [class.none]="value() === null" [class.low]="isLow()" [attr.aria-label]="label()" [title]="label()">
      @for (s of segments(); track $index) { <i [class.on]="s"></i> }
      <b>{{ value() ?? '–' }}</b>
    </span>
  `,
  styles: `
    .gauge { display: inline-flex; align-items: center; gap: 2px; white-space: nowrap; }
    i { width: 9px; height: 8px; background: #dfe6e8; }
    i:first-child { border-radius: 4px 0 0 4px; }
    i:nth-last-child(2) { border-radius: 0 4px 4px 0; }
    i.on { background: var(--accent, #0c5a70); }
    .low i.on { background: var(--bad, #b3261e); }
    b { margin-left: 5px; min-width: 1.6em; font-weight: 600; font-variant-numeric: tabular-nums; text-align: left; }
    .none b { color: var(--muted, #5c6c73); font-weight: 400; }
  `,
})
export class NoteGauge {
  value = input<number | null, number | null | undefined>(null, { transform: (v) => v ?? null });
  max = input(4);
  /** Seuil sous lequel la note est signalée en rouge (aucun signalement si absent). */
  lowUnder = input<number | null>(null);

  segments = computed(() => {
    const v = this.value() ?? 0;
    return Array.from({ length: this.max() }, (_, i) => v >= i + 1 || v - i >= 0.5);
  });
  isLow = computed(() => this.value() !== null && this.lowUnder() !== null && this.value()! < this.lowUnder()!);
  label = computed(() => (this.value() === null ? 'Non noté' : `${this.value()} sur ${this.max()}`));
}
