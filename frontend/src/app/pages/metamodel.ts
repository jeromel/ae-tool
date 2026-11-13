import { Component, inject, signal } from '@angular/core';
import { Api } from '../api';
import { GLYPH_KEYS } from '../glyphs';

/** Paramétrage du métamodèle : libellé, couche et visuel de chaque type, sans livraison de code. */
@Component({
  template: `
    <h1>Métamodèle</h1>
    <p class="lead">
      Types d'objets, couches, visuels et grille de notation sont des données. Le gabarit par défaut suit ArchiMate ; un type sans
      correspondance ArchiMate est exporté vers Archi avec le type de repli.
    </p>
    @if (error()) { <div class="error">{{ error() }}</div> }
    @if (m(); as m) {
      <div class="tools">
        <a class="btn ghost" [href]="api.url('/metamodel/export')">Exporter le métamodèle (JSON)</a>
        <label class="btn ghost">Importer un métamodèle<input type="file" accept=".json" hidden (change)="importFile($event)" /></label>
        <label><input type="checkbox" (change)="onlyUsed.set($any($event.target).checked)" checked /> Types utilisés seulement</label>
        @if (saved()) { <span class="chip ok">Enregistré</span> }
      </div>
      <div class="card">
        <h2>Types d'objets et visuels</h2>
        <div class="scroll">
          <table>
            <tr><th>Clé</th><th>Libellé</th><th>Couche</th><th>Type ArchiMate</th><th>Fond</th><th>Bordure</th><th>Forme</th><th>Gabarit</th><th>Pictogramme</th><th class="num">Objets</th></tr>
            @for (t of types(); track t.key) {
              <tr>
                <td class="muted small">{{ t.key }}</td>
                <td><input type="text" [value]="t.label" (change)="t.label = $any($event.target).value; save(t)" /></td>
                <td>
                  <select (change)="t.layerKey = $any($event.target).value; save(t)">
                    @for (l of m.layers; track l.key) { <option [value]="l.key" [selected]="t.layerKey === l.key">{{ l.label }}</option> }
                  </select>
                </td>
                <td class="muted">{{ t.archimateType ?? 'repli : ' + m.settings.fallbackArchimateType }}</td>
                <td><input type="color" [value]="t.visual.fill ?? '#ffffff'" (change)="t.visual.fill = $any($event.target).value; save(t)" /></td>
                <td><input type="color" [value]="t.visual.stroke ?? '#5c5c5c'" (change)="t.visual.stroke = $any($event.target).value; save(t)" /></td>
                <td>
                  <select (change)="t.visual.shape = $any($event.target).value; save(t)">
                    @for (s of shapes; track s) { <option [selected]="t.visual.shape === s">{{ s }}</option> }
                  </select>
                </td>
                <td>
                  <select (change)="t.visual.template = $any($event.target).value || null; save(t)">
                    <option value="" [selected]="!t.visual.template">par défaut</option>
                    @for (s of templates; track s) { <option [selected]="t.visual.template === s">{{ s }}</option> }
                  </select>
                </td>
                <td>
                  <select (change)="t.visual.icon = $any($event.target).value || null; save(t)">
                    <option value="" [selected]="!t.visual.icon">par défaut</option>
                    @for (g of glyphs; track g) { <option [selected]="t.visual.icon === g">{{ g }}</option> }
                  </select>
                </td>
                <td class="num">{{ counts()[t.key] ?? 0 }}</td>
              </tr>
            }
          </table>
        </div>
      </div>
      <div class="card">
        <h2>Relations dans le rendu soigné des vues</h2>
        <p class="muted small">« Par défaut » applique la notation ArchiMate du type. Le rendu « Géométrie Archi exacte » n'est pas concerné.</p>
        <table>
          <tr><th>Relation</th><th>Libellé</th><th>Trait</th><th>Tête</th></tr>
          @for (r of m.relationshipTypes; track r.key) {
            <tr>
              <td class="muted small">{{ r.key }}</td>
              <td><input type="text" [value]="r.label" (change)="r.label = $any($event.target).value; saveRelation(r)" /></td>
              <td>
                <select (change)="r.softLine = $any($event.target).value || null; saveRelation(r)">
                  <option value="" [selected]="!r.softLine">par défaut</option>
                  @for (l of lines; track l) { <option [selected]="r.softLine === l">{{ l }}</option> }
                </select>
              </td>
              <td>
                <select (change)="r.head = $any($event.target).value || null; saveRelation(r)">
                  <option value="" [selected]="!r.head">par défaut</option>
                  @for (h of heads; track h) { <option [selected]="r.head === h">{{ h }}</option> }
                </select>
              </td>
            </tr>
          }
        </table>
      </div>
      <div class="row">
        <div class="card">
          <h2>Grille de notation</h2>
          <table>
            <tr><th>Critère</th><th>Noté par</th><th class="num">Sous-critères</th><th class="num">Total des poids</th></tr>
            @for (c of m.scoring.criteria; track c.code) {
              <tr><td>{{ c.code }} · {{ c.label }}</td><td>{{ c.scoredBy }}</td><td class="num">{{ c.subCriteria.length }}</td><td class="num">{{ total(c) }}</td></tr>
            }
          </table>
          <p class="muted small">La grille et les seuils sont repris du classeur d'urbanisation à chaque import, ou modifiables par l'API (PUT /api/metamodel/scoring).</p>
        </div>
        <div class="card">
          <h2>Seuils</h2>
          <dl class="kv">
            <dt>Note 2 / 3 / 4 à partir de</dt><dd>{{ pct(m.scoring.thresholds.note2) }} / {{ pct(m.scoring.thresholds.note3) }} / {{ pct(m.scoring.thresholds.note4) }}</dd>
            <dt>Maîtrise technique</dt><dd>moyenne de {{ m.scoring.masteryCriteria.join(' et ') }}, seuil {{ m.scoring.thresholds.mastery }}</dd>
            <dt>Priorité</dt><dd>haute si U &gt; {{ m.scoring.thresholds.priorityHigh }}, moyenne si U ≥ {{ m.scoring.thresholds.priorityMedium }}</dd>
            <dt>Règle de garde</dt><dd>CM ≥ {{ m.scoring.thresholds.guardCm }} et besoin non maintenu : à instruire</dd>
            <dt>Projets</dt><dd>type {{ m.settings.projectTypeKey }}, dossier « {{ m.settings.projectFolderLabel }} »</dd>
            <dt>Fond de carte (hors impacts)</dt><dd>{{ m.settings.impactExcludedTypeKeys.join(', ') }}</dd>
          </dl>
        </div>
      </div>
      <div class="card">
        <h2>Propriétés ({{ m.propertyDefinitions.length }})</h2>
        <div class="scroll" style="max-height: 260px">
          <table>
            <tr><th>Clé</th><th>Libellé</th><th>Source</th><th title="Proposée dans le menu Affichage des vues">Calque sur les vues</th></tr>
            @for (p of m.propertyDefinitions; track p.key) {
              <tr><td>{{ p.key || '(vide)' }}</td><td>{{ p.label }}</td><td>{{ p.source }}</td>
                <td><input type="checkbox" [checked]="p.displayable" [attr.aria-label]="'Proposer ' + p.label + ' en calque'" (change)="setDisplayable(p, $any($event.target).checked)" /></td></tr>
            }
          </table>
        </div>
      </div>
    }
  `,
})
export class MetamodelPage {
  api = inject(Api);
  m = signal<any>(null);
  counts = signal<Record<string, number>>({});
  onlyUsed = signal(true);
  saved = signal(false);
  error = signal('');
  shapes = ['rectangle', 'arrondi', 'ellipse'];
  templates = ['carte', 'rond', 'zone'];
  glyphs = GLYPH_KEYS;
  lines = ['plein', 'tirets', 'pointillés'];
  heads = ['fleche', 'ouverte', 'triangle', 'losange', 'losange-creux', 'point', 'aucune'];

