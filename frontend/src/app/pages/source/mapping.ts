import { Component, computed, inject, signal } from '@angular/core';
import { SourceState } from './model';
import { ColumnPick, TypePick } from './fields';

const NEW_PROPERTY = '__nouvelle';

/** Matrice de transcodification de la source : colonnes d'identité, nature → type du métamodèle, colonnes reprises. */
@Component({
  selector: 'app-mapping-tab',
  imports: [ColumnPick, TypePick],
  template: `
    @if (state.mapping(); as m) {
      <div class="card">
        <h2>Aperçu de la source</h2>
        <p class="muted small">L'aperçu lit la source sans rien enregistrer ni comparer. Il alimente les listes de colonnes ci-dessous ; sans aperçu, saisissez les noms de colonnes à la main.</p>
        <div class="tools">
          @if (state.profile()!.kind === 'fichier') {
            <label class="btn ghost">Choisir un fichier CSV ou Excel
              <input type="file" accept=".csv,.txt,.xlsx" hidden (change)="pick($event)" />
            </label>
            <span class="muted">{{ state.file()?.name ?? 'Aucun fichier choisi' }}</span>
          }
          <button type="button" [disabled]="previewing() || (state.profile()!.kind === 'fichier' && !state.file())" (click)="preview()">Charger un aperçu</button>
          @if (previewing()) { <span class="muted">Lecture en cours…</span> }
        </div>
        @if (previewError()) { <div class="error" role="alert">{{ previewError() }}</div> }
        @if (state.preview(); as pv) {
          <p class="small muted">
            {{ pv.columns.length }} colonne(s) trouvée(s), {{ pv.rowCount }} ligne(s) lue(s){{ pv.total !== null ? ' sur ' + pv.total : '' }}.
            Les cinq premières lignes :
          </p>
          <div class="scroll preview">
            <table>
              <tr>@for (c of pv.columns; track c) { <th>{{ c }}</th> }</tr>
              @for (r of pv.rows; track $index) { <tr>@for (c of pv.columns; track c) { <td>{{ r[c] }}</td> }</tr> }
            </table>
          </div>
          @if (pv.missingColumns.length) {
            <div class="note warn-note">Colonnes du mappage enregistré absentes de la source : {{ pv.missingColumns.join(', ') }}. Choisissez ci-dessous les colonnes qui conviennent.</div>
          }
          @if (!pv.missingColumns.length) { @for (i of pv.issues.slice(0, 5); track $index) { <div class="small muted">{{ i.message }}</div> } }
        }
      </div>

      <div class="card" [class.has-error]="errorIn() === 'identite'">
        <h2>Identité des lignes</h2>
        @if (errorIn() === 'identite') { <div class="error" role="alert">{{ error() }}</div> }
        <div class="grid">
          <label>Colonne de l'identifiant <app-column-pick [(value)]="m.idField" label="Colonne de l'identifiant" [columns]="state.columns()" (valueChange)="touch()" /></label>
          <label>Colonne du nom <app-column-pick [(value)]="m.nameField" label="Colonne du nom" [columns]="state.columns()" (valueChange)="touch()" /></label>
          <label>Colonne de la nature <span class="muted small">(facultative)</span>
            <app-column-pick [(value)]="m.typeField" label="Colonne de la nature" [optional]="true" [columns]="state.columns()" (valueChange)="touch()" /></label>
        </div>
        <label class="check">
          <input type="checkbox" [checked]="state.master()" (change)="state.master.set($any($event.target).checked); touch()" />
          <span>La source fait foi pour le nom
            <span class="muted small"><br />À l'application, le nom des objets liés est aligné sur celui de la source. Décochée, le nom du référentiel est conservé et l'écart est seulement signalé.</span></span>
        </label>
      </div>

      <div class="card" [class.has-error]="errorIn() === 'types'">
        <h2>Nature de la source → type du métamodèle</h2>
        <p class="muted small">Chaque valeur de la colonne de nature est reliée à un type du métamodèle. Le type par défaut sert aux lignes sans nature ou dont la nature n'est pas listée ; sans lui, ces lignes sont « invalides ».</p>
        @if (errorIn() === 'types') { <div class="error" role="alert">{{ error() }}</div> }
        <table class="form-table">
          <tr><th>Valeur dans la source</th><th>Type du métamodèle</th><th></th></tr>
          @for (t of m.types; track $index) {
            <tr>
              <td><input type="text" [attr.aria-label]="'Valeur de la nature, ligne ' + ($index + 1)" [value]="t.sourceValue" (input)="t.sourceValue = $any($event.target).value; touch()" /></td>
              <td><app-type-pick [(value)]="t.typeKey" [layers]="state.layers()" [types]="state.types()" [label]="'Type de la ligne ' + ($index + 1)" (valueChange)="touch()" /></td>
              <td class="actions"><button type="button" class="ghost" (click)="removeType($index)">Retirer</button></td>
            </tr>
          }
          <tr class="default">
            <td><b>Type par défaut</b></td>
            <td><app-type-pick [(value)]="m.defaultTypeKey" [layers]="state.layers()" [types]="state.types()" empty="Aucun" label="Type par défaut" (valueChange)="touch()" /></td>
            <td></td>
          </tr>
        </table>
        <div class="tools top">
          <button type="button" class="ghost" (click)="addType('')">Ajouter une valeur</button>
          @if (suggestions().length) {
            <span class="muted small">Valeurs vues dans l'aperçu :</span>
            @for (s of suggestions(); track s) { <button type="button" class="question" (click)="addType(s)">+ {{ s }}</button> }
          }
        </div>
      </div>

      <div class="card" [class.has-error]="errorIn() === 'champs'">
        <h2>Colonnes reprises</h2>
        <p class="muted small">Chaque colonne choisie alimente une propriété des objets liés. Une propriété qui n'existe pas encore est créée à l'application, avec le libellé indiqué.</p>
        @if (errorIn() === 'champs') { <div class="error" role="alert">{{ error() }}</div> }
        @if (m.fields.length) {
          <table class="form-table">
            <tr><th>Colonne de la source</th><th>Propriété du référentiel</th><th>Libellé</th><th></th></tr>
            @for (f of m.fields; track $index) {
              <tr>
                <td><app-column-pick [(value)]="f.sourceField" [label]="'Colonne reprise ' + ($index + 1)" [columns]="state.columns()" (valueChange)="touch()" /></td>
                <td>
                  <select [attr.aria-label]="'Propriété de la ligne ' + ($index + 1)" (change)="chooseProperty(f, $any($event.target).value)">
                    @if (!f.propertyKey && !f.creating) { <option value="" selected>Choisir une propriété…</option> }
                    @for (p of state.properties(); track p.key) { <option [value]="p.key" [selected]="!f.creating && p.key === f.propertyKey">{{ p.label }} ({{ p.key }})</option> }
                    <option [value]="newProperty" [selected]="isNew(f)">Nouvelle propriété…</option>
                  </select>
                  @if (isNew(f)) {
                    <input type="text" class="newkey" [attr.aria-label]="'Clé de la nouvelle propriété, ligne ' + ($index + 1)" placeholder="cmdb:ma-propriete"
                      [value]="f.propertyKey" (input)="f.propertyKey = $any($event.target).value; touch()" />
                  }
                </td>
                <td><input type="text" [attr.aria-label]="'Libellé de la ligne ' + ($index + 1)" [value]="f.label" placeholder="Libellé affiché" (input)="f.label = $any($event.target).value; touch()" /></td>
                <td class="actions"><button type="button" class="ghost" (click)="removeField($index)">Retirer</button></td>
              </tr>
            }
          </table>
        }
        <div class="tools top"><button type="button" class="ghost" (click)="addField()">Ajouter une colonne reprise</button></div>
      </div>

      <div class="save">
        <button type="button" [disabled]="busy() || !state.mappingDirty()" (click)="save()">Enregistrer</button>
        @if (state.mappingDirty()) { <span class="chip warn">Modifications non enregistrées</span> }
        @else if (saved()) { <span class="chip ok">Enregistré</span> }
        @if (errorIn() === 'general') { <div class="error" role="alert">{{ error() }}</div> }
      </div>
    }
  `,
  styles: `
    .preview { max-height: 230px; border: 1px solid var(--line); border-radius: 6px; }
    .preview td { white-space: nowrap; max-width: 280px; overflow: hidden; text-overflow: ellipsis; }
    .warn-note { background: #fbe9cc; color: var(--warn); margin-top: 10px; }
    .grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px 16px; margin-bottom: 14px; }
    .grid label { display: flex; flex-direction: column; gap: 4px; font-weight: 600; }
    .check { display: flex; gap: 10px; align-items: flex-start; }
    .check input { margin-top: 3px; }
    .form-table input[type=text], .form-table select { width: 100%; min-width: 150px; }
    .form-table input.newkey { margin-top: 6px; }
    .form-table tr.default td { background: #f6f8f8; }
    .tools.top { margin: 10px 0 0; }
    .has-error { border-color: var(--bad); }
    .save { position: sticky; bottom: 0; display: flex; gap: 12px; align-items: center; flex-wrap: wrap; padding: 12px 0; background: var(--bg); }
    .save .error { margin: 0; }
    .question { padding: 3px 10px; font-size: 12.5px; }
    @media (max-width: 900px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class MappingTab {
  readonly state = inject(SourceState);
  readonly newProperty = NEW_PROPERTY;
  busy = signal(false);
  saved = signal(false);
  previewing = signal(false);
  previewError = signal('');
  error = signal('');
  errorIn = computed(() => this.section(this.error()));

  /** Valeurs de nature vues dans les lignes de l'aperçu et pas encore associées à un type. */
  suggestions = computed(() => {
    const pv = this.state.preview(), m = this.state.mapping();
    if (!pv || !m?.typeField) return [];
    const known = new Set(m.types.map((t) => t.sourceValue.trim().toLowerCase()));
    const seen = pv.rows.map((r) => (r[m.typeField] ?? '').trim()).filter((v) => v);
    return [...new Set(seen)].filter((v) => !known.has(v.toLowerCase()));
  });

  /** Le mappage est modifié en place (objets du brouillon) : on republie le signal pour que l'état « modifié » suive. */
  touch(): void {
    this.saved.set(false);
    this.state.mapping.update((m) => (m ? { ...m } : m));
  }

  isNew(f: { propertyKey: string; creating?: boolean }): boolean {
    return !!f.creating || (!!f.propertyKey && !this.state.properties().some((p) => p.key === f.propertyKey));
  }

  chooseProperty(f: { propertyKey: string; creating?: boolean }, value: string): void {
    f.creating = value === NEW_PROPERTY;
    f.propertyKey = f.creating ? '' : value;
    this.touch();
  }

  addType(value: string): void {
    this.state.mapping()!.types.push({ sourceValue: value, typeKey: '' });
    this.touch();
  }

  removeType(i: number): void {
    this.state.mapping()!.types.splice(i, 1);
    this.touch();
  }

  addField(): void {
    this.state.mapping()!.fields.push({ sourceField: '', propertyKey: '', label: '' });
    this.touch();
  }

  removeField(i: number): void {
    this.state.mapping()!.fields.splice(i, 1);
    this.touch();
  }

  pick(event: Event): void {
    this.state.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
    this.previewError.set('');
  }

  async preview(): Promise<void> {
    this.previewing.set(true);
    this.previewError.set('');
    try {
      await this.state.loadPreview();
    } catch (e) {
      this.previewError.set((e as Error).message);
    } finally {
      this.previewing.set(false);
    }
  }

  async save(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.state.save('mapping');
      this.saved.set(true);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Rattache un message du serveur à la carte concernée, pour l'afficher près du champ fautif. */
  private section(message: string): '' | 'identite' | 'types' | 'champs' | 'general' {
    if (!message) return '';
    if (/identifiant et du nom/.test(message)) return 'identite';
    if (/type|nature/i.test(message)) return 'types';
    if (/propriété|colonne de la source/i.test(message)) return 'champs';
    return 'general';
  }
}
