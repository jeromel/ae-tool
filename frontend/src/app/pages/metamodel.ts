import { Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../api';
import { ConfirmPanel } from './metamodel/confirm-panel';
import { LayersTab } from './metamodel/layers-tab';
import { NotationTab } from './metamodel/notation-tab';
import { PropertiesTab } from './metamodel/properties-tab';
import { RelationsTab } from './metamodel/relations-tab';
import { SettingsTab } from './metamodel/settings-tab';
import { MetaStore } from './metamodel/store';
import { TranscodingTab } from './metamodel/transcoding-tab';
import { TypesTab } from './metamodel/types-tab';

const TABS = [
  { key: 'types', label: 'Types d\'objets' }, { key: 'relations', label: 'Relations' }, { key: 'proprietes', label: 'Propriétés' },
  { key: 'couches', label: 'Couches' }, { key: 'transcodification', label: 'Transcodification ArchiMate' },
  { key: 'notation', label: 'Notation' }, { key: 'reglages', label: 'Réglages' },
];

/** Paramétrage du métamodèle : un onglet par sujet, l'onglet courant est porté par l'adresse (/metamodele/types…). */
@Component({
  imports: [ConfirmPanel, TypesTab, RelationsTab, PropertiesTab, LayersTab, TranscodingTab, NotationTab, SettingsTab],
  template: `
    <h1>Métamodèle</h1>
    <p class="lead">
      Types d'objets, relations, propriétés et couches sont des données de l'outil. Les rôles portés par un type déterminent ce que l'outil en fait ;
      la correspondance avec ArchiMate est portée par la matrice de transcodification.
    </p>
    <div class="tools">
      <a class="btn ghost" [href]="api.url('/metamodel/export')">Exporter le métamodèle (JSON)</a>
      <label class="btn ghost" tabindex="0" (keydown.enter)="file.click()" (keydown.space)="file.click(); $event.preventDefault()">Importer un métamodèle
        <input #file type="file" accept=".json" hidden (change)="choose($event)" /></label>
    </div>
    @if (pending(); as f) {
      <app-confirm title="Remplacer le métamodèle" action="Remplacer" (confirm)="importFile()" (cancel)="pending.set(null)">
        <p>Le métamodèle actuel ({{ store.m()?.objectTypes.length }} types d'objets, {{ store.m()?.relationshipTypes.length }} types de relations, {{ store.m()?.propertyDefinitions.length }} propriétés) sera remplacé par celui du fichier « {{ f.name }} », matrice de transcodification comprise.
          Un type utilisé par des objets mais absent du fichier est conservé, archivé. Aucun objet n'est supprimé.</p>
      </app-confirm>
    }
    @if (store.m()) {
      <div class="tabs" role="tablist" aria-label="Parties du métamodèle">
        @for (t of tabs; track t.key) {
          <button type="button" role="tab" [id]="'tab-' + t.key" [attr.aria-selected]="t.key === current()" [attr.aria-controls]="'panel'" (click)="go(t.key)">{{ t.label }}</button>
        }
      </div>
      <div id="panel" role="tabpanel" [attr.aria-labelledby]="'tab-' + current()">
        <!-- Le numéro de version change après un import : l'onglet repart d'un état propre. -->
        @for (v of [store.version()]; track v) {
          @switch (current()) {
            @case ('relations') { @defer (on immediate) { <app-relations-tab /> } }
            @case ('proprietes') { @defer (on immediate) { <app-properties-tab /> } }
            @case ('couches') { @defer (on immediate) { <app-layers-tab /> } }
            @case ('transcodification') { @defer (on immediate) { <app-transcoding-tab /> } }
            @case ('notation') { @defer (on immediate) { <app-notation-tab /> } }
            @case ('reglages') { @defer (on immediate) { <app-settings-tab /> } }
            @default { @defer (on immediate) { <app-types-tab /> } }
          }
        }
      </div>
    }
    @if (store.error()) { <div class="toast bad" role="alert">{{ store.error() }} <button type="button" class="ghost" (click)="store.error.set('')">Fermer</button></div> }
    @else if (store.saved()) { <div class="toast ok" role="status">Enregistré</div> }
  `,
})
export class MetamodelPage {
  protected readonly api = inject(Api);
  protected readonly store = inject(MetaStore);
  private readonly router = inject(Router);
  /** Segment d'adresse /metamodele/:onglet ; absent = types. */
  onglet = input<string>();
  protected readonly tabs = TABS;
  protected readonly pending = signal<File | null>(null);
  protected readonly current = computed(() => TABS.find((t) => t.key === this.onglet())?.key ?? 'types');

  constructor() {
    void this.store.load();
  }

  protected go(tab: string): void {
    this.store.error.set('');
    void this.router.navigate(['/metamodele', tab]);
  }

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pending.set(input.files?.[0] ?? null);
    input.value = '';
  }

  protected async importFile(): Promise<void> {
    const file = this.pending();
    if (!file) return;
    this.pending.set(null);
    if (await this.store.run(() => this.api.upload('/metamodel/import', file))) await this.store.load();
  }
}
