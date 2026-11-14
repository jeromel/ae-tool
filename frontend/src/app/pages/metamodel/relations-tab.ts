import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { LinePreview } from './glyph-preview';
import { RelationDetail } from './relation-detail';
import { Removal } from './removal';
import { MetaStore } from './store';

/** Types de relations : libellés et lecture inverse dans le tableau, rendu, rôles et couples autorisés dans le détail. */
@Component({
  selector: 'app-relations-tab',
  imports: [LinePreview, RelationDetail, Removal],
  template: `
    <div class="card inline-form" role="group" aria-label="Nouveau type de relation">
      <b>Nouveau type de relation</b>
      <input type="text" placeholder="Libellé (ex. Dessert)" aria-label="Libellé du nouveau type de relation" [value]="newLabel()" (input)="newLabel.set($any($event.target).value)" (keydown.enter)="create()" />
      <input type="text" placeholder="Lecture inverse (ex. Est desservi par)" aria-label="Lecture inverse du nouveau type" [value]="newReverse()" (input)="newReverse.set($any($event.target).value)" (keydown.enter)="create()" />
      <button type="button" (click)="create()" [disabled]="!newLabel().trim()">Créer</button>
    </div>

    @if (removing(); as r) {
      <app-removal path="/metamodel/relationship-types" [item]="r" noun="relation" [count]="count(r.key)" [rules]="ruleCount(r.key)" [options]="others(r.key)"
        (done)="removed(r.key)" (cancel)="removing.set(null)" />
    }

    <div class="mm-split">
      <div class="card flush mm-main">
        <div class="scroll tall" style="--above: 330px">
          <table>
            <tr><th scope="col">Rendu</th><th scope="col">Libellé</th><th scope="col">Lecture inverse</th><th scope="col">Couples</th><th scope="col" class="num">Relations</th><th scope="col"><span class="sr">Détail</span></th></tr>
            @for (t of store.relTypes(); track t.key) {
              <tr [class.on]="t.key === selected()" [class.archived]="t.archived">
                <td><app-line-preview [line]="t.softLine || 'plein'" [head]="t.head || 'aucune'" [width]="70" /></td>
                <td><input type="text" [value]="t.label" [attr.aria-label]="'Libellé du type de relation ' + t.key" (change)="rename(t, $any($event.target))" />
                  <div class="muted small">{{ t.key }}@if (t.archived) { · archivé }</div>
                  @for (r of t.roles; track r) { <span class="chip info">{{ store.roleLabel(r) }}</span> }</td>
                <td><input type="text" [value]="t.reverseLabel ?? ''" [attr.aria-label]="'Lecture inverse de ' + t.label" placeholder="(aucune)" (change)="reverse(t, $any($event.target).value)" /></td>
                <td>@if (t.allowedPairs.length) { {{ t.allowedPairs.length }} } @else { <span class="muted small">libre</span> }</td>
                <td class="num"><span [class.muted]="!count(t.key)">{{ count(t.key) }}</span></td>
                <td class="actions"><button type="button" class="ghost" [attr.aria-pressed]="t.key === selected()" (click)="select(t.key)">Détail</button></td>
              </tr>
            }
          </table>
        </div>
      </div>
      @if (current(); as t) {
        <aside class="card mm-aside" aria-label="Détail du type de relation">
          <app-relation-detail [type]="t" (close)="selected.set('')" (remove)="removing.set({ key: t.key, label: t.label })" />
        </aside>
      }
    </div>
  `,
})
export class RelationsTab {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  protected readonly selected = signal('');
  protected readonly newLabel = signal('');
  protected readonly newReverse = signal('');
  protected readonly removing = signal<{ key: string; label: string } | null>(null);
  protected current = computed(() => this.store.relTypes().find((t) => t.key === this.selected()));
  protected count = (key: string) => this.store.usage().relationshipTypes[key] ?? 0;

  protected select(key: string): void {
    this.selected.set(this.selected() === key ? '' : key);
  }

  protected others(key: string) {
    return this.store.relTypes().filter((t) => t.key !== key).map((t) => ({ key: t.key, label: t.label }));
  }

  protected ruleCount(key: string): number {
    return this.store.m().transcoding.relations.filter((r: any) => r.typeKey === key).length;
  }

  protected rename(type: any, input: HTMLInputElement): void {
    const label = input.value.trim();
    if (!label) { input.value = type.label; return; }
    void this.save({ ...type, label });
  }

  protected reverse(type: any, value: string): void {
    void this.save({ ...type, reverseLabel: value.trim() || null });
  }

  private async save(body: any): Promise<void> {
    await this.store.run(async () => {
      const saved = await this.api.put(`/metamodel/relationship-types/${encodeURIComponent(body.key)}`, body);
      this.store.patch({ relationshipTypes: this.store.m().relationshipTypes.map((x: any) => (x.key === saved.key ? saved : x)) });
    });
  }

  protected async create(): Promise<void> {
    const label = this.newLabel().trim();
    if (!label) return;
    const ok = await this.store.run(async () => {
      const created = await this.api.post('/metamodel/relationship-types', { label, reverseLabel: this.newReverse().trim() || null });
      this.store.patch({ relationshipTypes: [...this.store.m().relationshipTypes, created] });
      this.selected.set(created.key);
    });
    if (ok) { this.newLabel.set(''); this.newReverse.set(''); }
  }

  protected async removed(key: string): Promise<void> {
    this.removing.set(null);
    if (this.selected() === key) this.selected.set('');
    await this.store.load();
  }
}
