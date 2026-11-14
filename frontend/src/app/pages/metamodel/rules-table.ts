import { Component, computed, inject, input, signal } from '@angular/core';
import { MetaStore, plain } from './store';
import { RuleKind, TranscodingDraft } from './transcoding-draft';

/**
 * Règles de transcodification des objets ou des relations, groupées par type de l'outil. Les types sans règle restent visibles :
 * on y lit ce que devient leur export (concept de repli et marqueur), lus dans la matrice en cours d'édition.
 */
@Component({
  selector: 'app-rules-table',
  template: `
    @let t = draft.t();
    <p class="muted">
      @if (kind() === 'elements') {
        À l'import, la règle dit quel type de l'outil reçoit un concept ArchiMate ; avec un discriminant, seuls les éléments portant cette propriété avec cette valeur sont concernés
        (c'est ainsi que plusieurs types partagent <i>Grouping</i>). À l'export, la règle principale du type donne le concept ; un objet importé garde son concept d'origine.
      } @else {
        À l'import, la règle dit quel type de relation de l'outil reçoit un concept de relation ArchiMate ; les contraintes de type source et cible départagent plusieurs règles
        (la plus contrainte l'emporte). À l'export, la règle principale du type donne le concept.
      }
    </p>
    <div class="tools">
      <input type="search" placeholder="Rechercher un type" aria-label="Rechercher un type" [value]="search()" (input)="search.set($any($event.target).value)" />
      <label class="inline"><input type="checkbox" [checked]="showAll()" (change)="showAll.set($any($event.target).checked)" /> Afficher les types archivés sans règle</label>
      <span class="muted small">{{ groups().length }} types affichés</span>
    </div>
    <datalist id="archi-properties">@for (n of archiNames(); track n) { <option [value]="n"></option> }</datalist>
    <div class="card flush">
      <div class="scroll tall" style="--above: 470px">
        <table class="rules">
          <tr>
            <th scope="col">Concept ArchiMate</th>
            @if (kind() === 'elements') { <th scope="col">Discriminant : propriété Archi</th><th scope="col">valeur</th> }
            @else { <th scope="col">Source (type)</th><th scope="col">Cible (type)</th> }
            <th scope="col" class="num">Principale</th><th scope="col" class="num">{{ kind() === 'elements' ? 'Objets' : 'Relations' }}</th><th scope="col"><span class="sr">Retirer</span></th>
          </tr>
          @for (g of groups(); track g.type.key) {
            <tr class="typehead">
              <td colspan="5"><b>{{ g.type.label }}</b><span class="muted small" style="margin-left: 8px">{{ g.type.key }} · {{ count(g.type.key) }} {{ kind() === 'elements' ? 'objet(s)' : 'relation(s)' }}</span></td>
              <td class="actions"><button type="button" class="ghost" (click)="draft.add(kind(), g.type.key)" [attr.aria-label]="'Ajouter une règle pour ' + g.type.label">Ajouter une règle</button></td>
            </tr>
            @for (x of g.rules; track x.i) {
              <tr [class.bad]="draft.errors().has(kind() + ':' + x.i)">
                <td><select [attr.aria-label]="'Concept ArchiMate de la règle de ' + g.type.label" (change)="draft.patch(kind(), x.i, { archimateType: $any($event.target).value })">
                  <option value="" [selected]="!x.r.archimateType">Choisir un concept…</option>
                  @if (x.r.archimateType && !concepts().includes(x.r.archimateType)) { <option [value]="x.r.archimateType" selected>{{ x.r.archimateType }} (inconnu)</option> }
                  @for (c of concepts(); track c) { <option [value]="c" [selected]="c === x.r.archimateType">{{ c }}</option> }</select></td>
                @if (kind() === 'elements') {
                  <td><input type="text" list="archi-properties" [value]="x.r.whenProperty ?? ''" placeholder="(aucun : règle simple)" [attr.aria-label]="'Propriété discriminante'"
                    (change)="draft.patch('elements', x.i, { whenProperty: $any($event.target).value.trim() || null, whenValue: $any($event.target).value.trim() ? x.r.whenValue : null })" /></td>
                  <td><input type="text" [value]="x.r.whenValue ?? ''" [disabled]="!x.r.whenProperty" [attr.aria-label]="'Valeur discriminante'" (change)="draft.patch('elements', x.i, { whenValue: $any($event.target).value })" /></td>
                } @else {
                  <td><select [attr.aria-label]="'Type source'" (change)="draft.patch('relations', x.i, { sourceTypeKey: $any($event.target).value || null })">
                    <option value="" [selected]="!x.r.sourceTypeKey">Tous les types</option>
                    @for (o of store.types(); track o.key) { <option [value]="o.key" [selected]="o.key === x.r.sourceTypeKey">{{ o.label }}</option> }</select></td>
                  <td><select [attr.aria-label]="'Type cible'" (change)="draft.patch('relations', x.i, { targetTypeKey: $any($event.target).value || null })">
                    <option value="" [selected]="!x.r.targetTypeKey">Tous les types</option>
                    @for (o of store.types(); track o.key) { <option [value]="o.key" [selected]="o.key === x.r.targetTypeKey">{{ o.label }}</option> }</select></td>
                }
                <td class="num"><input type="radio" [name]="kind() + '-' + g.type.key" [checked]="x.r.principal" (change)="draft.setPrincipal(kind(), x.i)" [attr.aria-label]="'Règle principale de ' + g.type.label" /></td>
                <td class="num"><span [class.muted]="!used(x.r)" title="Éléments de ce type qui portent ce concept d'origine (matrice enregistrée)">{{ used(x.r) ?? '–' }}</span></td>
                <td class="actions"><button type="button" class="ghost" (click)="draft.remove(kind(), x.i)" [attr.aria-label]="'Retirer cette règle de ' + g.type.label">Retirer</button></td>
              </tr>
              @for (e of draft.errors().get(kind() + ':' + x.i) ?? []; track e) { <tr class="msg"><td colspan="6"><span class="chip bad">Erreur</span> {{ e }}</td></tr> }
              @for (e of draft.warnings().get(kind() + ':' + x.i) ?? []; track e) { <tr class="msg"><td colspan="6"><span class="chip warn">Attention</span> {{ e }}</td></tr> }
            } @empty {
              <tr class="msg"><td colspan="6">
                @if (g.type.key === unmapped()) {
                  <span class="chip info">Type de réception</span> Reçoit les concepts ArchiMate sans règle : ce n'est pas une anomalie. À l'export, ses {{ kind() === 'elements' ? 'objets' : 'relations' }} reprennent leur concept d'origine.
                } @else {
                  Sans équivalent ArchiMate : exporté en <i>{{ kind() === 'elements' ? t.fallbackElementType : t.fallbackRelationType }}</i> avec la propriété <i>{{ t.typeMarkerProperty }}</i>.
                }
              </td></tr>
            }
          } @empty { <tr><td colspan="6" class="muted">Aucun type ne correspond.</td></tr> }
        </table>
      </div>
    </div>
  `,
})
export class RulesTable {
  protected readonly store = inject(MetaStore);
  protected readonly draft = inject(TranscodingDraft);
  kind = input.required<RuleKind>();
  /** Effectifs par règle, issus de /metamodel/transcoding/checks (matrice enregistrée). */
  usage = input<any[]>([]);
  protected readonly search = signal('');
  protected readonly showAll = signal(false);

