import { Component, computed, inject, input, output, signal } from '@angular/core';
import { ConfirmPanel } from './confirm-panel';
import { MetaStore } from './store';

/**
 * Suppression d'un type d'objet ou de relation. Sans objet concerné, une confirmation suffit ; sinon le type ne peut disparaître
 * qu'en fusionnant ses objets dans un autre type, et le panneau dit combien seront retypés. Le serveur reste juge :
 * s'il refuse (objets archivés encore rattachés), le panneau bascule en mode fusion.
 */
@Component({
  selector: 'app-removal',
  imports: [ConfirmPanel],
  template: `
    <app-confirm [title]="merging() ? 'Fusionner « ' + label() + ' » dans un autre type' : 'Supprimer « ' + label() + ' »'"
      [action]="merging() ? 'Fusionner et supprimer' : 'Supprimer'" [disabled]="busy() || (merging() && !target())" (confirm)="go()" (cancel)="cancel.emit()">
      @if (merging()) {
        <p>
          {{ count() === 0 ? 'Des ' + plural() + ' archivés ont encore ce type.' : count() + ' ' + (count() > 1 ? plural() : noun()) + ' ' + (count() > 1 ? 'ont' : 'a') + ' ce type.' }}
          Un type utilisé ne peut pas être supprimé : ses {{ plural() }} doivent être fusionnés dans un autre type.
        </p>
        <label>Fusionner dans
          <select [value]="target()" (change)="target.set($any($event.target).value)">
            <option value="">Choisir un type…</option>
            @for (o of options(); track o.key) { <option [value]="o.key">{{ o.label }}{{ o.hint ? ' (' + o.hint + ')' : '' }}</option> }
          </select>
        </label>
        @if (target()) {
          <p>
            Les <b>{{ count() }}</b> {{ plural() }} actifs de « {{ label() }} » deviendront des « {{ targetLabel() }} » (les {{ plural() }} archivés aussi).
            {{ effects() }} Cette fusion est enregistrée dans le journal.
          </p>
        }
      } @else {
        <p>Aucun {{ noun() }} actif n'a ce type. {{ effects() }}</p>
      }
      @if (refusal()) { <p class="muted small">{{ refusal() }}</p> }
    </app-confirm>
  `,
  styles: 'label { display: flex; flex-direction: column; gap: 4px; font-weight: 600; max-width: 360px; margin-bottom: 8px; }',
})
export class Removal {
  protected readonly store = inject(MetaStore);
  /** Chemin de l'API : /metamodel/object-types ou /metamodel/relationship-types. */
  path = input.required<string>();
  item = input.required<{ key: string; label: string }>();
  noun = input('objet');
  /** Effectif actif du type. */
  count = input(0);
  /** Nombre de règles de transcodification qui disparaissent avec le type. */
  rules = input(0);
  /** Types de destination possibles. */
  options = input.required<{ key: string; label: string; hint?: string }[]>();
  done = output<void>();
  cancel = output<void>();

  protected readonly merging = signal(false);
  protected readonly target = signal('');
  protected readonly busy = signal(false);
  protected readonly refusal = signal('');
  protected label = computed(() => this.item().label);
  protected targetLabel = computed(() => this.options().find((o) => o.key === this.target())?.label ?? '');
  protected plural = computed(() => (this.noun() === 'objet' ? 'objets' : 'relations'));
  protected effects = computed(() => {
    const r = this.rules();
    const rules = r === 0 ? 'Aucune règle de transcodification n\'est rattachée à ce type.'
      : `${r} règle${r > 1 ? 's' : ''} de transcodification rattachée${r > 1 ? 's' : ''} à ce type ${r > 1 ? 'seront retirées' : 'sera retirée'}.`;
    return this.noun() === 'objet' ? `${rules} Ses couples autorisés et sa mention dans « Concerne » des propriétés seront aussi retirés.` : rules;
  });

  constructor() {
    queueMicrotask(() => this.merging.set(this.count() > 0));
  }

  async go(): Promise<void> {
    this.busy.set(true);
    this.refusal.set('');
    const query = this.merging() ? `?mergeInto=${encodeURIComponent(this.target())}` : '';
    const url = `${this.path()}/${encodeURIComponent(this.item().key)}${query}`;
    const ok = await this.store.run(() => this.store.remove(url));
    this.busy.set(false);
    if (ok) {
      this.done.emit();
      return;
    }
    // Refus sans destination : le serveur voit des objets que l'effectif des objets actifs ne montre pas.
    if (!this.merging()) {
      this.refusal.set(this.store.error());
      this.merging.set(true);
    }
  }
}
