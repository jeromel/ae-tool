import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Api, Brief } from '../api';
import { ObjectSearch } from '../object-search';
import { ProjectionState } from '../projection';
import { Session } from '../session';

interface Envisioned { id: string; name: string; typeKey: string; typeLabel: string; }
interface Change { id: string; op: string; target: string; label: string; issue: string | null; note: string | null; }
interface Step { id: string; name: string; order: number; date: string | null; status: string; changes: Change[]; issues: any[]; }
interface Scenario {
  id: string; projectId: string; name: string; description: string | null; status: string;
  start: { scenarioId: string; stepId: string; label: string } | null; envisioned: Envisioned[]; steps: Step[];
  createdBy: string; updatedBy: string | null; updatedAt: string | null;
}
type Op = 'ajout-objet' | 'ajout-lien' | 'retrait' | 'modification' | 'remplacement';
/** Un élément choisi dans un formulaire : objet du référentiel, objet envisagé, ou relation d'un objet. */
interface Pick { id: string; name: string; }

/**
 * Trajectoires d'un projet : scénarios, étapes et changements (EV-03, incrément 1). Chaque étape décrit ce qui change par
 * rapport à la précédente ; « Voir le SI à cette étape » ouvre tous les espaces sur l'état projeté.
 */
@Component({
  selector: 'app-project-trajectories',
  imports: [ObjectSearch],
  styles: `
    .scenario { border: 1px solid var(--line, #d9e2e4); border-radius: 10px; padding: 12px 16px; margin-top: 12px; }
    .scenario header { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    .scenario h3 { margin: 0; font-size: 17px; }
    .steps { display: grid; gap: 10px; margin-top: 10px; }
    .step { background: #f6f9f9; border-radius: 8px; padding: 10px 12px; }
    .step header h4 { margin: 0; font-size: 14.5px; }
    .step ul { margin: 8px 0 0; padding-left: 18px; }
    .step li { margin: 3px 0; }
    .issue { color: #8c2a1c; font-size: 12.5px; }
    .form { display: grid; gap: 8px; margin-top: 10px; padding: 10px; background: #fff; border: 1px dashed #b9cdd2; border-radius: 8px; }
    .form .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    .form .row > * { flex: 1; min-width: 180px; }
    .picked { font-weight: 600; }
    .x { border: none; background: none; color: #8c2a1c; cursor: pointer; padding: 0 4px; }
  `,
  template: `
    <div class="card">
      <div class="tools">
        <h2 style="margin: 0">Trajectoires ({{ scenarios().length }})</h2>
        @if (canEdit()) { <button type="button" (click)="creating.set(!creating())">Nouveau scénario</button> }
        @if (error()) { <span class="error" style="margin: 0">{{ error() }}</span> }
      </div>
      <p class="muted small">Un scénario est une proposition de solution, non validée. Chaque étape décrit ce qui change par rapport à la précédente ;
        « Voir le SI à cette étape » ouvre tous les écrans sur l'état projeté, en lecture seule.@if (!canEdit()) { Les brouillons ne sont pas visibles avec votre rôle. }</p>

      @if (creating()) {
        <div class="form">
          <div class="row">
            <input placeholder="Nom du scénario (ex. Facturation en SaaS)" [value]="newName()" (input)="newName.set($any($event.target).value)" aria-label="Nom du scénario" />
            <select (change)="newStart.set($any($event.target).value)" aria-label="Point de départ">
              <option value="">Part de l'existant</option>
              @for (s of scenarios(); track s.id) {
                @for (st of s.steps; track st.id) { <option [value]="s.id + ':' + st.id">Variante de « {{ s.name }} », après {{ st.name }}</option> }
              }
            </select>
          </div>
          <div class="row"><button type="button" [disabled]="!newName().trim()" (click)="createScenario()">Créer</button><button type="button" class="ghost" (click)="creating.set(false)">Annuler</button></div>
        </div>
      }

      @for (s of scenarios(); track s.id) {
        <div class="scenario">
          <header>
            <h3>{{ s.name }}</h3>
            <span class="chip" [class.warn]="s.status === 'Brouillon'">{{ s.status }}</span>
            @if (s.start) { <span class="muted small">variante de {{ s.start.label }}</span> } @else { <span class="muted small">part de l'existant</span> }
            @if (canEdit() && editable(s)) {
              <select (change)="setStatus(s, $any($event.target).value)" aria-label="Statut du scénario">
                <option value="Brouillon" [selected]="s.status === 'Brouillon'">Brouillon</option>
                <option value="Proposé" [selected]="s.status === 'Proposé'">Proposé</option>
              </select>
              <button type="button" class="ghost" (click)="rename(s)">Renommer</button>
              <button type="button" class="ghost" (click)="copy(s)">Copier</button>
              <button type="button" class="ghost" (click)="remove(s)">Supprimer</button>
            }
          </header>
          @if (s.envisioned.length) {
            <p class="small" style="margin: 8px 0 0">Objets envisagés :
              @for (e of s.envisioned; track e.id) { <span class="chip" style="margin-right: 4px">{{ e.name }} · {{ e.typeLabel }}</span> }
            </p>
          }
          <div class="steps">
            @for (st of s.steps; track st.id) {
              <div class="step">
                <header class="tools" style="margin: 0">
                  <h4>{{ st.order }}. {{ st.name }}@if (st.date) { <span class="muted small"> · {{ st.date }}</span> }</h4>
                  <button type="button" (click)="view(s, st)">Voir le SI à cette étape</button>
                  @if (canEdit() && editable(s)) {
                    <button type="button" class="ghost" (click)="openForm(s, st)">Ajouter un changement</button>
                    <button type="button" class="ghost" (click)="renameStep(s, st)">Renommer</button>
                    @if (s.steps.length > 1) { <button type="button" class="ghost" (click)="removeStep(s, st)">Supprimer l'étape</button> }
                  }
                </header>
                <ul>
                  @for (c of st.changes; track c.id) {
                    <li>{{ c.label }}@if (c.note) { <span class="muted small"> — {{ c.note }}</span> }
                      @if (canEdit() && editable(s)) { <button type="button" class="x" title="Retirer ce changement" (click)="removeChange(s, st, c)">×</button> }
                      @if (c.issue) { <div class="issue">{{ c.issue }}</div> }
                    </li>
                  } @empty { <li class="muted small">Aucun changement : l'étape montre le même SI que la précédente.</li> }
                </ul>

                @if (form()?.stepId === st.id) {
                  <div class="form">
                    <div class="row">
                      <select (change)="op.set($any($event.target).value)" aria-label="Nature du changement">
                        <option value="ajout-objet" [selected]="op() === 'ajout-objet'">Ajouter un objet (nouveau, envisagé)</option>
                        <option value="ajout-lien" [selected]="op() === 'ajout-lien'">Ajouter un lien (flux, relation)</option>
                        <option value="retrait" [selected]="op() === 'retrait'">Retirer un objet ou un lien</option>
                        <option value="modification" [selected]="op() === 'modification'">Modifier un objet ou un lien</option>
                        <option value="remplacement" [selected]="op() === 'remplacement'">Remplacer un objet par un autre</option>
                      </select>
                    </div>
                    @switch (op()) {
                      @case ('ajout-objet') {
                        <div class="row">
                          <input placeholder="Nom de l'objet envisagé" [value]="objName()" (input)="objName.set($any($event.target).value)" aria-label="Nom de l'objet envisagé" />
                          <select (change)="objType.set($any($event.target).value)" aria-label="Type de l'objet envisagé">
                            <option value="">Type…</option>
                            @for (t of objectTypes(); track t.key) { <option [value]="t.key" [selected]="t.key === objType()">{{ t.label }}</option> }
                          </select>
                        </div>
                        @if (envisionedFree(s).length) {
                          <div class="row"><select (change)="existingEnvisioned.set($any($event.target).value)" aria-label="Ou un objet envisagé existant">
                            <option value="">… ou un objet envisagé déjà décrit</option>
                            @for (e of envisionedFree(s); track e.id) { <option [value]="e.id">{{ e.name }} · {{ e.typeLabel }}</option> }
                          </select></div>
                        }
                      }
                      @case ('ajout-lien') {
                        <div class="row">
                          <select (change)="relType.set($any($event.target).value)" aria-label="Type de lien">
                            <option value="">Type de lien…</option>
                            @for (t of relationTypes(); track t.key) { <option [value]="t.key" [selected]="t.key === relType()">{{ t.label }}</option> }
                          </select>
                          <input placeholder="Libellé (ex. transmet les factures)" [value]="relName()" (input)="relName.set($any($event.target).value)" aria-label="Libellé du lien" />
                        </div>
                        <div class="row">
                          <div><span class="small muted">De</span> @if (a(); as x) { <span class="picked">{{ x.name }}</span> <button type="button" class="x" (click)="a.set(null)">×</button> } @else {
                            <app-object-search placeholder="Objet de départ" [showAll]="false" (picked)="a.set(brief($event))" />
                            @if (s.envisioned.length) { <select (change)="pickEnvisioned(s, $any($event.target).value, 'a')"><option value="">ou un objet envisagé</option>@for (e of s.envisioned; track e.id) { <option [value]="e.id">{{ e.name }}</option> }</select> } }
                          </div>
                          <div><span class="small muted">Vers</span> @if (b(); as x) { <span class="picked">{{ x.name }}</span> <button type="button" class="x" (click)="b.set(null)">×</button> } @else {
                            <app-object-search placeholder="Objet d'arrivée" [showAll]="false" (picked)="b.set(brief($event))" />
                            @if (s.envisioned.length) { <select (change)="pickEnvisioned(s, $any($event.target).value, 'b')"><option value="">ou un objet envisagé</option>@for (e of s.envisioned; track e.id) { <option [value]="e.id">{{ e.name }}</option> }</select> } }
                          </div>
                        </div>
                      }
                      @default {
                        <div class="row">
                          <div>
                            <span class="small muted">{{ op() === 'remplacement' ? 'Objet remplacé' : 'Objet' }}</span>
                            @if (a(); as x) { <span class="picked">{{ x.name }}</span> <button type="button" class="x" (click)="a.set(null); relations.set([])">×</button> } @else {
                              <app-object-search placeholder="Rechercher dans le référentiel" [showAll]="false" (picked)="pickTarget($event)" />
                              @if (s.envisioned.length && op() === 'modification') { <select (change)="pickEnvisioned(s, $any($event.target).value, 'a')"><option value="">ou un objet envisagé</option>@for (e of s.envisioned; track e.id) { <option [value]="e.id">{{ e.name }}</option> }</select> }
                            }
                          </div>
                          @if (op() !== 'remplacement' && relations().length) {
                            <select (change)="link.set($any($event.target).value || null)" aria-label="Ou l'un de ses liens">
                              <option value="">L'objet lui-même</option>
                              @for (r of relations(); track r.id) { <option [value]="r.id">Son lien : {{ r.name }}</option> }
                            </select>
                          }
                        </div>
                        @if (op() === 'modification') {
                          <div class="row">
                            <input placeholder="Nouveau nom (facultatif)" [value]="newObjName()" (input)="newObjName.set($any($event.target).value)" aria-label="Nouveau nom" />
                            <input placeholder="Propriété (ex. lifecycle.state)" list="trajectoire-props" [value]="propKey()" (input)="propKey.set($any($event.target).value)" aria-label="Propriété" />
                            <input placeholder="Nouvelle valeur (vide = effacée)" [value]="propValue()" (input)="propValue.set($any($event.target).value)" aria-label="Nouvelle valeur" />
                            <datalist id="trajectoire-props">@for (p of propertyKeys(); track p) { <option [value]="p"></option> }</datalist>
                          </div>
                        }
                        @if (op() === 'remplacement') {
                          <div class="row">
                            <div><span class="small muted">Par</span> @if (b(); as x) { <span class="picked">{{ x.name }}</span> <button type="button" class="x" (click)="b.set(null)">×</button> } @else {
                              <app-object-search placeholder="Un objet du référentiel" [showAll]="false" (picked)="b.set(brief($event))" />
                              @if (s.envisioned.length) { <select (change)="pickEnvisioned(s, $any($event.target).value, 'b')"><option value="">ou un objet envisagé</option>@for (e of s.envisioned; track e.id) { <option [value]="e.id">{{ e.name }}</option> }</select> } }
                            </div>
                          </div>
                          @if (!b()) {
                            <div class="row">
                              <input placeholder="… ou un nouvel objet envisagé : nom" [value]="objName()" (input)="objName.set($any($event.target).value)" aria-label="Nom du remplaçant envisagé" />
                              <select (change)="objType.set($any($event.target).value)" aria-label="Type du remplaçant envisagé">
                                <option value="">Type…</option>
                                @for (t of objectTypes(); track t.key) { <option [value]="t.key" [selected]="t.key === objType()">{{ t.label }}</option> }
                              </select>
                            </div>
                          }
                          <p class="muted small" style="margin: 0">Les liens de l'objet remplacé passent au remplaçant ; l'objet remplacé est retiré.</p>
                        }
                      }
                    }
                    <div class="row">
                      <input placeholder="Note (facultative)" [value]="note()" (input)="note.set($any($event.target).value)" aria-label="Note" />
                    </div>
                    <div class="row"><button type="button" (click)="addChange(s, st)">Ajouter</button><button type="button" class="ghost" (click)="form.set(null)">Fermer</button></div>
                  </div>
                }
              </div>
            }
          </div>
          @if (canEdit() && editable(s)) { <button type="button" class="ghost" style="margin-top: 10px" (click)="addStep(s)">Ajouter une étape</button> }
        </div>
      } @empty { <p class="muted">Aucun scénario pour ce projet.</p> }
    </div>
  `,
})
export class ProjectTrajectories {
  private api = inject(Api);
  private session = inject(Session);
  private projection = inject(ProjectionState);
  projectId = input.required<string>();
  projectName = input('');

