import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Api } from '../../api';
import { LinePreview } from './glyph-preview';
import { MetaStore } from './store';

interface Observed { sourceTypeKey: string; targetTypeKey: string; count: number; }

/** Détail d'un type de relation : rôles, rendu, couples autorisés. Brouillon jusqu'à « Enregistrer ». */
@Component({
  selector: 'app-relation-detail',
  imports: [LinePreview],
  template: `
    @let t = type();
    <div class="head">
      <app-line-preview [line]="draft().softLine || 'plein'" [head]="draft().head || 'aucune'" [width]="96" />
      <div><h2>{{ t.label }}</h2><span class="muted small">Clé <code>{{ t.key }}</code>, non modifiable</span></div>
      <button type="button" class="ghost right" (click)="close.emit()" aria-label="Fermer le détail">Fermer</button>
    </div>

    <fieldset>
      <legend>Rôles fonctionnels</legend>
      @for (r of store.catalog().relationshipRoles; track r.key) {
        <label class="check">
          <input type="checkbox" [checked]="draft().roles.includes(r.key)" (change)="toggleRole(r.key, $any($event.target).checked)" />
          <span><b>{{ r.label }}</b><br /><span class="muted small">{{ r.effect }}</span></span>
        </label>
      }
    </fieldset>

    <fieldset>
      <legend>Rendu soigné des vues</legend>
      <p class="muted small">« Par défaut » donne un trait plein sans tête. Le rendu « Géométrie Archi exacte » n'est pas concerné.</p>
      <div class="grid">
        <label>Trait<select [value]="draft().softLine ?? ''" (change)="set('softLine', $any($event.target).value || null)">
          <option value="">par défaut (plein)</option>
          @for (l of store.catalog().lines; track l) { <option [value]="l">{{ l }}</option> }</select></label>
        <label>Tête<select [value]="draft().head ?? ''" (change)="set('head', $any($event.target).value || null)">
          <option value="">par défaut (aucune)</option>
          @for (h of store.catalog().heads; track h) { <option [value]="h">{{ h }}</option> }</select></label>
      </div>
    </fieldset>

    <fieldset>
      <legend>Couples autorisés</legend>
      @if (draft().allowedPairs.length === 0) {
        <p class="note">Aucun couple : <b>aucun contrôle</b>, une relation « {{ t.label }} » est acceptée entre n'importe quels types.</p>
      } @else {
        <p class="muted small">Seuls ces couples sont acceptés à la saisie ; les relations existantes qui n'en font pas partie sont signalées par le contrôle de conformité.</p>
        <ul class="pairs">
          @for (p of draft().allowedPairs; track p) {
            <li><span>{{ label(p, 0) }} <b aria-hidden="true">→</b><span class="sr"> vers </span> {{ label(p, 1) }}</span>
              <span class="muted small">{{ countOf(p) }} relation(s)</span>
              <button type="button" class="ghost" (click)="removePair(p)" [attr.aria-label]="'Retirer le couple ' + label(p, 0) + ' vers ' + label(p, 1)">Retirer</button></li>
          }
        </ul>
        @if (outside() > 0) { <p class="chip warn">{{ outside() }} relation(s) existante(s) hors de ces couples</p> }
      }
      <div class="tools">
        <select aria-label="Type source" [value]="source()" (change)="source.set($any($event.target).value)"><option value="">Type source…</option>
          @for (o of store.types(); track o.key) { <option [value]="o.key">{{ o.label }}</option> }</select>
        <span aria-hidden="true">→</span>
        <select aria-label="Type cible" [value]="target()" (change)="target.set($any($event.target).value)"><option value="">Type cible…</option>
          @for (o of store.types(); track o.key) { <option [value]="o.key">{{ o.label }}</option> }</select>
        <button type="button" class="ghost" (click)="addPair()" [disabled]="!source() || !target()">Ajouter</button>
      </div>
      <button type="button" class="ghost" (click)="adopt()" [disabled]="observed().length === 0"
        title="Ajoute à la liste les couples présents dans les relations actives de ce type ; rien n'est enregistré">Reprendre les couples observés ({{ observed().length }})</button>
      @if (adopted()) { <p class="muted small">{{ adopted() }} Relisez la liste, puis enregistrez.</p> }
    </fieldset>

    <label class="block">Description<textarea [value]="draft().description" (input)="set('description', $any($event.target).value)"></textarea></label>
    <label class="check"><input type="checkbox" [checked]="draft().archived" (change)="set('archived', $any($event.target).checked)" />
      <span><b>Archivé</b><br /><span class="muted small">Le type n'est plus proposé à la création de relations.</span></span></label>

    <div class="tools">
      <button type="button" (click)="save()" [disabled]="!dirty() || busy()">Enregistrer</button>
      <button type="button" class="ghost" (click)="reset()" [disabled]="!dirty()">Annuler les modifications</button>
      @if (dirty()) { <span class="chip warn">Modifications non enregistrées</span> }
      <button type="button" class="ghost danger right" (click)="remove.emit()">Supprimer ou fusionner…</button>
    </div>
  `,
})
export class RelationDetail {
  protected readonly store = inject(MetaStore);
  private readonly api = inject(Api);
  type = input.required<any>();
  close = output<void>();
  remove = output<void>();
  protected readonly busy = signal(false);
  protected readonly source = signal('');
  protected readonly target = signal('');
  protected readonly observed = signal<Observed[]>([]);
  protected readonly adopted = signal('');
  protected readonly draft = signal<any>({ roles: [], softLine: null, head: null, description: '', allowedPairs: [], archived: false });
  protected readonly dirty = computed(() => JSON.stringify(this.draft()) !== JSON.stringify(this.fromType()));
  /** Relations existantes dont le couple n'est pas dans la liste courante (0 quand la liste est vide : aucun contrôle). */
  protected readonly outside = computed(() => {
    const pairs = this.draft().allowedPairs as string[];
    return pairs.length === 0 ? 0 : this.observed().filter((o) => !pairs.includes(`${o.sourceTypeKey}>${o.targetTypeKey}`)).reduce((n, o) => n + o.count, 0);
  });

