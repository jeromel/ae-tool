import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api, Brief } from '../api';
import { ObjectSearch } from '../object-search';
import { Session } from '../session';

interface Allowed { typeKey: string; label: string; reverseLabel: string | null; anyType: boolean; objectTypes: string[]; }

/**
 * Relations d'un objet, entrantes et sortantes, et leur saisie. Les types de relation et les objets proposés sont filtrés par les
 * couples autorisés du métamodèle. Une relation venue d'Archi se lit seulement : Archi en reste le maître.
 */
@Component({
  selector: 'app-object-relations',
  imports: [RouterLink, NgTemplateOutlet, ObjectSearch],
  template: `
    @if (notice()) { <div class="note" role="status">{{ notice() }}</div> }
    @if (canEdit() && hasArchi()) { <p class="muted small archi-note">Les relations venues d'Archi se modifient dans Archi.</p> }
    <div class="row">
      <div class="card">
        <h2>Relations sortantes ({{ outgoing().length }})</h2>
        <table>
          @for (r of outgoing(); track r.id) {
            <tr>
              <td class="muted" style="width: 130px">{{ r.typeLabel }} →</td>
              <td>
                <a [routerLink]="['/objets', r.other.id]">{{ r.other.name }}</a> <span class="muted small kind">{{ r.other.typeLabel }}</span>
                @if (r.name) { <div class="small muted">{{ r.name }}</div> }
                <div class="rel-actions"><ng-container *ngTemplateOutlet="actions; context: { r: r }" /></div>
                <ng-container *ngTemplateOutlet="editor; context: { r: r, source: objectTypeKey(), target: r.other.typeKey }" />
              </td>
            </tr>
          } @empty { <tr><td class="muted">Aucune.</td></tr> }
        </table>
      </div>
      <div class="card">
        <h2>Relations entrantes ({{ incoming().length }})</h2>
        <table>
          @for (r of incoming(); track r.id) {
            <tr>
              <td class="muted" style="width: 130px">{{ reverseOf(r.typeKey) ?? (r.typeLabel + ' ←') }}</td>
              <td>
                <a [routerLink]="['/objets', r.other.id]">{{ r.other.name }}</a> <span class="muted small kind">{{ r.other.typeLabel }}</span>
                @if (r.name) { <div class="small muted">{{ r.name }}</div> }
                <div class="rel-actions"><ng-container *ngTemplateOutlet="actions; context: { r: r }" /></div>
                <ng-container *ngTemplateOutlet="editor; context: { r: r, source: r.other.typeKey, target: objectTypeKey() }" />
              </td>
            </tr>
          } @empty { <tr><td class="muted">Aucune.</td></tr> }
        </table>
      </div>
    </div>

    <ng-template #actions let-r="r">
      @if (canEdit()) {
        @if (sources()[r.id] === 'archi') {
          <!-- Une relation d'Archi se lit seulement ; la mention est dite une fois, sous les relations (§6, O2). -->
        } @else if (confirming() === r.id) {
          <span class="small">Archiver cette relation ?</span>
          <button type="button" (click)="archive(r.id)">Archiver</button>
          <button type="button" class="ghost" (click)="confirming.set(null)">Annuler</button>
        } @else if (editing() !== r.id) {
          <button type="button" class="ghost" (click)="startEdit(r)">Modifier</button>
          <button type="button" class="ghost" (click)="confirming.set(r.id)">Archiver</button>
        }
      }
    </ng-template>

    <ng-template #editor let-r="r" let-source="source" let-target="target">
      @if (editing() === r.id) {
        <div class="inline-edit">
          <label>Type de relation
            <select (change)="editType.set($any($event.target).value)">
              @for (t of typesFor(source, target, r.typeKey); track t.key) { <option [value]="t.key" [selected]="t.key === editType()">{{ t.label }}</option> }
            </select>
          </label>
          <label>Nom
            <input type="text" [value]="editName()" (input)="editName.set($any($event.target).value)" />
          </label>
          @if (editError()) { <div class="error" role="alert">{{ editError() }}</div> }
          <span class="buttons">
            <button type="button" (click)="saveEdit(r.id)">Enregistrer</button>
            <button type="button" class="ghost" (click)="editing.set(null)">Annuler</button>
          </span>
        </div>
      }
    </ng-template>

    @if (canEdit()) {
      @if (!adding()) {
        <p><button type="button" (click)="adding.set(true)">Ajouter une relation</button></p>
      } @else {
        <form class="card add" novalidate (submit)="create($event)">
          <h2>Ajouter une relation</h2>
          @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
          <div class="field">
            <span class="lbl" id="dir-label">Sens</span>
            <div class="segmented" role="group" aria-labelledby="dir-label">
              <button type="button" [attr.aria-pressed]="direction() === 'out'" (click)="setDirection('out')">Sortante : cet objet vers un autre</button>
              <button type="button" [attr.aria-pressed]="direction() === 'in'" (click)="setDirection('in')">Entrante : un autre objet vers celui-ci</button>
            </div>
          </div>
          <div class="field">
            <label for="rel-type">Type de relation</label>
            <select id="rel-type" (change)="setRelation($any($event.target).value)">
              <option value="" [selected]="!relKey()">Choisir un type de relation</option>
              @for (a of options(); track a.typeKey) { <option [value]="a.typeKey" [selected]="a.typeKey === relKey()">{{ optionLabel(a) }}</option> }
            </select>
            @if (!options().length) { <p class="hint">Le métamodèle n'autorise aucune relation de ce sens pour ce type d'objet.</p> }
          </div>
          <div class="field">
            <label>{{ direction() === 'out' ? 'Objet cible' : 'Objet source' }}</label>
            @if (other(); as o) {
              <div class="picked"><b>{{ o.name }}</b> <span class="muted small">{{ o.typeLabel }}</span>
                <button type="button" class="ghost" (click)="other.set(null)">Changer</button></div>
            } @else if (selected(); as s) {
              <app-object-search placeholder="Rechercher par nom (2 lettres au moins)" [types]="s.anyType ? undefined : s.objectTypes" [showAll]="false" (picked)="pick($event)" />
              <p class="hint">@if (s.anyType) { Tous les types d'objets sont admis. } @else { Types admis : {{ typeLabels(s.objectTypes) }}. }</p>
            } @else {
              <p class="hint">Choisissez d'abord un type de relation : la recherche ne proposera que les objets admis.</p>
            }
          </div>
          <div class="field">
            <label for="rel-name">Nom (facultatif)</label>
            <input id="rel-name" type="text" [value]="name()" (input)="name.set($any($event.target).value)" />
          </div>
          @if (selected() && other(); as o) {
            <p class="preview">{{ direction() === 'out' ? objectName() : o.name }} <span>— {{ selected()!.label }} →</span> {{ direction() === 'out' ? o.name : objectName() }}</p>
          }
          <div class="buttons">
            <button type="submit" [disabled]="busy()">Ajouter la relation</button>
            <button type="button" class="ghost" (click)="closeForm()">Annuler</button>
          </div>
        </form>
      }
    }
  `,
  styles: `
    .kind { margin-left: 6px; }
    .rel-actions { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; margin-top: 4px; }
    .rel-actions:empty { display: none; }
    .rel-actions button { padding: 2px 9px; font-size: 12.5px; }
    .add { max-width: 640px; display: flex; flex-direction: column; gap: 12px; }
    .add h2 { margin: 0; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    .field label, .lbl { font-weight: 600; }
    .field select, .field input { width: 100%; }
    .segmented { align-self: flex-start; flex-wrap: wrap; }
    .hint { margin: 0; font-size: 12px; color: var(--muted); }
    .picked { display: flex; gap: 10px; align-items: center; padding: 6px 10px; background: var(--accent-soft); border-radius: 6px; }
    .picked button { margin-left: auto; padding: 3px 9px; }
    .preview { margin: 0; padding: 8px 12px; background: #f6f8f8; border-radius: 6px; font-weight: 600; overflow-wrap: anywhere; }
    .preview span { color: var(--accent); font-weight: 400; }
    .buttons { display: flex; gap: 10px; }
    .inline-edit { display: flex; flex-direction: column; gap: 8px; margin-top: 8px; padding: 10px; background: #f6f8f8; border-radius: 6px; }
    .inline-edit label { display: flex; flex-direction: column; gap: 3px; font-weight: 600; }
    .inline-edit input, .inline-edit select { width: 100%; font-weight: 400; }
    .inline-edit .error { margin: 0; }
    .inline-edit .buttons button { margin: 0; font-size: 14px; padding: 6px 12px; }
  `,
})
export class ObjectRelations {
  private api = inject(Api);
  private session = inject(Session);
  objectId = input.required<string>();
  objectName = input.required<string>();
  objectTypeKey = input.required<string>();
  archived = input(false);
  outgoing = input.required<any[]>();
  incoming = input.required<any[]>();
  meta = input.required<any>();
  /** Émis après une création, une modification ou un archivage : la fiche se recharge. */
  changed = output<void>();