  scenarios = signal<Scenario[]>([]);
  error = signal('');
  creating = signal(false);
  newName = signal('');
  newStart = signal('');
  objectTypes = signal<{ key: string; label: string }[]>([]);
  relationTypes = signal<{ key: string; label: string }[]>([]);
  propertyKeys = signal<string[]>([]);

  // Formulaire de changement (un seul ouvert à la fois).
  form = signal<{ scenarioId: string; stepId: string } | null>(null);
  op = signal<Op>('ajout-objet');
  objName = signal('');
  objType = signal('');
  existingEnvisioned = signal('');
  relType = signal('');
  relName = signal('');
  a = signal<Pick | null>(null);
  b = signal<Pick | null>(null);
  link = signal<string | null>(null);
  relations = signal<Pick[]>([]);
  newObjName = signal('');
  propKey = signal('');
  propValue = signal('');
  note = signal('');

  // Droit serveur : AccessRules.cs, écriture sous /api/projects et /api/scenarios → contributeur.
  canEdit = computed(() => this.session.can('contributeur'));

  constructor() {
    effect(() => {
      this.load(this.projectId());
    });
    this.api.get('/metamodel').then((m) => {
      const byLabel = (x: { label: string }, y: { label: string }) => x.label.localeCompare(y.label);
      this.objectTypes.set(m.objectTypes.map((t: any) => ({ key: t.key, label: t.label })).sort(byLabel));
      this.relationTypes.set(m.relationshipTypes.map((t: any) => ({ key: t.key, label: t.label })).sort(byLabel));
      this.propertyKeys.set((m.propertyDefinitions ?? []).map((p: any) => p.key).sort());
    }).catch(() => undefined);
  }

