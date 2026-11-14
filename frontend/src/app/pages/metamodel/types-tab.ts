import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { TypePreview } from './glyph-preview';
import { Removal } from './removal';
import { MetaStore, plain } from './store';
import { TypeDetail } from './type-detail';

/** Types d'objets : tableau groupé par couche, détail à droite. Le libellé et la couche se changent directement dans le tableau. */
@Component({
  selector: 'app-types-tab',
  imports: [TypePreview, TypeDetail, Removal],
  template: `
    <div class="tools">
      <input type="search" placeholder="Rechercher un type" aria-label="Rechercher un type par libellé" [value]="search()" (input)="search.set($any($event.target).value)" />
      <label class="inline"><input type="checkbox" [checked]="showUnused()" (change)="showUnused.set($any($event.target).checked)" /> Afficher les types inutilisés</label>
      <span class="muted small">{{ shown() }} {{ shown() > 1 ? 'types affichés' : 'type affiché' }} sur {{ store.types().length }}@if (hidden() > 0 && !search()) { ({{ hidden() }} masqués : types du gabarit sans objet ni rôle) }</span>
    </div>
    <div class="card inline-form" role="group" aria-label="Nouveau type">
      <b>Nouveau type</b>
      <input type="text" placeholder="Libellé (ex. Quartier)" aria-label="Libellé du nouveau type" [value]="newLabel()" (input)="newLabel.set($any($event.target).value)" (keydown.enter)="create()" />
      <select aria-label="Couche du nouveau type" [value]="newLayer()" (change)="newLayer.set($any($event.target).value)">
        @for (l of store.m().layers; track l.key) { <option [value]="l.key">{{ l.label }}</option> }
      </select>
      <button type="button" (click)="create()" [disabled]="!newLabel().trim()">Créer</button>
      <span class="muted small">La clé est créée par le serveur à partir du libellé.</span>
    </div>

    @if (removing(); as r) {
      <app-removal path="/metamodel/object-types" [item]="r" noun="objet" [count]="count(r.key)" [rules]="ruleCount(r.key)" [options]="otherTypes(r.key)"
        (done)="removed(r.key)" (cancel)="removing.set(null)" />
    }

    <div class="mm-split">
      <div class="card flush mm-main">
        <div class="scroll tall" style="--above: 400px">
          <table>
            <tr><th scope="col">Aperçu</th><th scope="col">Libellé</th><th scope="col">Couche</th><th scope="col">Rôles</th><th scope="col" class="num">Objets</th><th scope="col"><span class="sr">Détail</span></th></tr>
            @for (g of groups(); track g.layer.key) {
              <tr class="group"><td colspan="6">
                <button type="button" class="fold" [attr.aria-expanded]="!folded().has(g.layer.key)" (click)="fold(g.layer.key)">
                  {{ folded().has(g.layer.key) ? '▸' : '▾' }} {{ g.layer.label }} <span class="muted">({{ g.types.length }})</span></button>
              </td></tr>
              @if (!folded().has(g.layer.key)) {
                @for (t of g.types; track t.key) {
                  <tr [class.on]="t.key === selected()" [class.archived]="t.archived">
                    <td><app-type-preview [visual]="t.visual" [width]="34" /></td>
                    <td><input type="text" [value]="t.label" [attr.aria-label]="'Libellé du type ' + t.key" (change)="rename(t, $any($event.target))" />
                      <div class="muted small">{{ t.key }}@if (t.archived) { · archivé }</div></td>
                    <td><select [attr.aria-label]="'Couche de ' + t.label" (change)="move(t, $any($event.target).value)">
                      @for (l of store.m().layers; track l.key) { <option [value]="l.key" [selected]="t.layerKey === l.key">{{ l.label }}</option> }</select></td>
                    <td>@for (r of t.roles; track r) { <span class="chip info">{{ store.roleLabel(r) }}</span> } @empty { <span class="muted small">aucun</span> }</td>
                    <td class="num"><span [class.muted]="!count(t.key)">{{ count(t.key) }}</span></td>
                    <td class="actions"><button type="button" class="ghost" [attr.aria-pressed]="t.key === selected()" (click)="select(t.key)">Détail</button></td>
                  </tr>
                }
              }
            } @empty { <tr><td colspan="6" class="muted">Aucun type ne correspond. Cochez « Afficher les types inutilisés » pour voir les types du gabarit sans objet ni rôle.</td></tr> }
          </table>
        </div>
      </div>
      @if (current(); as t) {
        <aside class="card mm-aside" aria-label="Détail du type">
          <app-type-detail [type]="t" (close)="selected.set('')" (remove)="removing.set({ key: t.key, label: t.label })" />
        </aside>
      }
    </div>
  `,
})
export class TypesTab {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  protected readonly search = signal('');
  protected readonly showUnused = signal(false);
  protected readonly selected = signal('');
  protected readonly folded = signal(new Set<string>());
  protected readonly newLabel = signal('');
  protected readonly newLayer = signal(this.store.m().layers.find((l: any) => l.key === 'fonctionnelle')?.key ?? this.store.m().layers[0].key);
  protected readonly removing = signal<{ key: string; label: string } | null>(null);

