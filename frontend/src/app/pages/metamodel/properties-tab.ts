import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { ConfirmPanel } from './confirm-panel';
import { PropertyForm } from './property-form';
import { MetaStore, plain } from './store';

/** Propriétés : définitions typées, types d'objets concernés, nom dans Archi, nombre de valeurs saisies. */
@Component({
  selector: 'app-properties-tab',
  imports: [PropertyForm, ConfirmPanel],
  template: `
    <div class="tools">
      <input type="search" placeholder="Rechercher une propriété" aria-label="Rechercher une propriété par clé ou libellé" [value]="search()" (input)="search.set($any($event.target).value)" />
      <button type="button" (click)="startCreate()">Nouvelle propriété</button>
      <span class="muted small">{{ shown().length }} sur {{ all().length }} propriétés</span>
    </div>

    @if (removing(); as p) {
      <app-confirm [title]="'Supprimer la propriété « ' + (p.label || p.key) + ' »'" action="Supprimer" [disabled]="values(p.key) > 0" (confirm)="remove(p.key)" (cancel)="removing.set(null)">
        @if (values(p.key) > 0) {
          <p><b>{{ values(p.key) }}</b> valeur(s) saisie(s) existent : la propriété ne peut pas être supprimée tant qu'elles existent. Vous pouvez la retirer de « Calque sur les vues » ou la rendre facultative.</p>
        } @else {
          <p>Aucune valeur n'est saisie. La définition disparaît du métamodèle ; elle sera recréée automatiquement si un import Archi la contient encore.</p>
        }
      </app-confirm>
    }

    <div class="mm-split">
      <div class="card flush mm-main">
        <div class="scroll tall" style="--above: 300px">
          <table>
            <tr><th scope="col">Propriété</th><th scope="col">Type</th><th scope="col">Concerne</th><th scope="col" class="num" title="Obligatoire">Oblig.</th>
              <th scope="col" class="num" title="Proposée dans le menu Affichage des vues">Calque</th><th scope="col">Nom dans Archi</th><th scope="col">Source</th><th scope="col" class="num">Valeurs</th></tr>
            @for (p of shown(); track p.key) {
              <tr class="click" [class.on]="p.key === selected()" (click)="select(p.key)">
                <td class="wrap"><button type="button" class="link" (click)="select(p.key); $event.stopPropagation()">{{ p.key || '(vide)' }}</button>
                  @if (p.label && p.label !== p.key) { <div class="muted small">{{ p.label }}</div> }</td>
                <td>{{ typeLabel(p.dataType) }}@if (p.dataType === 'liste') { <span class="muted small"> ({{ p.allowedValues.length }})</span> }</td>
                <td>@if (p.appliesTo.length) { <span title="{{ names(p) }}">{{ p.appliesTo.length }} type(s)</span> } @else { <span class="muted">tous</span> }</td>
                <td class="num">{{ p.required ? 'oui' : '' }}</td>
                <td class="num"><input type="checkbox" [checked]="p.displayable" [attr.aria-label]="'Proposer ' + (p.label || p.key) + ' en calque'" (click)="$event.stopPropagation()" (change)="setDisplayable(p, $any($event.target).checked)" /></td>
                <td class="muted small wrap">{{ p.archiName || '' }}</td>
                <td class="muted small">{{ p.source }}</td>
                <td class="num"><span [class.muted]="!values(p.key)">{{ values(p.key) }}</span></td>
              </tr>
            }
          </table>
        </div>
      </div>
      @if (creating() || current()) {
        <aside class="card mm-aside" aria-label="Formulaire de propriété">
          <app-property-form [prop]="creating() ? null : current()" (close)="closeForm()" (remove)="removing.set(current())" (saved)="saved($event)" />
        </aside>
      }
    </div>
  `,
  styles: 'td.wrap { overflow-wrap: anywhere; max-width: 260px; } button.link { background: none; border: 0; padding: 0; color: var(--accent); font-weight: 400; white-space: normal; text-align: left; } button.link:hover { background: none; text-decoration: underline; }',
})
export class PropertiesTab {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  protected readonly search = signal('');
  protected readonly selected = signal<string | null>(null);
  protected readonly creating = signal(false);
  protected readonly removing = signal<any>(null);
  protected readonly all = computed<any[]>(() => this.store.m().propertyDefinitions);
  protected readonly shown = computed(() => {
    const q = plain(this.search().trim());
    return this.all().filter((p) => !q || plain(`${p.key} ${p.label ?? ''}`).includes(q));
  });
  protected readonly current = computed(() => (this.selected() === null ? null : this.all().find((p) => p.key === this.selected()) ?? null));

  protected values = (key: string) => this.store.usage().properties[key] ?? 0;
  protected typeLabel = (key: string) => this.store.catalog().dataTypes.find((t: any) => t.key === key)?.label.replace(/ \(.*\)/, '') ?? key;
  protected names = (p: any) => p.appliesTo.map((k: string) => this.store.typeLabel(k)).join(', ');

  protected select(key: string): void {
    this.creating.set(false);
    this.selected.set(key);
  }

  protected startCreate(): void {
    this.selected.set(null);
    this.creating.set(true);
  }

  protected closeForm(): void {
    this.selected.set(null);
    this.creating.set(false);
  }

  protected saved(key: string): void {
    this.creating.set(false);
    this.selected.set(key);
    this.search.set('');
  }

  protected async setDisplayable(property: any, displayable: boolean): Promise<void> {
    const ok = await this.store.run(() => this.api.put('/metamodel/properties/displayable', { key: property.key, displayable }));
    if (ok) this.store.patch({ propertyDefinitions: this.all().map((p) => (p.key === property.key ? { ...p, displayable } : p)) });
  }

  protected async remove(key: string): Promise<void> {
    const ok = await this.store.run(() => this.store.remove(`/metamodel/properties?key=${encodeURIComponent(key)}`));
    if (!ok) return;
    this.removing.set(null);
    this.closeForm();
    await this.store.load();
  }
}
