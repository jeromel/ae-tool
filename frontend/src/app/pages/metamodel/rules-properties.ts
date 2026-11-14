import { Component, computed, inject, signal } from '@angular/core';
import { MetaStore, plain } from './store';
import { TranscodingDraft } from './transcoding-draft';

/** Nom de chaque propriété dans Archi. Vide = identique à la clé. Enregistré avec la matrice. */
@Component({
  selector: 'app-rules-properties',
  template: `
    <p class="muted">À l'import, la propriété Archi portant ce nom est rangée sous la clé du métamodèle ; à l'export, la clé reprend son nom Archi. Vide : le nom Archi est la clé.</p>
    <div class="tools">
      <input type="search" placeholder="Rechercher une propriété" aria-label="Rechercher une propriété" [value]="search()" (input)="search.set($any($event.target).value)" />
      <label class="inline"><input type="checkbox" [checked]="onlyRenamed()" (change)="onlyRenamed.set($any($event.target).checked)" /> Seulement celles dont le nom diffère</label>
      <span class="muted small">{{ shown().length }} sur {{ store.m().propertyDefinitions.length }} propriétés</span>
    </div>
    <div class="card flush">
      <div class="scroll tall" style="--above: 400px">
        <table>
          <tr><th scope="col">Clé du métamodèle</th><th scope="col">Libellé</th><th scope="col">Nom dans Archi</th><th scope="col" class="num">Valeurs</th></tr>
          @for (p of shown(); track p.key) {
            <tr>
              <td>{{ p.key || '(vide)' }}</td><td class="muted">{{ p.label }}</td>
              <td><input type="text" [value]="draft.archiName(p)" [placeholder]="p.key" [attr.aria-label]="'Nom Archi de ' + p.key" (change)="draft.setArchiName(p.key, $any($event.target).value)" /></td>
              <td class="num"><span [class.muted]="!(store.usage().properties[p.key])">{{ store.usage().properties[p.key] ?? 0 }}</span></td>
            </tr>
          } @empty { <tr><td colspan="4" class="muted">Aucune propriété ne correspond.</td></tr> }
        </table>
      </div>
    </div>
  `,
})
export class RulesProperties {
  protected readonly store = inject(MetaStore);
  protected readonly draft = inject(TranscodingDraft);
  protected readonly search = signal('');
  protected readonly onlyRenamed = signal(false);
  protected readonly shown = computed(() => {
    const q = plain(this.search().trim());
    return (this.store.m().propertyDefinitions as any[]).filter((p) =>
      (!q || plain(`${p.key} ${p.label ?? ''}`).includes(q)) && (!this.onlyRenamed() || this.draft.archiName(p).trim() !== ''));
  });
}
