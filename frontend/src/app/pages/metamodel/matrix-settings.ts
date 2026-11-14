import { Component, inject } from '@angular/core';
import { MetaStore } from './store';
import { TranscodingDraft } from './transcoding-draft';

/** Réglages de la matrice : ce que devient un concept ou un type qu'aucune règle ne couvre. */
@Component({
  selector: 'app-matrix-settings',
  template: `
    @let t = draft.t();
    <div class="card">
      <h2>Réglages de la matrice</h2>
      <p class="muted">Ce qui se passe quand aucune règle ne s'applique, à l'import comme à l'export.</p>
      <div class="grid2">
        <fieldset>
          <legend>Export : type sans règle</legend>
          <label class="block">Concept de repli pour les objets
            <select (change)="draft.setting('fallbackElementType', $any($event.target).value)">
              @for (c of store.catalog().archimate.elements; track c) { <option [value]="c" [selected]="c === t.fallbackElementType">{{ c }}</option> }</select></label>
          <label class="block">Concept de repli pour les relations
            <select (change)="draft.setting('fallbackRelationType', $any($event.target).value)">
              @for (c of store.catalog().archimate.relationships; track c) { <option [value]="c" [selected]="c === t.fallbackRelationType">{{ c }}</option> }</select></label>
          <label class="block">Propriété marqueur
            <input type="text" [value]="t.typeMarkerProperty" (change)="draft.setting('typeMarkerProperty', $any($event.target).value.trim())" />
            <span class="muted small">Écrite à l'export sur ces objets pour que le réimport retrouve leur type.</span></label>
        </fieldset>
        <fieldset>
          <legend>Import : concept sans règle</legend>
          <label class="block">Type d'objet qui les reçoit
            <select (change)="draft.setting('unmappedTypeKey', $any($event.target).value)">
              @for (o of store.types(); track o.key) { <option [value]="o.key" [selected]="o.key === t.unmappedTypeKey">{{ o.label }}</option> }</select></label>
          <label class="block">Type de relation qui les reçoit
            <select (change)="draft.setting('unmappedRelationTypeKey', $any($event.target).value)">
              @for (o of store.relTypes(); track o.key) { <option [value]="o.key" [selected]="o.key === t.unmappedRelationTypeKey">{{ o.label }}</option> }</select></label>
          <p class="muted small">Le type « {{ store.typeLabel(t.unmappedTypeKey) }} » et la relation « {{ store.relLabel(t.unmappedRelationTypeKey) }} » sont des réceptacles normaux, pas des anomalies : ils ne peuvent pas être supprimés.</p>
        </fieldset>
      </div>
      @for (e of draft.errors().get('settings') ?? []; track e) { <p><span class="chip bad">Erreur</span> {{ e }}</p> }
    </div>
  `,
})
export class MatrixSettings {
  protected readonly store = inject(MetaStore);
  protected readonly draft = inject(TranscodingDraft);
}
