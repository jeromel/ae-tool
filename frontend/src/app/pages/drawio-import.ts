import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

interface Candidate { id: string; name: string; typeLabel: string | null; }
interface Item {
  key: string; page: string; kind: 'objet' | 'relation'; label: string;
  concept: string | null; conceptHow: string | null; typeKey: string | null; typeLabel: string | null;
  proposal: Action; targetId: string | null; reason: string; candidates: Candidate[];
  sourceKey: string | null; targetKey: string | null;
}
interface Analysis { file: string; pages: { id: string; name: string; shapes: number; links: number }[]; items: Item[]; anomalies: any[]; }
type Action = 'lier' | 'créer' | 'dessin';
interface Choice { action: Action; targetId: string | null; typeKey: string | null; }
type Filter = 'tout' | 'choisir' | 'lier' | 'créer' | 'dessin';

/**
 * Import d'un dessin draw.io : l'analyse propose, l'utilisateur ajuste forme par forme, puis l'import applique.
 * Rien n'est écrit avant le bouton « Importer ».
 */
@Component({
  imports: [RouterLink],
  template: `
    <h1>Importer un dessin draw.io</h1>
    <p class="lead">Chaque page du dessin devient une vue. Les formes reconnues sont reliées aux objets du référentiel ; les autres restent du dessin.
      Rien n'est créé sans votre accord, et les objets créés partent en validation.</p>
    @if (error()) { <div class="error">{{ error() }}</div> }

    <div class="card">
      <h2>1. Choisir le fichier</h2>
      <p class="muted small">Fichier .drawio ou .xml, ou image .drawio.svg enregistrée comme « SVG modifiable ». Les formes de la bibliothèque ArchiMate de draw.io
        sont reconnues ; une forme peut aussi porter, dans ses données, l'identifiant d'un objet (<code>ae.id</code>) ou son type (<code>archimate.type</code>).</p>
      <label class="btn">Choisir le fichier<input type="file" accept=".drawio,.xml,.svg" hidden (change)="analyse($event)" /></label>
      @if (busy()) { <span class="muted small" style="margin-left: 10px">Lecture en cours…</span> }
    </div>

    @if (analysis(); as a) {
      <div class="card">
        <h2>2. Vérifier les propositions <span class="muted small">· {{ a.file }}, {{ a.pages.length }} page(s)</span></h2>
        <div class="tools" style="flex-wrap: wrap; gap: 8px">
          @for (f of filters; track f.key) {
            <button type="button" class="btn" [class.ghost]="filter() !== f.key" (click)="filter.set(f.key)">{{ f.label }} ({{ count(f.key) }})</button>
          }
        </div>
        @if (a.anomalies.length) {
          <details style="margin: 10px 0"><summary>{{ a.anomalies.length }} point(s) à vérifier</summary>
            @for (x of a.anomalies; track $index) { <div class="small">{{ x.message }}</div> }
          </details>
        }
        <div class="scroll" style="max-height: 60vh">
          <table>
            <tr><th>Page</th><th>Forme ou lien</th><th>Reconnu comme</th><th style="width: 150px">Que faire</th><th style="width: 260px">Avec</th><th>Pourquoi</th></tr>
            @for (i of shown(); track i.key) {
              <tr>
                <td class="small muted">{{ i.page }}</td>
                <td>@if (i.kind === 'relation') { <span class="muted small">lien · </span> }{{ i.label || '(sans libellé)' }}</td>
                <td class="small">{{ i.concept ?? '—' }}@if (i.conceptHow) { <div class="muted">{{ i.conceptHow }}</div> }</td>
                <td>
                  <select (change)="setAction(i, $any($event.target).value)" [attr.aria-label]="'Que faire de ' + i.label">
                    <option value="lier" [disabled]="i.candidates.length === 0" [selected]="choice(i).action === 'lier'">Lier</option>
                    <option value="créer" [selected]="choice(i).action === 'créer'">Créer</option>
                    <option value="dessin" [selected]="choice(i).action === 'dessin'">Garder comme dessin</option>
                  </select>
                </td>
                <td>
                  @switch (choice(i).action) {
                    @case ('lier') {
                      <select (change)="setTarget(i, $any($event.target).value)" aria-label="Objet ou relation à lier">
                        @if (!choice(i).targetId) { <option value="">Choisir…</option> }
                        @for (c of i.candidates; track c.id) { <option [value]="c.id" [selected]="c.id === choice(i).targetId">{{ c.name }}@if (c.typeLabel) { — {{ c.typeLabel }} }</option> }
                      </select>
                    }
                    @case ('créer') {
                      <select (change)="setType(i, $any($event.target).value)" aria-label="Type à créer">
                        @if (!choice(i).typeKey) { <option value="">Choisir un type…</option> }
                        @for (t of (i.kind === 'objet' ? objectTypes() : relationTypes()); track t.key) { <option [value]="t.key" [selected]="t.key === choice(i).typeKey">{{ t.label }}</option> }
                      </select>
                    }
                    @default { <span class="muted small">reste une forme du dessin</span> }
                  }
                </td>
                <td class="small muted">{{ i.reason }}</td>
              </tr>
            } @empty { <tr><td colspan="6" class="muted">Rien dans cette catégorie.</td></tr> }
          </table>
        </div>
      </div>

      <div class="card">
        <h2>3. Importer</h2>
        @if (missing() > 0) { <p class="warn small">{{ missing() }} ligne(s) à compléter : choisissez l'objet à lier ou le type à créer, sinon elles resteront du dessin.</p> }
        <p class="small">{{ count('lier') }} lien(s) vers le référentiel, {{ count('créer') }} création(s), {{ count('dessin') }} forme(s) gardées comme dessin.</p>
        <button type="button" class="btn" [disabled]="busy()" (click)="apply()">Importer</button>
      </div>
    }

    @if (report(); as r) {
      <div class="card">
        <h2>Import terminé</h2>
        @for (c of entries(r.counts); track c[0]) {
          <div class="small">{{ c[0] }} : {{ c[1].added }} ajouté(s), {{ c[1].updated }} modifié(s), {{ c[1].unchanged }} inchangé(s)@if (c[1].archived) {, {{ c[1].archived }} archivé(s)}</div>
        }
        @for (n of r.notes; track $index) { <div class="small muted">{{ n }}</div> }
        @for (x of r.anomalies; track $index) { <div class="small" [class.error]="x.severity === 'erreur'">{{ x.message }}</div> }
        <p style="margin-top: 10px">
          @for (v of r.views ?? []; track v) { <a class="btn ghost" [routerLink]="['/vues', v]">Ouvrir la vue</a> }
          <a routerLink="/validations" style="margin-left: 8px">Objets en attente de validation</a>
        </p>
      </div>
    }
  `,
})
export class DrawioImportPage {
  private api = inject(Api);
  analysis = signal<Analysis | null>(null);
  report = signal<any | null>(null);
  choices = signal<Record<string, Choice>>({});
  filter = signal<Filter>('tout');
  busy = signal(false);
  error = signal('');
  objectTypes = signal<{ key: string; label: string }[]>([]);
  relationTypes = signal<{ key: string; label: string }[]>([]);
  private content = '';
  private fileName = '';

