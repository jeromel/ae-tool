import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { Api, ApiError, SOURCE_LABELS } from '../api';

/** Sources de définitions de propriétés qui se saisissent dans l'outil ; toute autre source maîtrise la valeur. */
const EDITABLE_SOURCES = ['saisie', 'urba'];

type Kind = 'texte' | 'long' | 'nombre' | 'date' | 'booleen' | 'liste';

interface Row {
  key: string;
  label: string;
  value: string;
  kind: Kind;
  allowed: string[];
  required: boolean;
  /** Source qui maîtrise la valeur (« Archi »…) ; null si elle se saisit ici. */
  lockedBy: string | null;
  description: string;
}

/**
 * Formulaire d'un objet : création ou modification. Il suit le métamodèle (types par couche, propriétés prévues pour le type,
 * champ adapté au type de donnée) et respecte le maître de chaque donnée : ce qui vient d'Archi se lit mais ne se modifie pas ici.
 * Il enregistre lui-même et signale le résultat ; il ne perd jamais la saisie sur une erreur.
 */
@Component({
  selector: 'app-object-form',
  template: `
    <form class="oform" novalidate (submit)="submit($event)">
      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }

      <div class="field">
        <label for="f-name">Nom <span class="req" aria-hidden="true">*</span>
          @if (archi()) { <span class="chip info">Maîtrisé dans Archi</span> }</label>
        <input id="f-name" type="text" [value]="name()" [readOnly]="archi()" [attr.aria-invalid]="errors()['name'] ? true : null"
          (input)="setName($any($event.target).value)" />
        @if (errors()['name']; as e) { <p class="err">{{ e }}</p> }
      </div>

      <div class="field">
        <label for="f-type">Type <span class="req" aria-hidden="true">*</span></label>
        <select id="f-type" [attr.aria-invalid]="errors()['typeKey'] ? true : null" (change)="setType($any($event.target).value)">
          @if (!typeKey()) { <option value="" selected>Choisir un type</option> }
          @for (g of groups(); track g.label) {
            <optgroup [label]="g.label">
              @for (t of g.types; track t.key) { <option [value]="t.key" [selected]="t.key === typeKey()">{{ t.label }}{{ t.archived ? ' (archivé)' : '' }}</option> }
            </optgroup>
          }
        </select>
        @if (typeDescription(); as d) { <p class="hint">{{ d }}</p> }
        @if (errors()['typeKey']; as e) { <p class="err">{{ e }}</p> }
      </div>

      <div class="field">
        <label for="f-doc">Description @if (archi()) { <span class="chip info">Maîtrisé dans Archi</span> }</label>
        <textarea id="f-doc" rows="4" [value]="documentation()" [readOnly]="archi()" (input)="documentation.set($any($event.target).value)"></textarea>
      </div>

      <div class="field">
        <label for="f-owner">Responsable</label>
        <input id="f-owner" type="text" [value]="owner()" (input)="owner.set($any($event.target).value)" />
      </div>

      <h3>Propriétés</h3>
      <div class="props">
      @for (r of rows(); track r.key; let i = $index) {
        <div class="field" [class.wide]="r.kind === 'long'">
          <label [for]="'f-p-' + i">{{ r.label }} @if (r.required) { <span class="req" title="Obligatoire">*</span> }
            @if (r.lockedBy) { <span class="chip info">Maîtrisé dans {{ r.lockedBy }}</span> }</label>
          @switch (r.kind) {
            @case ('liste') {
              <select [id]="'f-p-' + i" [disabled]="!!r.lockedBy" (change)="setValue(r.key, $any($event.target).value)">
                <option value="" [selected]="!r.value"></option>
                @for (v of r.allowed; track v) { <option [value]="v" [selected]="v === r.value">{{ v }}</option> }
                @if (r.value && !r.allowed.includes(r.value)) { <option [value]="r.value" selected>{{ r.value }} (hors liste)</option> }
              </select>
            }
            @case ('booleen') {
              <select [id]="'f-p-' + i" [disabled]="!!r.lockedBy" (change)="setValue(r.key, $any($event.target).value)">
                <option value="" [selected]="!r.value"></option>
                <option value="oui" [selected]="r.value === 'oui'">Oui</option>
                <option value="non" [selected]="r.value === 'non'">Non</option>
                @if (r.value && r.value !== 'oui' && r.value !== 'non') { <option [value]="r.value" selected>{{ r.value }} (valeur non reconnue)</option> }
              </select>
            }
            @case ('date') {
              <input [id]="'f-p-' + i" type="date" [value]="r.value" [readOnly]="!!r.lockedBy" (input)="setValue(r.key, $any($event.target).value)" />
            }
            @case ('nombre') {
              <input [id]="'f-p-' + i" type="number" step="any" [value]="r.value" [readOnly]="!!r.lockedBy" (input)="setValue(r.key, $any($event.target).value)" />
            }
            @case ('long') {
              <textarea [id]="'f-p-' + i" rows="3" [value]="r.value" [readOnly]="!!r.lockedBy" (input)="setValue(r.key, $any($event.target).value)"></textarea>
            }
            @default {
              <input [id]="'f-p-' + i" type="text" [value]="r.value" [readOnly]="!!r.lockedBy" (input)="setValue(r.key, $any($event.target).value)" />
            }
          }
          @if (r.description) { <p class="hint">{{ r.description }}</p> }
          @for (e of errors()['p:' + r.key] ?? []; track e) { <p class="err">{{ e }}</p> }
        </div>
      } @empty {
        <p class="muted">Aucune propriété prévue pour ce type.</p>
      }
      </div>

      @if (addable().length) {
        <div class="add">
          <label for="f-add">Ajouter une propriété</label>
          <select id="f-add" #pick>
            @for (d of addable(); track d.key) { <option [value]="d.key">{{ d.label || d.key }}</option> }
          </select>
          <button type="button" class="ghost" (click)="add(pick.value)">Ajouter</button>
        </div>
      }

      <div class="actions">
        <button type="submit" [disabled]="busy()">{{ busy() ? 'Enregistrement…' : (object() ? 'Enregistrer' : 'Créer l\\'objet') }}</button>
        <button type="button" class="ghost" (click)="cancelled.emit()">Annuler</button>
      </div>
    </form>
  `,
  styles: `
    .oform { display: flex; flex-direction: column; gap: 12px; max-width: 880px; }
    .oform > .field:not(.wide), .oform > .field { max-width: 640px; width: 100%; }
    /* Un type porte souvent des dizaines de propriétés : deux ou trois colonnes plutôt qu'une page interminable. */
    .props { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 12px 16px; }
    .props .wide { grid-column: 1 / -1; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    .field label, .add label { font-weight: 600; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .field input, .field select, .field textarea { width: 100%; }
    .field input[readonly], .field textarea[readonly], .field select:disabled { background: #f1f4f4; color: var(--muted); }
    [aria-invalid=true] { border-color: var(--bad) !important; }
    .req { color: var(--bad); }
    .hint { margin: 0; font-size: 12px; color: var(--muted); }
    .err { margin: 0; font-size: 12.5px; color: var(--bad); }
    h3 { font-family: var(--display); font-size: 14px; margin: 8px 0 -4px; padding-top: 12px; border-top: 1px solid var(--line); }
    .add { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; padding: 10px 12px; background: #f6f8f8; border-radius: 6px; }
    .add select { min-width: 200px; }
    .actions { display: flex; gap: 10px; margin-top: 4px; }
  `,
})
export class ObjectForm implements OnInit {
  private api = inject(Api);
  /** Métamodèle (GET /metamodel). */
  meta = input.required<any>();
  /** Objet à modifier (champ « object » de GET /objects/{id}) ; absent pour une création. */
  object = input<any>(null);
  /** Type présélectionné à la création. */
  presetType = input<string>();
  saved = output<string>();
  cancelled = output<void>();

