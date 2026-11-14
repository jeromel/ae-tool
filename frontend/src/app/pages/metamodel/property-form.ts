import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Api } from '../../api';
import { MetaStore, plain } from './store';

const empty = () => ({ key: '', label: '', description: '', dataType: 'texte', valuesText: '', appliesTo: [] as string[], required: false, displayable: false, archiName: '' });

/** Création et modification d'une propriété. La clé ne change plus une fois créée : les valeurs saisies s'y rattachent. */
@Component({
  selector: 'app-property-form',
  template: `
    <div class="head">
      <div><h2>{{ prop() ? prop().label || prop().key : 'Nouvelle propriété' }}</h2>
        @if (prop(); as p) { <span class="muted small">Clé <code>{{ p.key }}</code>, non modifiable · source : {{ p.source }}</span> }</div>
      <button type="button" class="ghost right" (click)="close.emit()" aria-label="Fermer le formulaire">Fermer</button>
    </div>
    @if (!prop()) {
      <label class="block">Clé<input type="text" [value]="d().key" (input)="set('key', $any($event.target).value)" placeholder="ex. site.code" />
        <span class="muted small">Identifiant technique ; elle peut contenir des points et des deux-points.</span></label>
    }
    <label class="block">Libellé<input type="text" [value]="d().label" (input)="set('label', $any($event.target).value)" /></label>
    <label class="block">Type de donnée
      <select [value]="d().dataType" (change)="set('dataType', $any($event.target).value)">
        @for (t of store.catalog().dataTypes; track t.key) { <option [value]="t.key">{{ t.label }}</option> }
      </select></label>
    @if (d().dataType === 'liste') {
      <label class="block">Valeurs permises (une par ligne)
        <textarea [value]="d().valuesText" (input)="set('valuesText', $any($event.target).value)" rows="5" placeholder="Brouillon&#10;Validé&#10;Archivé"></textarea>
        <span class="muted small">{{ values().length }} valeur(s). Une liste doit en avoir au moins une.</span></label>
    }
    <fieldset>
      <legend>Types d'objets concernés</legend>
      <p class="muted small">{{ d().appliesTo.length ? d().appliesTo.length + ' type(s) coché(s) : seuls leurs objets portent cette propriété.' : 'Aucun type coché : la propriété concerne tous les types.' }}</p>
      <input type="search" placeholder="Filtrer les types" aria-label="Filtrer les types concernés" [value]="filter()" (input)="filter.set($any($event.target).value)" />
      <div class="checklist">
        @for (t of choices(); track t.key) {
          <label class="check"><input type="checkbox" [checked]="d().appliesTo.includes(t.key)" (change)="toggle(t.key, $any($event.target).checked)" />
            <span>{{ t.label }} <span class="muted small">{{ store.layerLabel(t.layerKey) }}</span></span></label>
        }
      </div>
    </fieldset>
    <label class="check"><input type="checkbox" [checked]="d().required" (change)="set('required', $any($event.target).checked)" />
      <span><b>Obligatoire</b><br /><span class="muted small">Les objets concernés sans valeur sont signalés par le contrôle de conformité.</span></span></label>
    @if (d().required && !d().appliesTo.length) { <p class="chip warn">Obligatoire pour tous les types : chaque objet sans valeur sera signalé</p> }
    <label class="check"><input type="checkbox" [checked]="d().displayable" (change)="set('displayable', $any($event.target).checked)" />
      <span><b>Calque sur les vues</b><br /><span class="muted small">Proposée dans le menu Affichage : sa valeur s'affiche en pastille sur les cartes et peut les colorer.</span></span></label>
    <label class="block">Nom dans Archi
      <input type="text" [value]="d().archiName" (input)="set('archiName', $any($event.target).value)" placeholder="(identique à la clé)" />
      <span class="muted small">Nom de la propriété à l'import et à l'export Archi ; vide = la clé.</span></label>
    <label class="block">Description<textarea [value]="d().description" (input)="set('description', $any($event.target).value)"></textarea></label>

    @if (problem(); as p) { <p class="chip bad">{{ p }}</p> }
    <div class="tools">
      <button type="button" (click)="save()" [disabled]="!!problem() || busy() || (!!prop() && !dirty())">{{ prop() ? 'Enregistrer' : 'Créer la propriété' }}</button>
      @if (prop() && dirty()) { <span class="chip warn">Modifications non enregistrées</span> }
      @if (prop()) { <button type="button" class="ghost danger right" (click)="remove.emit()">Supprimer…</button> }
    </div>
  `,
})
export class PropertyForm {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  /** Propriété modifiée ; absente = création. */
  prop = input<any>(null);
  close = output<void>();
  remove = output<void>();
  saved = output<string>();
  private gone = false;
  protected readonly busy = signal(false);
  protected readonly filter = signal('');
  protected readonly d = signal(empty());
  protected readonly values = computed(() => this.d().valuesText.split('\n').map((v) => v.trim()).filter(Boolean));
  protected readonly dirty = computed(() => JSON.stringify(this.d()) !== JSON.stringify(this.fromProp()));
  protected readonly choices = computed(() => {
    const q = plain(this.filter().trim());
    return this.store.types().filter((t) => !q || plain(t.label).includes(q));
  });
  protected readonly problem = computed(() => {
    const d = this.d();
    if (!this.prop() && !d.key.trim()) return 'La clé est obligatoire.';
    if (d.dataType === 'liste' && this.values().length === 0) return 'Une propriété de type liste doit avoir des valeurs permises.';
    return '';
  });

  constructor() {
    // L'enregistrement peut aboutir après un changement d'onglet : une sortie émise par un composant détruit est une erreur Angular.
    inject(DestroyRef).onDestroy(() => (this.gone = true));
    effect(() => {
      this.prop()?.key;
      untracked(() => this.d.set(this.fromProp()));
    });
  }

  private fromProp() {
    const p = this.prop();
    return p ? { key: p.key, label: p.label ?? '', description: p.description ?? '', dataType: p.dataType, valuesText: p.allowedValues.join('\n'),
      appliesTo: [...p.appliesTo], required: !!p.required, displayable: !!p.displayable, archiName: p.archiName ?? '' } : empty();
  }

  protected set(field: string, value: unknown): void {
    this.d.update((d) => ({ ...d, [field]: value }));
  }

  protected toggle(key: string, on: boolean): void {
    this.set('appliesTo', on ? [...this.d().appliesTo, key] : this.d().appliesTo.filter((k) => k !== key));
  }

  protected async save(): Promise<void> {
    const d = this.d();
    const body = { ...(this.prop() ?? { source: 'saisie' }), key: this.prop()?.key ?? d.key.trim(), label: d.label.trim() || null, description: d.description.trim() || null,
      dataType: d.dataType, allowedValues: d.dataType === 'liste' ? this.values() : [], appliesTo: d.appliesTo, required: d.required,
      displayable: d.displayable, archiName: d.archiName.trim() || null };
    this.busy.set(true);
    const ok = await this.store.run(async () => {
      const saved = this.prop()
        ? await this.api.put(`/metamodel/properties?key=${encodeURIComponent(body.key)}`, body)
        : await this.api.post('/metamodel/properties', body);
      const others = this.store.m().propertyDefinitions.filter((p: any) => p.key !== saved.key);
      this.store.patch({ propertyDefinitions: [...others, saved] });
      if (!this.gone) this.saved.emit(saved.key);
    });
    this.busy.set(false);
    if (ok && this.prop()) this.d.set(this.fromProp());
  }
}