  private async load(id: string): Promise<void> {
    try {
      this.scenarios.set(await this.api.get(`/projects/${id}/scenarios`));
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  editable(s: Scenario): boolean {
    return s.status === 'Brouillon' || s.status === 'Proposé';
  }

  envisionedFree(s: Scenario): Envisioned[] {
    return s.envisioned;
  }

  brief(o: Brief): Pick {
    return { id: o.id, name: o.name };
  }

  pickEnvisioned(s: Scenario, id: string, which: 'a' | 'b'): void {
    const e = s.envisioned.find((x) => x.id === id);
    if (!e) return;
    (which === 'a' ? this.a : this.b).set({ id: e.id, name: e.name });
  }

  /** Objet choisi pour un retrait ou une modification : ses liens sont proposés aussi. */
  async pickTarget(o: Brief): Promise<void> {
    this.a.set(this.brief(o));
    this.link.set(null);
    try {
      const d = await this.api.get(`/objects/${o.id}`);
      const rel = (r: any, dir: string) => ({ id: r.id, name: `${r.typeLabel}${r.name ? ' « ' + r.name + ' »' : ''} ${dir} ${r.other?.name ?? '?'}` });
      this.relations.set([...d.outgoing.map((r: any) => rel(r, '→')), ...d.incoming.map((r: any) => rel(r, '←'))]);
    } catch {
      this.relations.set([]);
    }
  }

  openForm(s: Scenario, st: Step): void {
    this.form.set({ scenarioId: s.id, stepId: st.id });
    this.op.set('ajout-objet');
    for (const sig of [this.objName, this.objType, this.existingEnvisioned, this.relType, this.relName, this.newObjName, this.propKey, this.propValue, this.note]) sig.set('');
    this.a.set(null); this.b.set(null); this.link.set(null); this.relations.set([]);
  }

  private replace(updated: Scenario): void {
    this.scenarios.update((all) => all.map((x) => (x.id === updated.id ? updated : x)));
  }

  private async run(action: () => Promise<unknown>): Promise<boolean> {
    this.error.set('');
    try {
      await action();
      return true;
    } catch (e) {
      this.error.set((e as Error).message);
      return false;
    }
  }

  async createScenario(): Promise<void> {
    const [startScenarioId, startStepId] = this.newStart() ? this.newStart().split(':') : [null, null];
    const ok = await this.run(async () => {
      const s = await this.api.post(`/projects/${this.projectId()}/scenarios`, { name: this.newName().trim(), startScenarioId, startStepId });
      this.scenarios.update((all) => [...all, s]);
    });
    if (ok) { this.creating.set(false); this.newName.set(''); this.newStart.set(''); }
  }

  setStatus(s: Scenario, status: string): void {
    this.run(async () => this.replace(await this.api.put(`/scenarios/${s.id}`, { status })));
  }

  rename(s: Scenario): void {
    const name = prompt('Nouveau nom du scénario', s.name);
    if (name?.trim()) this.run(async () => this.replace(await this.api.put(`/scenarios/${s.id}`, { name })));
  }

  copy(s: Scenario): void {
    this.run(async () => {
      const c = await this.api.post(`/scenarios/${s.id}/copie`, {});
      this.scenarios.update((all) => [...all, c]);
    });
  }

  remove(s: Scenario): void {
    if (!confirm(`Supprimer le scénario « ${s.name} » ?`)) return;
    this.run(async () => {
      await this.api.delete(`/scenarios/${s.id}`);
      this.scenarios.update((all) => all.filter((x) => x.id !== s.id));
    });
  }

  addStep(s: Scenario): void {
    this.run(async () => this.replace(await this.api.post(`/scenarios/${s.id}/etapes`, {})));
  }

  renameStep(s: Scenario, st: Step): void {
    const name = prompt("Nom de l'étape", st.name);
    if (name?.trim()) this.run(async () => this.replace(await this.api.put(`/scenarios/${s.id}/etapes/${st.id}`, { name })));
  }

  removeStep(s: Scenario, st: Step): void {
    if (!confirm(`Supprimer l'étape « ${st.name} » et ses changements ?`)) return;
    this.run(async () => this.replace(await this.api.delete(`/scenarios/${s.id}/etapes/${st.id}`)));
  }

  removeChange(s: Scenario, st: Step, c: Change): void {
    this.run(async () => this.replace(await this.api.delete(`/scenarios/${s.id}/etapes/${st.id}/changements/${c.id}`)));
  }

  /** Ouvre tous les espaces sur le SI à cette étape. */
  view(s: Scenario, st: Step): void {
    this.projection.enter(s.id, st.id, `${this.projectName()} › ${s.name} › ${st.name}`, '/applications');
  }

  async addChange(s: Scenario, st: Step): Promise<void> {
    const body = this.changeBody();
    if (!body) return;
    const ok = await this.run(async () => this.replace(await this.api.post(`/scenarios/${s.id}/etapes/${st.id}/changements`, body)));
    if (ok) this.openForm(s, st);
  }

  private changeBody(): any | null {
    const note = this.note().trim() || null;
    const newObject = this.objName().trim() ? { name: this.objName().trim(), typeKey: this.objType() || null } : null;
    switch (this.op()) {
      case 'ajout-objet':
        if (!newObject && !this.existingEnvisioned()) { this.error.set("Nommez l'objet envisagé et choisissez son type."); return null; }
        return { op: 'ajout', target: 'objet', elementId: this.existingEnvisioned() || null, newObject, note };
      case 'ajout-lien':
        if (!this.a() || !this.b() || !this.relType()) { this.error.set('Choisissez le type de lien et ses deux extrémités.'); return null; }
        return { op: 'ajout', target: 'relation', relation: { typeKey: this.relType(), sourceId: this.a()!.id, targetId: this.b()!.id, name: this.relName().trim() || null, properties: [] }, note };
      case 'retrait':
        if (!this.a()) { this.error.set('Choisissez ce qui est retiré.'); return null; }
        return { op: 'retrait', elementId: this.link() ?? this.a()!.id, note };
      case 'modification': {
        if (!this.a()) { this.error.set('Choisissez ce qui est modifié.'); return null; }
        const set = this.propKey().trim() ? [{ key: this.propKey().trim(), value: this.propValue() }] : [];
        return { op: 'modification', elementId: this.link() ?? this.a()!.id, newName: this.newObjName().trim() || null, set, note };
      }
      case 'remplacement':
        if (!this.a() || (!this.b() && !newObject)) { this.error.set('Choisissez l\'objet remplacé et son remplaçant.'); return null; }
        return { op: 'remplacement', elementId: this.a()!.id, replacementId: this.b()?.id ?? null, newObject: this.b() ? null : newObject, note };
    }
  }
}
