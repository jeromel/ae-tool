import { Component, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, SOURCE_LABELS } from '../api';

/** Fiche d'un objet : propriétés, relations, vues, projets, analyse d'impact et visuel propre à l'élément. */
@Component({
  imports: [RouterLink],
  template: `
    @if (d(); as d) {
      <a routerLink="/referentiel">← Tous les objets</a>
      <h1><span class="swatch" [style.background]="d.effectiveVisual.fill"></span>{{ d.brief.name || '(sans nom)' }}</h1>
      <p class="lead">
        <span class="chip info">{{ d.brief.typeLabel }}</span> <span class="chip">{{ d.brief.layerLabel }}</span>
        <span class="chip">Source : {{ sources[d.brief.source] ?? d.brief.source }}</span> <span class="chip">{{ d.brief.status }}</span>
        @if (d.object.archived) { <span class="chip bad">Archivé</span> }
        @if (d.hasAssessment) { <a [routerLink]="['/portefeuille', d.brief.id]">Voir la notation</a> }
        <a routerLink="/impact" [queryParams]="{ objet: d.brief.id }">Analyser l'impact</a>
      </p>
      <div class="row">
        <div class="card">
          <h2>Description et propriétés</h2>
          @if (d.object.documentation) { <p style="white-space: pre-wrap">{{ d.object.documentation }}</p> }
          <dl class="kv">
            @for (p of d.properties; track $index) { <dt>{{ p.label }}</dt><dd>{{ p.value }}</dd> }
            <dt>Identifiant</dt><dd class="small muted">{{ d.brief.id }}</dd>
            @for (f of d.folders; track f) { <dt>Dossier</dt><dd>{{ f }}</dd> }
          </dl>
        </div>
        <div class="card" style="max-width: 380px">
          <h2>Visuel de cet élément</h2>
          <p class="muted small">Par défaut, l'élément prend le visuel de son type. Un réglage ici ne vaut que pour lui, dans toutes les vues affichées avec les couleurs du métamodèle.</p>
          <dl class="kv" style="grid-template-columns: 110px 1fr">
            <dt>Couleur de fond</dt>
            <dd><input type="color" [value]="d.effectiveVisual.fill" (change)="setVisual({ fill: $any($event.target).value })" />
              @if (d.object.visualOverride?.fill) { <span class="chip warn">propre à l'élément</span> } @else { <span class="chip">du type</span> }</dd>
            <dt>Forme</dt>
            <dd>
              <select (change)="setVisual({ shape: $any($event.target).value })">
                @for (s of shapes; track s) { <option [selected]="d.effectiveVisual.shape === s">{{ s }}</option> }
              </select>
            </dd>
          </dl>
          @if (d.object.visualOverride) { <button class="ghost" style="margin-top: 10px" (click)="resetVisual()">Revenir au visuel du type</button> }
          @if (d.projects.length) {
            <h2 style="margin-top: 18px">Projets qui touchent cet objet</h2>
            @for (p of d.projects; track p.projectId) {
              <div><a [routerLink]="['/projets', p.projectId]">{{ p.projectName }}</a> <span class="chip">{{ p.nature }}</span></div>
            }
          }
          @if (d.diagrams.length) {
            <h2 style="margin-top: 18px">Présent dans {{ d.diagrams.length }} vue(s)</h2>
            <div style="max-height: 180px; overflow: auto">
              @for (v of d.diagrams; track v.id) { <div><a [routerLink]="['/vues', v.id]">{{ v.name }}</a></div> }
            </div>
          }
        </div>
      </div>
      <div class="row">
        <div class="card">
          <h2>Relations sortantes ({{ d.outgoing.length }})</h2>
          <table>
            @for (r of d.outgoing; track r.id) {
              <tr><td class="muted" style="width: 130px">{{ r.typeLabel }} →</td>
                <td><a [routerLink]="['/objets', r.other.id]">{{ r.other.name }}</a> <span class="muted small">{{ r.other.typeLabel }}</span>
                  @if (r.name) { <div class="small muted">{{ r.name }}</div> }</td></tr>
            } @empty { <tr><td class="muted">Aucune.</td></tr> }
          </table>
        </div>
        <div class="card">
          <h2>Relations entrantes ({{ d.incoming.length }})</h2>
          <table>
            @for (r of d.incoming; track r.id) {
              <tr><td><a [routerLink]="['/objets', r.other.id]">{{ r.other.name }}</a> <span class="muted small">{{ r.other.typeLabel }}</span>
                  @if (r.name) { <div class="small muted">{{ r.name }}</div> }</td>
                <td class="muted" style="width: 130px">→ {{ r.typeLabel }}</td></tr>
            } @empty { <tr><td class="muted">Aucune.</td></tr> }
          </table>
        </div>
      </div>
      <div class="card">
        <div class="tools">
          <h2 style="margin: 0">Analyse d'impact</h2>
          <label>Profondeur
            <select (change)="depth.set(+$any($event.target).value); loadImpact()">
              @for (n of [1, 2, 3, 4]; track n) { <option [selected]="depth() === n">{{ n }}</option> }
            </select>
          </label>
          <span class="muted small">Aval : ce que l'objet sert, réalise ou alimente. Amont : ce dont il dépend.</span>
        </div>
        <div class="row">
          @for (dir of ['amont', 'aval']; track dir) {
            <div style="flex: 1; min-width: 280px">
              <b>{{ dir === 'amont' ? 'Amont' : 'Aval' }} ({{ side(dir).length }})</b>
              <div class="scroll" style="max-height: 300px">
                <table>
                  @for (r of side(dir); track r.id) {
                    <tr><td class="num muted" style="width: 30px">{{ r.depth }}</td>
                      <td><a [routerLink]="['/objets', r.id]">{{ r.name }}</a> <span class="muted small">{{ r.typeKey }} · via {{ r.via }}</span></td></tr>
                  } @empty { <tr><td class="muted">Rien à cette profondeur.</td></tr> }
                </table>
              </div>
            </div>
          }
        </div>
      </div>
    }
  `,
})
export class ObjectPage {
  private api = inject(Api);
  private router = inject(Router);
  id = input.required<string>();
  d = signal<any>(null);
  impact = signal<any[]>([]);
  depth = signal(2);
  sources = SOURCE_LABELS;
  shapes = ['rectangle', 'arrondi', 'ellipse'];

  constructor() {
    effect(() => {
      this.id();
      this.load();
    });
  }

  load(): void {
    this.api.get(`/objects/${this.id()}`).then((d) => this.d.set(d)).catch(() => this.router.navigate(['/referentiel']));
    this.loadImpact();
  }

  loadImpact(): void {
    this.api.get(`/analysis/impact/${this.id()}?depth=${this.depth()}&direction=tous`).then((r) => this.impact.set(r));
  }

  side(direction: string): any[] {
    return this.impact().filter((r) => r.direction === direction);
  }

  async setVisual(change: Record<string, string>): Promise<void> {
    const current = this.d().object.visualOverride ?? {};
    await this.api.put(`/objects/${this.id()}`, { visualOverride: { ...current, ...change } });
    this.load();
  }

  async resetVisual(): Promise<void> {
    await this.api.put(`/objects/${this.id()}`, { clearVisualOverride: true });
    this.load();
  }
}