  constructor() {
    this.load();
  }

  load(): void {
    this.api.get('/metamodel').then((m) => this.m.set(m));
    this.api.get('/summary').then((s) => {
      const counts: Record<string, number> = {};
      for (const t of s.layers.flatMap((l: any) => l.types)) counts[t.key] = t.count;
      this.counts.set(counts);
    });
  }

  types(): any[] {
    const order = new Map<string, number>(this.m().layers.map((l: any) => [l.key, l.order]));
    return (this.m().objectTypes as any[])
      .filter((t) => !this.onlyUsed() || this.counts()[t.key])
      .sort((a, b) => (order.get(a.layerKey) ?? 99) - (order.get(b.layerKey) ?? 99) || a.label.localeCompare(b.label));
  }

  total(c: any): number {
    return c.subCriteria.reduce((sum: number, s: any) => sum + s.weight, 0);
  }

  pct(v: number): string {
    return Math.round(v * 100) + ' %';
  }

  async save(type: any): Promise<void> {
    this.error.set('');
    try {
      await this.api.put(`/metamodel/object-types/${encodeURIComponent(type.key)}`, type);
      this.saved.set(true);
      this.m.set({ ...this.m() });
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  async saveRelation(type: any): Promise<void> {
    await this.run(() => this.api.put(`/metamodel/relationship-types/${encodeURIComponent(type.key)}`, type));
  }

  async setDisplayable(property: any, displayable: boolean): Promise<void> {
    property.displayable = displayable;
    await this.run(() => this.api.put('/metamodel/properties/displayable', { key: property.key, displayable }));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.error.set('');
    try {
      await action();
      this.saved.set(true);
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  async importFile(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.error.set('');
    try {
      await this.api.upload('/metamodel/import', file);
      this.load();
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
}