  allowed = signal<{ outgoing: Allowed[]; incoming: Allowed[] }>({ outgoing: [], incoming: [] });
  sources = signal<Record<string, string>>({});
  hasArchi = computed(() => Object.values(this.sources()).includes('archi'));
  notice = signal('');
  // Droit serveur : AccessRules.cs, écriture sous /api/relationships → contributeur.
  canEdit = computed(() => this.session.can('contributeur') && !this.archived());

  adding = signal(false);
  direction = signal<'out' | 'in'>('out');
  relKey = signal('');
  other = signal<Brief | null>(null);
  name = signal('');
  busy = signal(false);
  error = signal('');

  editing = signal<string | null>(null);
  editType = signal('');
  editName = signal('');
  editError = signal('');
  confirming = signal<string | null>(null);

  options = computed(() => (this.direction() === 'out' ? this.allowed().outgoing : this.allowed().incoming));
  selected = computed(() => this.options().find((a) => a.typeKey === this.relKey()) ?? null);

  constructor() {
    // Relu quand la fiche change d'objet ou se recharge (une relation a pu apparaître ou disparaître).
    effect(() => {
      const id = this.objectId();
      this.outgoing();
      this.incoming();
      this.api.get(`/objects/${id}/allowed-relations`).then((a) => this.allowed.set(a));
      this.api.get(`/objects/${id}/relationship-sources`).then((s) => this.sources.set(s));
    });
  }

