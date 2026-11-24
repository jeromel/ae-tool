import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

interface Candidate { id: string; name: string; typeLabel: string | null; }
type Action = 'lier' | 'créer' | 'ignorer';
interface Item { code: string; kind: 'application' | 'donnee' | 'flux-technique' | 'flux' | 'externe'; label: string; detail: string | null;
  proposal: Action; targetId: string | null; reason: string; candidates: Candidate[]; }
interface Analysis { file: string; sheets: Record<string, number>; items: Item[]; anomalies: { severity: string; code: string; message: string; ref: string | null }[]; }
interface Choice { action: Action; targetId: string | null; }

const KINDS: { key: Item['kind'] | 'tout' | 'choisir'; label: string }[] = [
  { key: 'tout', label: 'Tout' }, { key: 'choisir', label: 'À vérifier' }, { key: 'application', label: 'Applications' }, { key: 'donnee', label: 'Objets de données' },
  { key: 'flux', label: 'Flux applicatifs' }, { key: 'flux-technique', label: 'Flux techniques' }, { key: 'externe', label: 'Destinataires externes' },
];
const KIND_LABEL: Record<string, string> = {
  application: 'Application', donnee: 'Objet de données', flux: 'Flux applicatif', 'flux-technique': 'Flux technique', externe: 'Destinataire externe',
};

/**
 * Import du catalogue des données et des flux (classeur Excel). L'analyse rapproche chaque ligne du référentiel par son code,
 * puis par son nom ; l'utilisateur confirme ou corrige ligne par ligne, puis l'import applique. Rien n'est écrit avant « Importer ».
 */
@Component({
  imports: [RouterLink],
  template: `
    <h1>Importer le catalogue des données et des flux</h1>
    <p class="lead">Applications, objets de données et leurs attributs, flux applicatifs, flux techniques et traçabilité entre les deux.
      Chaque ligne est rapprochée du référentiel par son code, puis par son nom ; ce qui est créé part en validation. L'import se rejoue :
      un code déjà connu met à jour l'objet ou le flux qui le porte.</p>
    @if (error()) { <div class="error">{{ error() }}</div> }

    <div class="card">
      <h2>1. Choisir le classeur</h2>
      <p class="muted small">Onglets attendus : Applications, Objets de données, Attributs, Flux applicatifs, Flux techniques, Traçabilité FA-FT, et Listes (valeurs de référence).
        La matrice des flux applicatifs par flux techniques n'est pas lue : l'outil la déduit de la traçabilité.</p>
      <label class="btn">Choisir le classeur<input type="file" accept=".xlsx" hidden (change)="analyse($event)" /></label>
      @if (busy()) { <span class="muted small" style="margin-left: 10px">Lecture en cours…</span> }
    </div>

    @if (analysis(); as a) {
      <div class="card">
        <h2>2. Vérifier les rapprochements <span class="muted small">· {{ a.file }}</span></h2>
        <p class="small muted">@for (s of sheetEntries(a); track s[0]; let last = $last) { {{ s[0] }} : {{ s[1] }} ligne(s){{ last ? '.' : ', ' }} }</p>
        <div class="tools" style="flex-wrap: wrap; gap: 8px">
          @for (k of kinds; track k.key) {
            @if (count(k.key) > 0 || k.key === 'tout') {
              <button type="button" class="btn" [class.ghost]="filter() !== k.key" (click)="filter.set(k.key)">{{ k.label }} ({{ count(k.key) }})</button>
            }
          }
        </div>
        @if (a.anomalies.length) {
          <details style="margin: 10px 0" open><summary>{{ a.anomalies.length }} point(s) relevé(s) dans le classeur</summary>
            @for (x of a.anomalies; track $index) { <div class="small" [class.error]="x.severity === 'erreur'">@if (x.ref) { <b>{{ x.ref }}</b> : }{{ x.message }}</div> }
          </details>
        }
        <div class="scroll" style="max-height: 60vh">
          <table>
            <tr><th>Code</th><th>Ligne du catalogue</th><th>Nature</th><th style="width: 140px">Que faire</th><th style="width: 280px">Avec</th><th>Pourquoi</th></tr>
            @for (i of shown(); track i.code + i.kind) {
              <tr>
                <td class="small"><b>{{ i.code }}</b></td>
                <td>{{ i.label }}@if (i.detail) { <div class="muted small">{{ i.detail }}</div> }</td>
                <td class="small muted">{{ kindLabel(i.kind) }}</td>
                <td>
                  <select (change)="setAction(i, $any($event.target).value)" [attr.aria-label]="'Que faire de ' + i.label">
                    <option value="lier" [disabled]="i.candidates.length === 0" [selected]="choice(i).action === 'lier'">Lier</option>
                    <option value="créer" [selected]="choice(i).action === 'créer'">Créer</option>
                    <option value="ignorer" [selected]="choice(i).action === 'ignorer'">Ignorer</option>
                  </select>
                </td>
                <td>
                  @switch (choice(i).action) {
                    @case ('lier') {
                      <select (change)="setTarget(i, $any($event.target).value)" aria-label="Élément du référentiel à lier">
                        @if (!choice(i).targetId) { <option value="">Choisir…</option> }
                        @for (c of i.candidates; track c.id) { <option [value]="c.id" [selected]="c.id === choice(i).targetId">{{ c.name }}@if (c.typeLabel) { — {{ c.typeLabel }} }</option> }
                      </select>
                    }
                    @case ('créer') {
                      <span class="muted small">créé{{ i.kind === 'flux' ? '' : ' et proposé à la validation' }}@if (i.candidates.length) {, ou à lier ({{ i.candidates.length }} proche(s))}</span>
                    }
                    @default { <span class="muted small">ligne non reprise</span> }
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
        <p class="small">{{ total('lier') }} lien(s) vers le référentiel, {{ total('créer') }} création(s), {{ total('ignorer') }} ligne(s) ignorée(s).
          Une sauvegarde du référentiel est prise avant l'import.</p>
        @if (unresolved() > 0) { <p class="warn small">{{ unresolved() }} ligne(s) « Lier » sans élément choisi : elles seront créées.</p> }
        <button type="button" class="btn" [disabled]="busy()" (click)="apply()">Importer</button>
      </div>
    }

    @if (report(); as r) {
      <div class="card">
        <h2>Import terminé</h2>
        @for (c of entries(r.counts); track c[0]) {
          <div class="small">{{ c[0] }} : {{ c[1].added }} ajouté(s), {{ c[1].updated }} modifié(s), {{ c[1].unchanged }} inchangé(s)</div>
        }
        @for (n of r.notes; track $index) { <div class="small muted">{{ n }}</div> }
        @for (x of r.anomalies; track $index) { <div class="small" [class.error]="x.severity === 'erreur'">@if (x.ref) { <b>{{ x.ref }}</b> : }{{ x.message }}</div> }
        <p style="margin-top: 10px">
          <a routerLink="/qualite" fragment="catalogue">Contrôles de cohérence du catalogue</a>
          <span class="muted"> · </span><a routerLink="/donnees-personnelles">Registre des données personnelles</a>
          <span class="muted"> · </span><a routerLink="/donnees">Parcourir les données</a>
        </p>
      </div>
    }
  `,
})
export class CatalogueImportPage {
  private api = inject(Api);
  analysis = signal<Analysis | null>(null);
  report = signal<any | null>(null);
  choices = signal<Record<string, Choice>>({});
  filter = signal<string>('tout');
  busy = signal(false);
  error = signal('');
  readonly kinds = KINDS;
  private content = '';
  private fileName = '';

