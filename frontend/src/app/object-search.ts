import { Component, inject, input, output, signal } from '@angular/core';
import { Api, Brief } from './api';

/** Recherche d'un objet par son nom, dans tout le référentiel. Sert de point d'entrée à la place d'une liste à parcourir. */
@Component({
  selector: 'app-object-search',
  template: `
    <div class="search" (focusout)="closeSoon()">
      <input type="search" [placeholder]="placeholder()" [attr.aria-label]="placeholder()" [value]="text()" autocomplete="off"
        (input)="type($any($event.target).value)" (focus)="open.set(true)" (keydown.enter)="first()" (keydown.escape)="open.set(false)" />
      @if (open() && text().trim().length >= 2) {
        <div class="results">
          @for (o of items(); track o.id) {
            <button type="button" (click)="choose(o)"><b>{{ o.name || '(sans nom)' }}</b><span>{{ o.typeLabel }}</span></button>
          } @empty {
            <p>Aucun objet ne porte ce nom.</p>
          }
          @if (total() > items().length) {
            @if (showAll()) { <button type="button" class="more" (click)="all.emit(text())">Voir les {{ total() }} résultats</button> }
            @else { <p class="rest">{{ total() - items().length }} autre(s) résultat(s) : précisez la recherche.</p> }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .search { position: relative; }
    input { width: 100%; }
    .results { position: absolute; left: 0; right: 0; top: calc(100% + 4px); z-index: 20; max-height: 60vh; overflow: auto; padding: 4px;
      background: var(--card); color: var(--ink); border: 1px solid var(--line); border-radius: 8px; box-shadow: 0 8px 24px #14262d2e; }
    .results button { display: block; width: 100%; text-align: left; background: transparent; color: inherit; border: 0; border-radius: 5px; padding: 6px 8px; font-weight: 400; white-space: normal; }
    .results button:hover, .results button:focus-visible { background: var(--accent-soft); }
    .results b { display: block; font-weight: 600; overflow-wrap: anywhere; }
    .results span { font-size: 12px; color: var(--muted); }
    .results .more { color: var(--accent); font-weight: 600; border-top: 1px solid var(--line); border-radius: 0; }
    .results p { margin: 0; padding: 8px; color: var(--muted); }
    .results .rest { font-size: 12px; border-top: 1px solid var(--line); }
  `,
})
export class ObjectSearch {
  private api = inject(Api);
  placeholder = input('Rechercher un objet');
  /** Clés des types d'objets proposés ; absent = tous les types. Une liste vide ne propose rien. */
  types = input<string[] | undefined>(undefined);
  /** Propose la liste complète des résultats ; à désactiver quand l'appelant n'a pas de page où l'afficher. */
  showAll = input(true);
  picked = output<Brief>();
  /** Demande de la liste complète des résultats pour le texte saisi. */
  all = output<string>();

  text = signal('');
  items = signal<Brief[]>([]);
  total = signal(0);
  open = signal(false);
  private request = 0;

  type(value: string): void {
    this.text.set(value);
    this.open.set(true);
    if (value.trim().length < 2) return this.items.set([]);
    const mine = ++this.request;
    const types = this.types();
    if (types?.length === 0) return this.items.set([]);
    const url = types ? `/objects/search?q=${encodeURIComponent(value)}&types=${encodeURIComponent(types.join(','))}&limit=8` : `/objects?q=${encodeURIComponent(value)}&limit=8`;
    this.api.get(url).then((d) => {
      if (mine !== this.request) return; // une saisie plus récente est en cours
      this.items.set(d.items);
      this.total.set(d.total);
    });
  }

  first(): void {
    const o = this.items()[0];
    if (o) this.choose(o);
  }

  choose(o: Brief): void {
    this.picked.emit(o);
    this.text.set('');
    this.open.set(false);
  }

  closeSoon(): void {
    setTimeout(() => this.open.set(false), 150); // laisse au clic sur un résultat le temps d'aboutir
  }
}
