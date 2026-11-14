import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, Brief, SOURCE_LABELS } from '../api';
import { Session } from '../session';

@Component({
  imports: [RouterLink],
  template: `
    <h1>Tous les objets</h1>
    <p class="lead">Les objets d'architecture existent indépendamment des vues : un objet n'est créé qu'une fois, quel que soit le nombre de diagrammes où il apparaît.</p>
    <div class="card flush">
      <div class="tools">
        <select (change)="setLayer($any($event.target).value)">
          <option value="">Toutes les couches</option>
          @for (l of meta()?.layers ?? []; track l.key) { <option [value]="l.key" [selected]="layer() === l.key">{{ l.label }}</option> }
        </select>
        <select (change)="type.set($any($event.target).value); load()">
          <option value="">Tous les types</option>
          @for (t of types(); track t.key) { <option [value]="t.key" [selected]="type() === t.key">{{ t.label }}</option> }
        </select>
        <select aria-label="État de validation" (change)="status.set($any($event.target).value); load()">
          <option value="">Tous les états</option>
          @for (s of statuses; track s) { <option [value]="s" [selected]="status() === s">{{ s }}</option> }
        </select>
        <input type="search" placeholder="Nom ou valeur de propriété" [value]="q()" (input)="q.set($any($event.target).value); load()" />
        <span class="muted">{{ total() }} objet(s)@if (total() > items().length) {, {{ items().length }} affichés}</span>
        <span class="right tools-right">
          @if (session.can('contributeur')) { <a class="btn" routerLink="/objets/nouveau" [queryParams]="type() ? { type: type() } : {}">Nouvel objet</a> }
          <a class="btn ghost" [href]="api.url('/export/objects.csv')">Exporter en CSV</a>
        </span>
      </div>
      <div class="scroll tall" style="--above: 190px">
        <table>
          <tr><th>Nom</th><th>Type</th><th>Couche</th><th>Source</th><th>Statut</th></tr>
          @for (o of items(); track o.id) {
            <tr class="click" (click)="open(o.id)">
              <td><span class="swatch" [style.background]="fills()[o.typeKey]"></span><a [routerLink]="['/objets', o.id]">{{ o.name || '(sans nom)' }}</a></td>
              <td>{{ o.typeLabel }}</td><td>{{ o.layerLabel }}</td><td>{{ sources[o.source] ?? o.source }}</td><td><span class="chip" [class.ok]="o.status === 'Validé'" [class.warn]="o.status === 'Proposé'">{{ o.status }}</span></td>
            </tr>
          }
        </table>
      </div>
    </div>
  `,
  styles: `.tools-right { display: flex; gap: 8px; }`,
})
export class ObjectsPage {
  api = inject(Api);
  session = inject(Session);
  private router = inject(Router);
  couche = input<string>();
  /** Texte cherché, transmis par la recherche du menu. */
  rechercheInitiale = input<string>(undefined, { alias: 'q' });
  /** État de validation demandé par l'adresse (« ?etat=Proposé »), pour un lien direct vers ce qui attend. */
  etat = input<string>();
  meta = signal<any>(null);
  layer = signal('');
  type = signal('');
  q = signal('');
  status = signal('');
  statuses = ['Brouillon', 'Proposé', 'Validé'];
  items = signal<Brief[]>([]);
  total = signal(0);
  sources = SOURCE_LABELS;
  /** Couleur de fond de chaque type, pour repérer la nature d'un objet d'un coup d'œil. */
  fills = computed<Record<string, string>>(() =>
    Object.fromEntries(((this.meta()?.objectTypes ?? []) as any[]).map((t) => [t.key, t.visual?.fill ?? '#ffffff'])),
  );

  constructor() {
    this.api.get('/metamodel').then((m) => this.meta.set(m));
    effect(() => {
      this.layer.set(this.couche() ?? '');
      this.q.set(this.rechercheInitiale() ?? '');
      this.status.set(this.etat() ?? '');
      this.load();
    });
  }

  types(): any[] {
    const all = (this.meta()?.objectTypes ?? []) as any[];
    return all.filter((t) => !this.layer() || t.layerKey === this.layer()).sort((a, b) => a.label.localeCompare(b.label));
  }

  setLayer(layer: string): void {
    this.layer.set(layer);
    this.type.set('');
    this.load();
  }

  load(): void {
    const p = new URLSearchParams({ layer: this.layer(), type: this.type(), q: this.q(), status: this.status() });
    this.api.get(`/objects?${p}`).then((d) => {
      this.items.set(d.items);
      this.total.set(d.total);
    });
  }

  open(id: string): void {
    this.router.navigate(['/objets', id]);
  }
}