  shown = computed(() => {
    const a = this.analysis();
    if (!a) return [];
    const f = this.filter();
    return a.items.filter((i) => f === 'tout' || (f === 'choisir' ? this.toCheck(i) : i.kind === f));
  });
  unresolved = computed(() => (this.analysis()?.items ?? []).filter((i) => this.choice(i).action === 'lier' && !this.choice(i).targetId).length);

  kindLabel(k: string): string { return KIND_LABEL[k] ?? k; }

  key(i: Item): string { return i.code; }

  choice(i: Item): Choice {
    return this.choices()[this.key(i)] ?? { action: i.proposal, targetId: i.targetId };
  }

  /** À vérifier : création alors que des noms proches existent, ou plusieurs candidats pour un lien. */
  toCheck(i: Item): boolean {
    const c = this.choice(i);
    return (c.action === 'créer' && i.candidates.length > 0) || (c.action === 'lier' && (!c.targetId || i.candidates.length > 1));
  }

  count(k: string): number {
    const items = this.analysis()?.items ?? [];
    if (k === 'tout') return items.length;
    if (k === 'choisir') return items.filter((i) => this.toCheck(i)).length;
    return items.filter((i) => i.kind === k).length;
  }

  total(action: Action): number {
    return (this.analysis()?.items ?? []).filter((i) => this.choice(i).action === action).length;
  }

  setAction(i: Item, action: Action): void {
    const c = this.choice(i);
    const targetId = action === 'lier' ? (c.targetId ?? (i.candidates.length === 1 ? i.candidates[0].id : null)) : null;
    this.choices.update((all) => ({ ...all, [this.key(i)]: { action, targetId } }));
  }

  setTarget(i: Item, id: string): void {
    this.choices.update((all) => ({ ...all, [this.key(i)]: { ...this.choice(i), targetId: id || null } }));
  }

  sheetEntries(a: Analysis): [string, number][] { return Object.entries(a.sheets); }
  entries(counts: Record<string, any>): [string, any][] { return Object.entries(counts); }

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
      const a = await this.api.upload<Analysis>('/import/catalogue/analyse?name=' + encodeURIComponent(file.name), file);
      this.analysis.set(a);
      this.filter.set(a.items.some((i) => this.toCheck(i)) ? 'choisir' : 'tout');
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
    const decisions: Record<string, Choice> = {};
    for (const i of a.items) {
      const c = this.choice(i);
      decisions[this.key(i)] = c.action === 'lier' && !c.targetId ? { action: 'créer', targetId: null } : c;
    }
    try {
      this.report.set(await this.api.post('/import/catalogue', { name: this.fileName, content: this.content, decisions }));
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