  name = signal('');
  typeKey = signal('');
  documentation = signal('');
  owner = signal('');
  busy = signal(false);
  error = signal('');
  errors = signal<Record<string, string[]>>({});

  private values = signal<Record<string, string>>({});
  /** Propriétés déjà portées au départ, puis celles que l'utilisateur ajoute ou qu'un changement de type laisse sans définition prévue. */
  private carried: string[] = [];
  private added = signal<string[]>([]);

  archi = computed(() => this.object()?.source === 'archi');

  /** Types non archivés groupés par couche, dans l'ordre des couches ; le type actuel est conservé même archivé. */
  groups = computed(() => {
    const m = this.meta();
    const layers = [...(m.layers as any[])].sort((a, b) => a.order - b.order);
    const current = this.typeKey();
    const types = (m.objectTypes as any[]).filter((t) => !t.archived || t.key === current);
    return layers
      .map((l) => ({ label: l.label as string, types: types.filter((t) => t.layerKey === l.key).sort((a, b) => a.label.localeCompare(b.label, 'fr')) }))
      .filter((g) => g.types.length);
  });

  typeDescription = computed(() => (this.meta().objectTypes as any[]).find((t) => t.key === this.typeKey())?.description ?? '');

  /** Propriétés prévues pour le type (obligatoires en tête), puis celles déjà portées, puis celles ajoutées. */
  rows = computed<Row[]>(() => {
    const defs = this.meta().propertyDefinitions as any[];
    const type = this.typeKey();
    const values = this.values();
    const planned = defs.filter((d) => !d.appliesTo?.length || d.appliesTo.includes(type));
    const ordered = [...planned.filter((d) => d.required), ...planned.filter((d) => !d.required)].map((d) => d.key as string);
    const keys = [...new Set([...ordered, ...this.carried, ...this.added()])];
    return keys.map((k) => this.row(k, values[k] ?? '')).filter((r) => !(r.lockedBy && !r.value));
  });

