import { Component, input } from '@angular/core';
import { glyphPath } from '../../glyphs';

/** Aperçu d'un type d'objet : sa forme, ses couleurs et son pictogramme, tels que le rendu des vues les combine. */
@Component({
  selector: 'app-type-preview',
  template: `
    <svg viewBox="0 0 40 28" [attr.width]="width()" [attr.height]="width() * 0.7" role="img" [attr.aria-label]="'Aperçu : ' + (visual()?.shape || 'rectangle')">
      @switch (visual()?.shape) {
        @case ('ellipse') { <ellipse cx="20" cy="14" rx="18" ry="12" [attr.fill]="fill()" [attr.stroke]="stroke()" stroke-width="1.5" /> }
        @case ('hexagone') { <polygon points="9,2 31,2 38,14 31,26 9,26 2,14" [attr.fill]="fill()" [attr.stroke]="stroke()" stroke-width="1.5" stroke-linejoin="round" /> }
        @case ('arrondi') { <rect x="2" y="2" width="36" height="24" rx="9" [attr.fill]="fill()" [attr.stroke]="stroke()" stroke-width="1.5" /> }
        @default { <rect x="2" y="2" width="36" height="24" rx="2" [attr.fill]="fill()" [attr.stroke]="stroke()" stroke-width="1.5" /> }
      }
      <path [attr.d]="path()" transform="translate(12 6)" fill="none" [attr.stroke]="visual()?.textColor || '#14262d'" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
  `,
  styles: ':host { display: inline-flex; vertical-align: middle; } svg { display: block; }',
})
export class TypePreview {
  visual = input<any>();
  width = input(40);
  fill = () => this.visual()?.fill || '#f0f0f0';
  stroke = () => this.visual()?.stroke || '#5c5c5c';
  path = () => glyphPath(this.visual()?.icon);
}

/** Aperçu du trait et de la tête d'un type de relation. Les têtes « losange » et « point » s'écrivent côté source. */
@Component({
  selector: 'app-line-preview',
  template: `
    <svg viewBox="0 0 84 16" [attr.width]="width()" [attr.height]="width() * 0.19" role="img" [attr.aria-label]="'Aperçu : trait ' + line() + ', tête ' + head()">
      <line [attr.x1]="start()" y1="8" [attr.x2]="end()" y2="8" stroke="currentColor" stroke-width="1.6" [attr.stroke-dasharray]="dash()" />
      @switch (head()) {
        @case ('fleche') { <path d="M70,3 L80,8 L70,13 z" fill="currentColor" /> }
        @case ('ouverte') { <path d="M70,3 L80,8 L70,13" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /> }
        @case ('triangle') { <path d="M68,3 L80,8 L68,13 z" fill="#fff" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" /> }
        @case ('losange') { <path d="M4,8 L10,4 L16,8 L10,12 z" fill="currentColor" /> }
        @case ('losange-creux') { <path d="M4,8 L10,4 L16,8 L10,12 z" fill="#fff" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" /> }
        @case ('point') { <circle cx="7" cy="8" r="3.2" fill="currentColor" /> }
      }
    </svg>
  `,
  styles: ':host { display: inline-flex; vertical-align: middle; color: var(--ink); } svg { display: block; }',
})
export class LinePreview {
  line = input<string | null | undefined>('plein');
  head = input<string | null | undefined>('aucune');
  width = input(84);
  dash = () => (this.line() === 'tirets' ? '7 4' : this.line() === 'pointillés' ? '2 4' : null);
  start = () => (this.head() === 'losange' || this.head() === 'losange-creux' ? 16 : this.head() === 'point' ? 10 : 4);
  end = () => (['fleche', 'ouverte', 'triangle'].includes(this.head() ?? '') ? 70 : 80);
}
