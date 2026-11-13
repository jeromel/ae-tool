import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, Brief } from '../api';
import { ObjectSearch } from '../object-search';

interface Reached { id: string; name: string; typeKey: string; layer: string; depth: number; via: string; direction: string; }

/**
 * Analyse d'impact : ce qui est touché si un objet tombe ou change (aval), et ce dont il dépend (amont).
 * Elle traverse toutes les couches, du serveur à la capacité, selon les relations du référentiel.
 */
@Component({
  imports: [RouterLink, ObjectSearch],
  template: `
    <h1>Analyse d'impact</h1>
    <p class="lead">Choisissez un serveur, un réseau, une application ou une donnée : l'outil suit les relations pour dire ce qui est touché s'il tombe ou change, et ce dont il dépend.</p>
    <div class="card">
      <div class="tools" style="margin: 0">
        <div style="flex: 1; min-width: 240px; max-width: 420px"><app-object-search placeholder="Objet à analyser" (picked)="choose($event)" /></div>
        <label>Distance
          <select (change)="depth.set(+$any($event.target).value)">
            @for (n of [1, 2, 3, 4, 5]; track n) { <option [selected]="depth() === n" [value]="n">{{ n }} relation{{ n > 1 ? 's' : '' }}</option> }
          </select>
        </label>
      </div>
    </div>

    @if (subject(); as s) {
      <div class="stats">
        <div class="stat" style="flex: 2"><b style="font-size: 20px">{{ s.name }}</b><span>{{ s.typeLabel }}, {{ s.layerLabel }} · <a [routerLink]="['/objets', s.id]">contexte</a></span></div>
        <div class="stat" [class.alert]="downstream().length > 0"><b>{{ downstream().length }}</b><span>objets touchés en aval</span></div>
        <div class="stat"><b>{{ applicationsDown() }}</b><span>dont applications</span></div>
        <div class="stat"><b>{{ upstream().length }}</b><span>dépendances en amont</span></div>
      </div>
      <div class="row">
        @for (side of sides(); track side.key) {
          <div class="card">
            <h2>{{ side.title }}</h2>
            <p class="muted small" style="margin-top: -6px">{{ side.hint }}</p>
            @for (g of side.groups; track g.layer) {
              <h3>{{ g.label }} ({{ g.items.length }})</h3>
              @for (r of g.items; track r.id) {
                <div class="line">
                  <span class="chip" title="Distance, en nombre de relations">{{ r.depth }}</span>
                  <a [routerLink]="['/objets', r.id]">{{ r.name || '(sans nom)' }}</a>
                  <span class="muted small">{{ types()[r.typeKey] ?? r.typeKey }}, via {{ r.via }}</span>
                  <a class="small right" routerLink="/impact" [queryParams]="{ objet: r.id }">Analyser</a>
                </div>
              }
            } @empty {
              <p class="muted">Rien à cette distance.</p>
            }
          </div>
        }
      </div>
    } @else {
      <div class="card muted">Aucun objet choisi. Cherchez-le par son nom, ou utilisez l'action « Impact » proposée sur les listes et les vues.</div>
    }
  `,
  styles: `
    h3 { margin: 14px 0 4px; font-size: 12.5px; color: var(--muted); font-weight: 600; }
    .line { display: flex; gap: 8px; align-items: baseline; padding: 4px 0; border-bottom: 1px solid var(--line); flex-wrap: wrap; }
  `,
})
export class ImpactPage {
  private api = inject(Api);
  private router = inject(Router);
  /** Objet analysé, porté par l'adresse (?objet=…) : l'analyse se partage par lien. */
  objet = input<string>();
  depth = signal(3);
  subject = signal<Brief | null>(null);
  reached = signal<Reached[]>([]);
  types = signal<Record<string, string>>({});
  private layers = signal<{ key: string; label: string; order: number }[]>([]);
  private applicationTypes = signal<string[]>([]);

  downstream = computed(() => this.reached().filter((r) => r.direction === 'aval'));
  upstream = computed(() => this.reached().filter((r) => r.direction === 'amont'));
  applicationsDown = computed(() => this.downstream().filter((r) => this.applicationTypes().includes(r.typeKey)).length);

  sides = computed(() => [
    { key: 'aval', title: 'Ce qui est touché s\'il tombe ou change', hint: 'Aval : ce que l\'objet sert, réalise ou alimente.', groups: this.group(this.downstream()) },
    { key: 'amont', title: 'Ce dont il dépend', hint: 'Amont : ce qui le sert, le réalise ou l\'alimente.', groups: this.group(this.upstream()) },
  ]);

  constructor() {
    this.api.get('/metamodel').then((m) => {
      this.types.set(Object.fromEntries(m.objectTypes.map((t: any) => [t.key, t.label])));
      this.layers.set(m.layers);
      this.applicationTypes.set(m.settings.applicationTypeKeys);
    });
    effect(() => {
      const id = this.objet();
      const depth = this.depth();
      if (!id) {
        this.subject.set(null);
        return this.reached.set([]);
      }
      this.api.get(`/objects/${id}`).then((o) => this.subject.set(o.brief)).catch(() => this.subject.set(null));
      this.api.get(`/analysis/impact/${id}?depth=${depth}&direction=tous`).then((r) => this.reached.set(r)).catch(() => this.reached.set([]));
    });
  }

  choose(o: Brief): void {
    this.router.navigate([], { queryParams: { objet: o.id } });
  }

  /** Regroupe par couche, dans l'ordre du métamodèle, puis par distance croissante. */
  private group(list: Reached[]): { layer: string; label: string; items: Reached[] }[] {
    return this.layers()
      .slice()
      .sort((a, b) => a.order - b.order)
      .map((l) => ({ layer: l.key, label: l.label, items: list.filter((r) => r.layer === l.key).sort((a, b) => a.depth - b.depth || a.name.localeCompare(b.name, 'fr')) }))
      .filter((g) => g.items.length > 0);
  }
}