  /** Définitions qui se saisissent ici et qui ne figurent pas encore dans le formulaire. */
  addable = computed(() => {
    const shown = new Set(this.rows().map((r) => r.key));
    return (this.meta().propertyDefinitions as any[])
      .filter((d) => !shown.has(d.key) && EDITABLE_SOURCES.includes(d.source ?? 'saisie'))
      .sort((a, b) => (a.label || a.key).localeCompare(b.label || b.key, 'fr'));
  });

  ngOnInit(): void {
    const o = this.object();
    if (!o) {
      const wanted = this.presetType();
      this.typeKey.set((this.meta().objectTypes as any[]).some((t) => t.key === wanted && !t.archived) ? wanted! : '');
      return;
    }
    this.name.set(o.name ?? '');
    this.typeKey.set(o.typeKey);
    this.documentation.set(o.documentation ?? '');
    this.owner.set(o.owner ?? '');
    const props = (o.properties as { key: string; value: string | null }[]) ?? [];
    this.values.set(Object.fromEntries(props.map((p) => [p.key, p.value ?? ''])));
    this.carried = props.map((p) => p.key);
  }

  setName(value: string): void {
    this.name.set(value);
    this.clearError('name');
  }

  setType(key: string): void {
    // Une valeur saisie pour une propriété qui n'est plus prévue par le nouveau type reste visible plutôt que d'être envoyée sans être vue.
    const planned = new Set((this.meta().propertyDefinitions as any[]).filter((d) => !d.appliesTo?.length || d.appliesTo.includes(key)).map((d) => d.key));
    const orphans = this.rows().filter((r) => r.value && !planned.has(r.key) && !this.carried.includes(r.key) && !this.added().includes(r.key)).map((r) => r.key);
    this.typeKey.set(key);
    if (orphans.length) this.added.update((a) => [...a, ...orphans]);
    this.clearError('typeKey');
  }