  reverseOf(typeKey: string): string | null {
    return (this.meta().relationshipTypes as any[]).find((t) => t.key === typeKey)?.reverseLabel ?? null;
  }

  optionLabel(a: Allowed): string {
    return this.direction() === 'out' ? a.label : (a.reverseLabel ? `${a.reverseLabel} (${a.label})` : `${a.label} (entrante)`);
  }

  typeLabels(keys: string[]): string {
    const types = this.meta().objectTypes as any[];
    return keys.map((k) => types.find((t) => t.key === k)?.label ?? k).join(', ');
  }

  setDirection(d: 'out' | 'in'): void {
    this.direction.set(d);
    this.relKey.set('');
    this.other.set(null);
  }

  setRelation(key: string): void {
    this.relKey.set(key);
    const o = this.other();
    const s = this.selected();
    if (o && s && !s.anyType && !s.objectTypes.includes(o.typeKey)) this.other.set(null); // l'objet choisi n'est plus admis
  }

  pick(o: Brief): void {
    this.other.set(o);
    this.error.set('');
  }

  closeForm(): void {
    this.adding.set(false);
    this.relKey.set('');
    this.other.set(null);
    this.name.set('');
    this.error.set('');
  }

  async create(event: Event): Promise<void> {
    event.preventDefault();
    const other = this.other();
    if (!this.relKey() || !other) return this.error.set('Choisissez un type de relation et l\'objet à l\'autre extrémité.');
    const out = this.direction() === 'out';
    this.busy.set(true);
    try {
      await this.api.post('/relationships', {
        typeKey: this.relKey(), sourceId: out ? this.objectId() : other.id, targetId: out ? other.id : this.objectId(), name: this.name().trim() || null,
      });
      this.closeForm();
      this.say('Relation ajoutée.');
      this.changed.emit();
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'La relation n\'a pas pu être ajoutée.');
    } finally {
      this.busy.set(false);
    }
  }

  /** Types de relation qui acceptent le couple (source, cible) ; le type actuel reste proposé quoi qu'il arrive. */
  typesFor(source: string, target: string, current: string): any[] {
    return (this.meta().relationshipTypes as any[]).filter(
      (t) => t.key === current || (!t.archived && (!t.allowedPairs?.length || t.allowedPairs.includes(`${source}>${target}`))),
    );
  }

  startEdit(r: any): void {
    this.confirming.set(null);
    this.editError.set('');
    this.editType.set(r.typeKey);
    this.editName.set(r.name ?? '');
    this.editing.set(r.id);
  }

  async saveEdit(id: string): Promise<void> {
    try {
      await this.api.put(`/relationships/${id}`, { typeKey: this.editType(), name: this.editName() });
      this.editing.set(null);
      this.say('Relation modifiée.');
      this.changed.emit();
    } catch (e) {
      this.editError.set(e instanceof Error ? e.message : 'La modification a échoué.');
    }
  }

  async archive(id: string): Promise<void> {
    try {
      await this.api.delete(`/relationships/${id}`);
      this.confirming.set(null);
      this.say('Relation archivée.');
      this.changed.emit();
    } catch (e) {
      this.confirming.set(null);
      this.say(e instanceof Error ? e.message : 'L\'archivage a échoué.');
    }
  }

  private say(message: string): void {
    this.notice.set(message);
    setTimeout(() => this.notice.set(''), 5000);
  }
}
