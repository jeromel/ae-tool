import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { MetaStore, plain } from './store';

/** Couches : liste ordonnée. Les modifications restent en brouillon jusqu'à « Enregistrer » ; l'ordre est celui des vues et des espaces. */
@Component({
  selector: 'app-layers-tab',
  template: `
    <p class="lead">Une couche est un classement de présentation : elle regroupe les types dans les listes et les synthèses. Elle ne décide d'aucune fonction (voir les rôles).</p>
    <div class="card">
      <table>
        <tr><th scope="col">Ordre</th><th scope="col">Libellé</th><th scope="col">Clé</th><th scope="col" class="num">Types</th><th scope="col"><span class="sr">Actions</span></th></tr>
        @for (l of layers(); track l.key; let i = $index; let first = $first; let last = $last) {
          <tr>
            <td>{{ i + 1 }}</td>
            <td><input type="text" [value]="l.label" [attr.aria-label]="'Libellé de la couche ' + l.key" (input)="rename(i, $any($event.target).value)" /></td>
            <td class="muted small">{{ l.key }}</td>
            <td class="num"><span [class.muted]="!typeCount(l.key)">{{ typeCount(l.key) }}</span></td>
            <td class="actions">
              <button type="button" class="ghost" [disabled]="first" (click)="move(i, -1)" [attr.aria-label]="'Monter ' + l.label">Monter</button>
              <button type="button" class="ghost" [disabled]="last" (click)="move(i, 1)" [attr.aria-label]="'Descendre ' + l.label">Descendre</button>
              <button type="button" class="ghost" [disabled]="typeCount(l.key) > 0" (click)="removeAt(i)" [attr.aria-label]="'Retirer ' + l.label"
                [title]="typeCount(l.key) ? typeCount(l.key) + ' type(s) utilisent cette couche : déplacez-les d\\'abord' : ''">Retirer</button>
            </td>
          </tr>
        }
      </table>
      <div class="tools" style="margin-top: 12px">
        <input type="text" placeholder="Nouvelle couche (ex. Sécurité)" aria-label="Libellé de la nouvelle couche" [value]="newLabel()" (input)="newLabel.set($any($event.target).value)" (keydown.enter)="add()" />
        <button type="button" class="ghost" (click)="add()" [disabled]="!newLabel().trim()">Ajouter</button>
      </div>
      <div class="tools">
        <button type="button" (click)="save()" [disabled]="!dirty() || !valid()">Enregistrer</button>
        <button type="button" class="ghost" (click)="reset()" [disabled]="!dirty()">Annuler les modifications</button>
        @if (dirty()) { <span class="chip warn">Modifications non enregistrées</span> }
        @if (!valid()) { <span class="chip bad">Chaque couche doit avoir un libellé</span> }
      </div>
    </div>
  `,
})
export class LayersTab {
  private readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  protected readonly layers = signal<{ key: string; label: string; order: number }[]>([]);
  protected readonly newLabel = signal('');
  private readonly baseline = computed(() => JSON.stringify(this.store.m().layers.map((l: any) => [l.key, l.label])));
  protected readonly dirty = computed(() => JSON.stringify(this.layers().map((l) => [l.key, l.label])) !== this.baseline());
  protected readonly valid = computed(() => this.layers().every((l) => l.label.trim()));

  constructor() {
    this.reset();
  }

  protected reset(): void {
    this.layers.set([...this.store.m().layers].sort((a: any, b: any) => a.order - b.order).map((l: any) => ({ ...l })));
  }

  protected typeCount(key: string): number {
    return this.store.m().objectTypes.filter((t: any) => t.layerKey === key).length;
  }

  protected rename(i: number, label: string): void {
    this.layers.update((ls) => ls.map((l, j) => (j === i ? { ...l, label } : l)));
  }

  protected move(i: number, by: number): void {
    this.layers.update((ls) => {
      const next = [...ls];
      [next[i], next[i + by]] = [next[i + by], next[i]];
      return next;
    });
  }

  protected removeAt(i: number): void {
    this.layers.update((ls) => ls.filter((_, j) => j !== i));
  }

  protected add(): void {
    const label = this.newLabel().trim();
    if (!label) return;
    const base = plain(label).replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'couche';
    const taken = new Set(this.layers().map((l) => l.key));
    let key = base;
    for (let n = 2; taken.has(key); n++) key = `${base}-${n}`;
    this.layers.update((ls) => [...ls, { key, label, order: ls.length }]);
    this.newLabel.set('');
  }

  protected async save(): Promise<void> {
    const body = this.layers().map((l, order) => ({ key: l.key, label: l.label.trim(), order }));
    const ok = await this.store.run(async () => {
      const saved = await this.api.put('/metamodel/layers', body);
      this.store.patch({ layers: saved });
    });
    if (ok) this.reset();
  }
}