  protected count = (key: string) => this.store.usage().objectTypes[key] ?? 0;
  private visible = computed(() => {
    const q = plain(this.search().trim());
    // Seuls les types du gabarit livré se replient tant qu'ils ne servent pas : un type créé ici reste toujours visible.
    const template = new Set<string>(this.store.catalog().templateObjectTypeKeys);
    return this.store.types().filter((t) => (q ? plain(t.label).includes(q)
      : this.showUnused() || this.count(t.key) > 0 || t.roles.length > 0 || !template.has(t.key)));
  });
  protected shown = computed(() => this.visible().length);
  protected hidden = computed(() => this.store.types().length - this.shown());
  protected groups = computed(() =>
    [...this.store.m().layers].sort((a: any, b: any) => a.order - b.order)
      .map((layer: any) => ({ layer, types: this.visible().filter((t) => t.layerKey === layer.key) }))
      .filter((g) => g.types.length > 0));
  protected current = computed(() => this.store.types().find((t) => t.key === this.selected()));

  protected select(key: string): void {
    this.selected.set(this.selected() === key ? '' : key);
  }

  protected fold(layer: string): void {
    this.folded.update((s) => { const n = new Set(s); n.has(layer) ? n.delete(layer) : n.add(layer); return n; });
  }

  protected otherTypes(key: string) {
    return this.store.types().filter((t) => t.key !== key).map((t) => ({ key: t.key, label: t.label, hint: this.store.layerLabel(t.layerKey) }));
  }

  protected ruleCount(key: string): number {
    return this.store.m().transcoding.elements.filter((r: any) => r.typeKey === key).length;
  }

  protected rename(type: any, input: HTMLInputElement): void {
    const label = input.value.trim();
    if (!label) { input.value = type.label; return; }
    void this.save({ ...type, label });
  }

  protected move(type: any, layerKey: string): void {
    void this.save({ ...type, layerKey });
  }

  private async save(body: any): Promise<void> {
    await this.store.run(async () => {
      const saved = await this.api.put(`/metamodel/object-types/${encodeURIComponent(body.key)}`, body);
      this.store.patch({ objectTypes: this.store.m().objectTypes.map((x: any) => (x.key === saved.key ? saved : x)) });
    });
  }

  protected async create(): Promise<void> {
    const label = this.newLabel().trim();
    if (!label) return;
    const ok = await this.store.run(async () => {
      const created = await this.api.post('/metamodel/object-types', { label, layerKey: this.newLayer() });
      this.store.patch({ objectTypes: [...this.store.m().objectTypes, created] });
      // Un type neuf n'a ni objet ni rôle : on le cherche par son libellé pour qu'il reste visible, et on ouvre son détail.
      this.search.set(label);
      this.selected.set(created.key);
    });
    if (ok) this.newLabel.set('');
  }

  protected async removed(key: string): Promise<void> {
    this.removing.set(null);
    if (this.selected() === key) this.selected.set('');
    await this.store.load();
  }
}
