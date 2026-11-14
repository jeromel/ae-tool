import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { MetaStore } from './store';

/** Réglages du métamodèle, et lecture d'ensemble des rôles : quel type fait quoi. */
@Component({
  selector: 'app-settings-tab',
  template: `
    <div class="row">
      <div class="card">
        <h2>Réglages</h2>
        <div class="form">
          <label>Dossier des projets dans Archi
            <input type="text" [value]="draft().projectFolderLabel" (input)="set('projectFolderLabel', $any($event.target).value)" />
            <span class="muted small">Nom du dossier dont les sous-dossiers portent le nom des projets.</span></label>
          <label>Propriété du protocole d'un flux
            <input type="text" [value]="draft().flowProtocolKey" (input)="set('flowProtocolKey', $any($event.target).value)" />
            <span class="muted small">Clé de propriété affichée sur la pastille d'une relation de flux.</span></label>
          <label>Propriété du chiffrement d'un flux
            <input type="text" [value]="draft().flowEncryptionKey" (input)="set('flowEncryptionKey', $any($event.target).value)" />
            <span class="muted small">Clé de propriété qui dit si le flux est chiffré (cadenas sur les vues).</span></label>
          <label>État des objets importés d'Archi
            <select (change)="set('archiImportStatus', $any($event.target).value)">
              @for (s of statuses; track s) { <option [value]="s" [selected]="draft().archiImportStatus === s">{{ s }}</option> }
            </select>
            <span class="muted small">« Validé » si le modèle Archi fait référence. « Proposé » pour qu'un valideur relise chaque nouvel objet importé. Ne change rien aux objets déjà présents.</span></label>
        </div>
        <div class="tools" style="margin-top: 12px">
          <button type="button" (click)="save()" [disabled]="!dirty() || !valid()">Enregistrer</button>
          @if (dirty()) { <span class="chip warn">Modifications non enregistrées</span> }
          @if (!valid()) { <span class="chip bad">Tous les réglages sont obligatoires</span> }
        </div>
      </div>
      <div class="card">
        <h2>Rôles : qui fait quoi</h2>
        <p class="muted small">Les rôles se règlent sur chaque type, dans les onglets Types et Relations. Cette vue d'ensemble montre ce que l'outil retiendra pour chaque fonction.</p>
        <dl class="kv">
          @for (r of store.catalog().objectRoles; track r.key) {
            <dt>{{ r.label }}</dt><dd>@if (store.typesWith(r.key).length) { {{ store.typesWith(r.key).join(', ') }} } @else { <span class="chip warn">Aucun type</span> }</dd>
          }
          @for (r of store.catalog().relationshipRoles; track r.key) {
            <dt>{{ r.label }} (relation)</dt><dd>@if (relationsWith(r.key).length) { {{ relationsWith(r.key).join(', ') }} } @else { <span class="chip warn">Aucun type</span> }</dd>
          }
        </dl>
      </div>
    </div>
  `,
})
export class SettingsTab {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  protected readonly draft = signal<any>({ ...this.store.m().settings });
  protected readonly dirty = computed(() => JSON.stringify(this.draft()) !== JSON.stringify(this.store.m().settings));
  protected readonly statuses = ['Brouillon', 'Proposé', 'Validé'];
  protected readonly valid = computed(() => Object.values(this.draft()).every((v) => String(v ?? '').trim() !== ''));

  protected set(field: string, value: string): void {
    this.draft.update((d: any) => ({ ...d, [field]: value }));
  }

  protected relationsWith(role: string): string[] {
    return this.store.relTypes().filter((t) => t.roles.includes(role)).map((t) => t.label);
  }

  protected async save(): Promise<void> {
    const body = Object.fromEntries(Object.entries(this.draft()).map(([k, v]) => [k, String(v).trim()]));
    const ok = await this.store.run(async () => {
      this.store.patch({ settings: await this.api.put('/metamodel/settings', body) });
    });
    if (ok) this.draft.set({ ...this.store.m().settings });
  }
}
