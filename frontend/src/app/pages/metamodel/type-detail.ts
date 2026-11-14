import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Api } from '../../api';
import { GLYPH_KEYS } from '../../glyphs';
import { TypePreview } from './glyph-preview';
import { MetaStore } from './store';

const TEMPLATES: Record<string, string> = {
  carte: 'carte : fiche rectangulaire avec pictogramme',
  rond: 'rond : pictogramme dans un disque',
  zone: 'zone : cadre qui regroupe d\'autres objets',
};

/** Détail d'un type d'objet : rôles, rendu, description. Les modifications sont en brouillon jusqu'à « Enregistrer ». */
@Component({
  selector: 'app-type-detail',
  imports: [TypePreview],
  template: `
    @let t = type();
    <div class="head">
      <app-type-preview [visual]="draft().visual" [width]="56" />
      <div><h2>{{ t.label }}</h2><span class="muted small">Clé <code>{{ t.key }}</code>, non modifiable</span></div>
      <button type="button" class="ghost right" (click)="close.emit()" aria-label="Fermer le détail">Fermer</button>
    </div>

    <fieldset>
      <legend>Rôles fonctionnels</legend>
      <p class="muted small">Un rôle décide ce que l'outil fait des objets de ce type. Sans rôle, le type sert seulement à classer et à dessiner.</p>
      @for (r of store.catalog().objectRoles; track r.key) {
        <label class="check">
          <input type="checkbox" [checked]="draft().roles.includes(r.key)" (change)="toggleRole(r.key, $any($event.target).checked)" />
          <span><b>{{ r.label }}</b><br /><span class="muted small">{{ r.effect }}</span>
            @if (!draft().roles.includes(r.key) && t.roles.includes(r.key) && others(r.key).length === 0) {
              <br /><span class="chip warn">Plus aucun type ne portera ce rôle : cette fonction sera vide</span>
            } @else if (others(r.key).length) {
              <br /><span class="muted small">Aussi porté par : {{ others(r.key).join(', ') }}</span>
            }
          </span>
        </label>
      }
    </fieldset>

    <fieldset>
      <legend>Rendu dans les vues</legend>
      <div class="grid">
        <label>Forme<select [value]="draft().visual.shape ?? 'rectangle'" (change)="visual('shape', $any($event.target).value)">
          @for (s of store.catalog().shapes; track s) { <option [value]="s">{{ s }}</option> }</select></label>
        <label>Gabarit<select [value]="draft().visual.template ?? ''" (change)="visual('template', $any($event.target).value || null)">
          <option value="">par défaut (carte)</option>
          @for (s of store.catalog().templates; track s) { <option [value]="s">{{ templates[s] ?? s }}</option> }</select></label>
        <label>Pictogramme<select [value]="draft().visual.icon ?? ''" (change)="visual('icon', $any($event.target).value || null)">
          <option value="">par défaut</option>
          @for (g of glyphs; track g) { <option [value]="g">{{ g }}</option> }</select></label>
        <label>Couleur de fond<input type="color" [value]="draft().visual.fill ?? '#f0f0f0'" (input)="visual('fill', $any($event.target).value)" /></label>
        <label>Couleur de bordure<input type="color" [value]="draft().visual.stroke ?? '#5c5c5c'" (input)="visual('stroke', $any($event.target).value)" /></label>
      </div>
    </fieldset>

    <label class="block">Description
      <textarea [value]="draft().description" (input)="describe($any($event.target).value)" placeholder="À quoi sert ce type, ce qu'il faut y ranger"></textarea>
    </label>
    <label class="check"><input type="checkbox" [checked]="draft().archived" (change)="archive($any($event.target).checked)" />
      <span><b>Archivé</b><br /><span class="muted small">Le type n'est plus proposé à la création ; ses objets existants restent.</span></span></label>

    <div class="tools">
      <button type="button" (click)="save()" [disabled]="!dirty() || busy()">Enregistrer</button>
      <button type="button" class="ghost" (click)="reset()" [disabled]="!dirty()">Annuler les modifications</button>
      @if (dirty()) { <span class="chip warn">Modifications non enregistrées</span> }
      <button type="button" class="ghost danger right" (click)="remove.emit()">Supprimer ou fusionner…</button>
    </div>
  `,
})
export class TypeDetail {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  type = input.required<any>();
  close = output<void>();
  remove = output<void>();
  protected readonly templates = TEMPLATES;
  protected readonly glyphs = GLYPH_KEYS;
  protected readonly busy = signal(false);
  protected readonly draft = signal<{ roles: string[]; visual: any; description: string; archived: boolean }>({ roles: [], visual: {}, description: '', archived: false });
  protected readonly dirty = computed(() => JSON.stringify(this.draft()) !== JSON.stringify(this.fromType()));

  constructor() {
    // Le brouillon repart de zéro quand on change de type, pas quand le libellé ou la couche (modifiés dans le tableau) évoluent.
    effect(() => {
      this.type().key;
      untracked(() => this.reset());
    });
  }

  private fromType() {
    const t = this.type();
    return { roles: [...t.roles], visual: { ...t.visual }, description: t.description ?? '', archived: !!t.archived };
  }

  protected reset(): void {
    this.draft.set(this.fromType());
  }

  protected others(role: string): string[] {
    return this.store.typesWith(role).filter((label) => label !== this.type().label);
  }

  protected toggleRole(role: string, on: boolean): void {
    this.draft.update((d) => ({ ...d, roles: on ? [...d.roles, role] : d.roles.filter((r) => r !== role) }));
  }

  protected visual(field: string, value: string | null): void {
    this.draft.update((d) => ({ ...d, visual: { ...d.visual, [field]: value } }));
  }

  protected describe(value: string): void {
    this.draft.update((d) => ({ ...d, description: value }));
  }

  protected archive(value: boolean): void {
    this.draft.update((d) => ({ ...d, archived: value }));
  }

  protected async save(): Promise<void> {
    const d = this.draft();
    // La route remplace tous les champs : on part du type courant (libellé et couche modifiés dans le tableau) et on y pose le brouillon.
    const body = { ...this.type(), roles: d.roles, visual: d.visual, description: d.description.trim() || null, archived: d.archived };
    this.busy.set(true);
    const ok = await this.store.run(async () => {
      const saved = await this.api.put(`/metamodel/object-types/${encodeURIComponent(body.key)}`, body);
      this.store.patch({ objectTypes: this.store.m().objectTypes.map((x: any) => (x.key === saved.key ? saved : x)) });
    });
    this.busy.set(false);
    if (ok) this.reset();
  }
}