  setValue(key: string, value: string): void {
    this.values.update((v) => ({ ...v, [key]: value }));
    this.clearError('p:' + key);
  }

  add(key: string): void {
    if (key) this.added.update((a) => [...a, key]);
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.error.set('');
    this.errors.set({});
    const local: Record<string, string[]> = {};
    if (!this.archi() && !this.name().trim()) local['name'] = ['Le nom est obligatoire.'];
    if (!this.typeKey()) local['typeKey'] = ['Choisissez un type.'];
    if (Object.keys(local).length) {
      this.errors.set(local);
      return;
    }
    this.busy.set(true);
    try {
      const o = this.object();
      const saved = o ? await this.api.put(`/objects/${o.id}`, this.payload()) : await this.api.post('/objects', this.payload());
      this.saved.emit(saved.id);
    } catch (e) {
      this.report(e);
    } finally {
      this.busy.set(false);
    }
  }

  /** Corps envoyé : sans nom ni description pour un objet maîtrisé dans Archi, et jamais l'état de validation (autre circuit). */
  private payload(): Record<string, unknown> {
    const values = this.values();
    const keys = new Set(this.rows().map((r) => r.key));
    // Les propriétés maîtrisées ailleurs repartent telles quelles : le serveur remplace la liste entière.
    const properties = Object.entries(values).filter(([k, v]) => (keys.has(k) || this.carried.includes(k)) && v.trim() !== '').map(([key, value]) => ({ key, value: value.trim() }));
    const body: Record<string, unknown> = { typeKey: this.typeKey(), owner: this.owner().trim(), properties };
    if (!this.archi()) {
      body['name'] = this.name().trim();
      body['documentation'] = this.documentation();
    }
    return body;
  }

  /** Affiche chaque problème détaillé par le serveur sous le champ dont il parle (repéré par son libellé), le reste en tête du formulaire. */
  private report(e: unknown): void {
    const problems = e instanceof ApiError ? e.problems : [];
    if (!problems.length) return this.error.set(e instanceof Error ? e.message : 'L\'enregistrement a échoué.');
    const byField: Record<string, string[]> = {};
    const rest: string[] = [];
    for (const p of problems) {
      const row = this.rows().find((r) => p.includes(`« ${r.label} »`));
      if (row) (byField['p:' + row.key] ??= []).push(capitalize(p));
      else rest.push(p);
    }
    this.errors.set(byField);
    this.error.set(rest.length ? rest.join(' ') : 'Certaines valeurs ne sont pas conformes : corrigez-les puis enregistrez à nouveau.');
  }

  private clearError(field: string): void {
    if (this.errors()[field]) this.errors.update(({ [field]: _removed, ...others }) => others);
  }

  private row(key: string, value: string): Row {
    const def = (this.meta().propertyDefinitions as any[]).find((d) => d.key === key);
    const source: string | null = def ? (def.source ?? 'saisie') : this.archi() ? 'archi' : null;
    const locked = source !== null && !EDITABLE_SOURCES.includes(source);
    return {
      key, value, label: def?.label || key, required: !!def?.required, allowed: def?.allowedValues ?? [], description: def?.description ?? '',
      lockedBy: locked ? (SOURCE_LABELS[source!] ?? source) : null, kind: kindOf(def?.dataType, value, def?.allowedValues?.length > 0),
    };
  }
}

/** Champ adapté au type de donnée ; une valeur existante que le champ natif ne saurait afficher passe en texte libre pour ne pas être perdue. */
function kindOf(dataType: string | undefined, value: string, hasList: boolean): Kind {
  switch (dataType) {
    case 'liste': return hasList ? 'liste' : 'texte';
    case 'booleen': return 'booleen';
    case 'date': return !value || /^\d{4}-\d{2}-\d{2}$/.test(value) ? 'date' : 'texte';
    case 'nombre': return !value || /^-?\d+(\.\d+)?$/.test(value) ? 'nombre' : 'texte';
    default: return value.includes('\n') ? 'long' : 'texte';
  }
}

function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}