  readonly filters: { key: Filter; label: string }[] = [
    { key: 'tout', label: 'Tout' }, { key: 'choisir', label: 'À compléter' }, { key: 'lier', label: 'Liées' },
    { key: 'créer', label: 'À créer' }, { key: 'dessin', label: 'Dessin' },
  ];

  shown = computed(() => {
    const a = this.analysis();
    if (!a) return [];
    const f = this.filter();
    return a.items.filter((i) => f === 'tout' || (f === 'choisir' ? this.incomplete(i) : this.choice(i).action === f));
  });
  missing = computed(() => (this.analysis()?.items ?? []).filter((i) => this.incomplete(i)).length);

  constructor() {
    this.api.get('/metamodel').then((m) => {
      const byLabel = (a: { label: string }, b: { label: string }) => a.label.localeCompare(b.label);
      this.objectTypes.set(m.objectTypes.map((t: any) => ({ key: t.key, label: t.label })).sort(byLabel));
      this.relationTypes.set(m.relationshipTypes.map((t: any) => ({ key: t.key, label: t.label })).sort(byLabel));
    });
  }

  choice(i: Item): Choice {
    return this.choices()[i.key] ?? { action: i.proposal, targetId: i.targetId, typeKey: i.typeKey };
  }

  /** Ligne qui ne peut pas s'appliquer telle quelle : objet ambigu à départager, ou création sans type. */
  incomplete(i: Item): boolean {
    const c = this.choice(i);
    return (c.action === 'lier' && !c.targetId) || (c.action === 'créer' && !c.typeKey) || (c.action === 'dessin' && i.candidates.length > 1 && !this.choices()[i.key]);
  }

  count(f: Filter): number {
    const items = this.analysis()?.items ?? [];
    if (f === 'tout') return items.length;
    if (f === 'choisir') return items.filter((i) => this.incomplete(i)).length;
    return items.filter((i) => this.choice(i).action === f).length;
  }

  setAction(i: Item, action: Action): void {
    const c = this.choice(i);
    const targetId = action === 'lier' ? (c.targetId ?? (i.candidates.length === 1 ? i.candidates[0].id : null)) : null;
    this.choices.update((all) => ({ ...all, [i.key]: { ...c, action, targetId } }));
  }

  setTarget(i: Item, id: string): void {
    this.choices.update((all) => ({ ...all, [i.key]: { ...this.choice(i), targetId: id || null } }));
  }

  setType(i: Item, key: string): void {
    this.choices.update((all) => ({ ...all, [i.key]: { ...this.choice(i), typeKey: key || null } }));
  }

  entries(counts: Record<string, any>): [string, any][] {
    return Object.entries(counts);
  }

  async analyse(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.busy.set(true);
    this.error.set('');
    this.report.set(null);
    this.analysis.set(null);
    this.choices.set({});
    try {
      this.fileName = file.name;
      this.content = await toBase64(file);
      const a = await this.api.upload<Analysis>('/import/drawio/analyse?name=' + encodeURIComponent(file.name), file);
      this.analysis.set(a);
      this.filter.set(this.missing() > 0 ? 'choisir' : 'tout');
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      input.value = '';
      this.busy.set(false);
    }
  }

  async apply(): Promise<void> {
    const a = this.analysis();
    if (!a) return;
    this.busy.set(true);
    this.error.set('');
    // Une ligne incomplète reste du dessin : l'import ne devine jamais à la place de l'utilisateur.
    const decisions: Record<string, Choice> = {};
    for (const i of a.items) {
      const c = this.choice(i);
      decisions[i.key] = this.incomplete(i) ? { action: 'dessin', targetId: null, typeKey: null } : c;
    }
    try {
      this.report.set(await this.api.post('/import/drawio', { name: this.fileName, content: this.content, decisions }));
      this.analysis.set(null);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}

function toBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
    reader.onerror = () => reject(new Error('Lecture du fichier impossible.'));
    reader.readAsDataURL(file);
  });
}