  constructor() {
    effect(() => {
      const key = this.type().key;
      untracked(() => {
        this.reset();
        this.observed.set([]);
        void this.api.get<Observed[]>(`/metamodel/relationship-types/${encodeURIComponent(key)}/observed-pairs`)
          .then((o) => { if (this.type().key === key) this.observed.set(o); }).catch(() => undefined);
      });
    });
  }

  private fromType() {
    const t = this.type();
    return { roles: [...t.roles], softLine: t.softLine ?? null, head: t.head ?? null, description: t.description ?? '', allowedPairs: [...t.allowedPairs], archived: !!t.archived };
  }

  protected reset(): void {
    this.draft.set(this.fromType());
    this.adopted.set('');
  }

  protected set(field: string, value: unknown): void {
    this.draft.update((d: any) => ({ ...d, [field]: value }));
  }

  protected toggleRole(role: string, on: boolean): void {
    this.set('roles', on ? [...this.draft().roles, role] : this.draft().roles.filter((r: string) => r !== role));
  }

  protected label(pair: string, end: 0 | 1): string {
    return this.store.typeLabel(pair.split('>')[end]);
  }

  protected countOf(pair: string): number {
    const [s, t] = pair.split('>');
    return this.observed().find((o) => o.sourceTypeKey === s && o.targetTypeKey === t)?.count ?? 0;
  }

  protected addPair(): void {
    this.addPairs([`${this.source()}>${this.target()}`]);
    this.source.set('');
    this.target.set('');
  }

  protected removePair(pair: string): void {
    this.set('allowedPairs', this.draft().allowedPairs.filter((p: string) => p !== pair));
  }

  private addPairs(pairs: string[]): number {
    const known = new Set<string>(this.draft().allowedPairs);
    const fresh = pairs.filter((p) => !known.has(p));
    this.set('allowedPairs', [...this.draft().allowedPairs, ...fresh]);
    return fresh.length;
  }

  protected adopt(): void {
    const added = this.addPairs(this.observed().map((o) => `${o.sourceTypeKey}>${o.targetTypeKey}`));
    const total = this.observed().reduce((n, o) => n + o.count, 0);
    this.adopted.set(`${added} couple(s) ajouté(s), d'après ${total} relation(s) existante(s).`);
  }

  protected async save(): Promise<void> {
    const body = { ...this.type(), ...this.draft(), description: this.draft().description.trim() || null };
    this.busy.set(true);
    const ok = await this.store.run(async () => {
      const saved = await this.api.put(`/metamodel/relationship-types/${encodeURIComponent(body.key)}`, body);
      this.store.patch({ relationshipTypes: this.store.m().relationshipTypes.map((x: any) => (x.key === saved.key ? saved : x)) });
    });
    this.busy.set(false);
    if (ok) this.reset();
  }
}