  protected concepts = computed<string[]>(() => this.store.catalog().archimate[this.kind() === 'elements' ? 'elements' : 'relationships']);
  protected unmapped = computed(() => this.draft.t()[this.kind() === 'elements' ? 'unmappedTypeKey' : 'unmappedRelationTypeKey']);
  protected archiNames = computed(() => this.store.m().propertyDefinitions.map((p: any) => p.archiName || p.key));

  protected count = (key: string) => this.store.usage()[this.kind() === 'elements' ? 'objectTypes' : 'relationshipTypes'][key] ?? 0;

  protected groups = computed(() => {
    const kind = this.kind();
    const list: any[] = kind === 'elements' ? this.store.types() : this.store.relTypes();
    const rules: any[] = this.draft.t()[kind];
    const known = new Set(list.map((t) => t.key));
    const unknown = [...new Set(rules.map((r) => r.typeKey).filter((k) => !known.has(k)))].map((key) => ({ key, label: `${key} (type inconnu)`, roles: [] as string[] }));
    const q = plain(this.search().trim());
    return [...list, ...unknown]
      .map((type) => ({ type, rules: rules.map((r, i) => ({ r, i })).filter((x) => x.r.typeKey === type.key) }))
      // Un type sans règle reste visible : c'est ici qu'on lui en donne une. Seul un type archivé, sans règle ni objet, se replie.
      .filter((g) => (q ? plain(g.type.label).includes(q) : this.showAll() || !g.type.archived || g.rules.length > 0 || this.count(g.type.key) > 0));
  });

  protected used(rule: any): number | null {
    const kind = this.kind() === 'elements' ? 'element' : 'relation';
    return this.usage().find((u) => u.kind === kind && u.typeKey === rule.typeKey && u.archimateType === rule.archimateType)?.count ?? null;
  }
}
