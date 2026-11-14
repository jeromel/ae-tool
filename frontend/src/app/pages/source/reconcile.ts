import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api } from '../../api';
import { ACTION_LABELS, Candidate, Line, Pending, Report, SourceState } from './model';

type View = 'lié' | 'proposé' | 'nouveau' | 'ambigu' | 'ignoré' | 'invalide' | 'disparus' | 'sans-lien';
interface ApplyResult { linked: number; created: number; updated: number; ignored: number; unlinked: number; messages: string[]; changed: boolean; }

const PAGE = 500;
const DIFF_PAGE = 2000;

function plain(text: string | null | undefined): string {
  return (text ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
}

/** Rapprochement : comparer sans rien modifier, décider ligne par ligne, puis appliquer en une fois avec un récapitulatif chiffré. */
@Component({
  selector: 'app-reconcile-tab',
  imports: [DatePipe, RouterLink, NgTemplateOutlet],
  template: `
    @if (state.profile(); as p) {
      <div class="card">
        <h2>Lancer le rapprochement</h2>
        <p class="note small calm"><b>Le calcul ne modifie rien.</b> Il compare la source au référentiel et propose des liens ; vous décidez ensuite, et rien n'est écrit avant « Appliquer ».</p>
        @if (state.mappingDirty()) {
          <div class="note warn-note small">Les correspondances ont des modifications non enregistrées : le rapprochement utilise la version enregistrée.</div>
        }
        <div class="tools">
          @if (p.kind === 'fichier') {
            <label class="btn ghost">Choisir un fichier CSV ou Excel
              <input type="file" accept=".csv,.txt,.xlsx" hidden (change)="pickFile($event)" />
            </label>
            <span class="muted">{{ state.file()?.name ?? 'Aucun fichier choisi' }}</span>
          }
          <button type="button" [disabled]="running() || (p.kind === 'fichier' && !state.file())" (click)="run()">Lancer le rapprochement</button>
          @if (running()) { <span class="muted">Lecture et comparaison en cours…</span> }
        </div>
        @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
        @if (report(); as r) {
          <p class="small muted meta">
            Dernier rapprochement le {{ r.at | date: 'dd/MM/yyyy HH:mm' }}@if (p.lastRunBy) { par {{ p.lastRunBy }} }
            @if (r.sourceName) { · fichier « {{ r.sourceName }} » }
            · {{ r.rowsRead }} ligne(s) lue(s){{ r.sourceTotal !== null ? ' sur ' + r.sourceTotal : '' }}.
          </p>
          @if (r.truncated) { <div class="note warn-note small">Lecture partielle de la source : la liste des objets disparus n'est pas fiable.</div> }
          @for (n of r.notes; track n) { <div class="note small">{{ n }}</div> }
          @if (r.issues.length) {
            <details>
              <summary>{{ r.issues.length }} anomalie(s) de lecture</summary>
              <ul class="small">@for (i of r.issues.slice(0, 50); track $index) { <li>{{ i.message }} @if (i.ref) { <span class="muted">({{ i.ref }})</span> }</li> }</ul>
              @if (r.issues.length > 50) { <p class="small muted">… et {{ r.issues.length - 50 }} autre(s).</p> }
            </details>
          }
        } @else if (!running()) {
          <p class="muted small">Aucun rapprochement n'a encore été lancé pour cette source.</p>
        }
      </div>

      @if (report(); as r) {
        @if (bilan(); as b) {
          <div class="card bilan" role="status">
            <h2>Bilan de l'application</h2>
            @if (b.changed) {
              <p>{{ b.linked }} lié(s), {{ b.created }} créé(s), {{ b.updated }} objet(s) mis à jour, {{ b.ignored }} ignoré(s), {{ b.unlinked }} délié(s) ou rétabli(s).</p>
              @if (b.created) { <p class="small muted">Les objets créés sont à l'état « Proposé » : ils attendent la validation.</p> }
            } @else { <p>Rien n'a changé dans le référentiel.</p> }
            @if (b.messages.length) {
              <p class="small"><b>{{ b.messages.length }} décision(s) refusée(s) :</b></p>
              <ul class="small">@for (m of b.messages; track $index) { <li>{{ m }}</li> }</ul>
            }
            <button type="button" class="ghost" (click)="bilan.set(null)">Fermer le bilan</button>
          </div>
        }

        <div class="stats" role="group" aria-label="Résultat par statut">
          @for (s of views(); track s.key) {
            <button type="button" class="stat" [class.on]="view() === s.key" [class.apart]="s.apart" [attr.aria-pressed]="view() === s.key" (click)="show(s.key)">
              <b>{{ s.count }}</b><span>{{ s.label }}</span>
            </button>
          }
        </div>

        @if (hasRecap()) {
          <div class="card recap">
            <h2>{{ state.pending().length + autoAccepted() }} décision(s) en attente</h2>
            <p class="muted small">Ce qui va changer dans le référentiel à « Appliquer » :</p>
            <ul>
              @for (l of recap(); track l) { <li>{{ l }}</li> }
            </ul>
            <label class="check">
              <input type="checkbox" [checked]="sync()" (change)="sync.set($any($event.target).checked)" />
              <span>Mettre à jour les propriétés des objets déjà liés
                <span class="muted small"> (les colonnes reprises sont recopiées de la source{{ state.master() ? ', y compris le nom' : '' }}{{ diffCount() !== null ? ' ; ' + diffLabel() : '' }})</span></span>
            </label>
            <div class="tools top">
              <button type="button" [disabled]="applying() || nothingToApply()" (click)="apply()">Appliquer</button>
              <button type="button" class="ghost" [disabled]="applying() || (!state.pending().length && !state.acceptAll())" (click)="state.clearDecisions()">Annuler les décisions</button>
              @if (applying()) { <span class="muted">Application en cours…</span> }
            </div>
          </div>
        }

        @switch (view()) {
          @case ('disparus') {
            <div class="card flush">
              <div class="tools"><span class="muted small">Objets du référentiel liés à cette source dont l'identifiant n'est plus dans la lecture. Information seule : rien n'est supprimé.</span></div>
              <div class="scroll tall" style="--above: 560px">
                <table>
                  <tr><th>Objet</th><th>Type</th><th>Identifiant chez la source</th></tr>
                  @for (o of r.missing; track o.id) {
                    <tr><td><a [routerLink]="['/objets', o.id]">{{ o.name }}</a></td><td>{{ state.typeLabel()(o.typeKey) }}</td><td class="muted">{{ o.externalId }}</td></tr>
                  } @empty { <tr><td colspan="3" class="muted">Aucun objet lié n'a disparu de la source.</td></tr> }
                </table>
              </div>
            </div>
          }
          @case ('sans-lien') {
            <div class="card flush">
              <div class="tools"><span class="muted small">Objets du référentiel, des types visés par cette source, qui n'ont aucun lien avec elle. Information seule.
                @if (r.unlinkedCount > r.unlinked.length) { Les {{ r.unlinked.length }} premiers sur {{ r.unlinkedCount }}. }</span></div>
              <div class="scroll tall" style="--above: 560px">
                <table>
                  <tr><th>Objet</th><th>Type</th></tr>
                  @for (o of r.unlinked; track o.id) {
                    <tr><td><a [routerLink]="['/objets', o.id]">{{ o.name }}</a></td><td>{{ state.typeLabel()(o.typeKey) }}</td></tr>
                  } @empty { <tr><td colspan="2" class="muted">Tous les objets visés ont un lien.</td></tr> }
                </table>
              </div>
            </div>
          }
          @default {
            <div class="card flush">
              <div class="tools">
                <input type="search" aria-label="Rechercher par nom" placeholder="Rechercher par nom" [value]="search()" (input)="search.set($any($event.target).value)" />
                @switch (view()) {
                  @case ('proposé') {
                    <button type="button" [class.ghost]="!state.acceptAll()" [attr.aria-pressed]="state.acceptAll()" (click)="state.acceptAll.set(!state.acceptAll())">
                      {{ state.acceptAll() ? 'Toutes les propositions seront acceptées' : 'Accepter toutes les propositions' }}
                    </button>
                  }
                  @case ('nouveau') {
                    <button type="button" [disabled]="!selectedCount()" (click)="createSelected()">Créer la sélection ({{ selectedCount() }})</button>
                  }
                  @case ('lié') {
                    <label class="inline"><input type="checkbox" [checked]="onlyDiff()" (change)="onlyDiff.set($any($event.target).checked)" /> Avec écarts seulement</label>
                  }
                }
                <span class="muted small right">{{ shown().length }} affichée(s) sur {{ total() }}</span>
              </div>
              <div class="scroll tall" style="--above: 520px">
                <table>
                  @switch (view()) {
                    @case ('proposé') {
                      <tr><th>Nom chez la source</th><th>Objet proposé</th><th>Écarts</th><th class="actions-h">Décision</th></tr>
                      @for (l of shown(); track l.externalId) {
                        <tr>
                          <td>{{ l.name }}<div class="muted small">{{ l.externalId }}</div></td>
                          <td><a [routerLink]="['/objets', l.objectId]">{{ l.objectName }}</a></td>
                          <td><ng-container [ngTemplateOutlet]="diffs" [ngTemplateOutletContext]="{ $implicit: l }" /></td>
                          <td class="actions">
                            @if (state.decisionOf(l.externalId); as d) { <ng-container [ngTemplateOutlet]="decided" [ngTemplateOutletContext]="{ $implicit: d }" /> }
                            @else {
                              @if (state.acceptAll()) { <span class="chip info">Sera lié</span> }
                              <button type="button" class="ghost" (click)="link(l, l.objectId!, l.objectName!)">Lier</button>
                              <button type="button" class="ghost" (click)="ignore(l)">Ignorer</button>
                            }
                          </td>
                        </tr>
                      }
                    }
                    @case ('nouveau') {
                      <tr>
                        <th class="chk"><input type="checkbox" aria-label="Tout sélectionner" [checked]="allSelected()" (change)="toggleAll($any($event.target).checked)" /></th>
                        <th>Nom chez la source</th><th>Nature</th><th>Type proposé</th><th class="actions-h">Décision</th>
                      </tr>
                      @for (l of shown(); track l.externalId) {
                        <tr>
                          <td class="chk"><input type="checkbox" [attr.aria-label]="'Sélectionner ' + l.name" [disabled]="!!state.decisionOf(l.externalId)"
                            [checked]="selected().has(l.externalId)" (change)="toggle(l.externalId, $any($event.target).checked)" /></td>
                          <td>{{ l.name }}<div class="muted small">{{ l.externalId }}</div></td>
                          <td>{{ l.typeValue }}</td>
                          <td>{{ state.typeLabel()(l.typeKey) }}</td>
                          <td class="actions">
                            @if (state.decisionOf(l.externalId); as d) { <ng-container [ngTemplateOutlet]="decided" [ngTemplateOutletContext]="{ $implicit: d }" /> }
                            @else { <button type="button" class="ghost" (click)="ignore(l)">Ignorer</button> }
                          </td>
                        </tr>
                      }
                    }
                    @case ('ambigu') {
                      <tr><th>Nom chez la source</th><th>Candidats</th><th class="actions-h">Décision</th></tr>
                      @for (l of shown(); track l.externalId) {
                        <tr>
                          <td>{{ l.name }}<div class="muted small">{{ l.externalId }}</div>@if (l.message) { <div class="small warn-text">{{ l.message }}</div> }</td>
                          <td>
                            <select [attr.aria-label]="'Objet à lier pour ' + l.name" (change)="choose(l.externalId, $any($event.target).value)">
                              <option value="" [selected]="!chosen()[l.externalId]">Choisir l'objet…</option>
                              @for (c of l.candidates; track c.id) { <option [value]="c.id" [selected]="chosen()[l.externalId] === c.id">{{ c.name }} ({{ state.typeLabel()(c.typeKey) }})</option> }
                            </select>
                          </td>
                          <td class="actions">
                            @if (state.decisionOf(l.externalId); as d) { <ng-container [ngTemplateOutlet]="decided" [ngTemplateOutletContext]="{ $implicit: d }" /> }
                            @else {
                              <button type="button" class="ghost" [disabled]="!chosen()[l.externalId]" (click)="linkChosen(l)">Lier à cet objet</button>
                              <button type="button" class="ghost" (click)="ignore(l)">Ignorer</button>
                            }
                          </td>
                        </tr>
                      }
                    }
                    @case ('lié') {
                      <tr><th>Nom chez la source</th><th>Objet lié</th><th>Écarts avec la source</th><th class="actions-h">Décision</th></tr>
                      @for (l of shown(); track l.externalId) {
                        <tr>
                          <td>{{ l.name }}<div class="muted small">{{ l.externalId }}</div></td>
                          <td><a [routerLink]="['/objets', l.objectId]">{{ l.objectName }}</a>@if (l.message) { <div class="small warn-text">{{ l.message }}</div> }</td>
                          <td><ng-container [ngTemplateOutlet]="diffs" [ngTemplateOutletContext]="{ $implicit: l }" /></td>
                          <td class="actions">
                            @if (state.decisionOf(l.externalId); as d) { <ng-container [ngTemplateOutlet]="decided" [ngTemplateOutletContext]="{ $implicit: d }" /> }
                            @else { <button type="button" class="ghost" (click)="unlink(l)">Délier</button> }
                          </td>
                        </tr>
                      }
                    }
                    @case ('ignoré') {
                      <tr><th>Nom chez la source</th><th>Identifiant</th><th class="actions-h">Décision</th></tr>
                      @for (l of shown(); track l.externalId) {
                        <tr>
                          <td>{{ l.name }}</td><td class="muted">{{ l.externalId }}</td>
                          <td class="actions">
                            @if (state.decisionOf(l.externalId); as d) { <ng-container [ngTemplateOutlet]="decided" [ngTemplateOutletContext]="{ $implicit: d }" /> }
                            @else { <button type="button" class="ghost" (click)="unlink(l)">Rétablir</button> }
                          </td>
                        </tr>
                      }
                    }
                    @case ('invalide') {
                      <tr><th>Nom chez la source</th><th>Nature</th><th>Raison</th><th></th></tr>
                      @for (l of shown(); track l.externalId) {
                        <tr>
                          <td>{{ l.name }}<div class="muted small">{{ l.externalId }}</div></td>
                          <td>{{ l.typeValue }}</td>
                          <td class="warn-text">{{ l.message }}</td>
                          <td class="actions"><a [routerLink]="['/sources', p.key]" [queryParams]="{ onglet: 'correspondances' }">Régler les correspondances</a></td>
                        </tr>
                      }
                    }
                  }
                </table>
                @if (!shown().length) { <p class="empty-note muted">{{ loadingLines() ? 'Chargement…' : 'Aucune ligne à afficher.' }}</p> }
                @if (lines().length < total()) {
                  <div class="more"><button type="button" class="ghost" [disabled]="loadingLines()" (click)="more()">Charger les {{ total() - lines().length > pageSize ? pageSize : total() - lines().length }} suivantes</button>
                    <span class="muted small">La recherche porte sur les lignes chargées.</span></div>
                }
              </div>
            </div>
          }
        }
      }

      <ng-template #diffs let-l>
        @for (d of l.differences; track d.field) {
          <div class="diff small"><span class="muted">{{ d.label }} :</span> {{ d.repositoryValue ?? '(vide)' }} <span class="arrow muted">→</span> <b>{{ d.sourceValue ?? '(vide)' }}</b></div>
        } @empty { <span class="muted small">Aucun écart</span> }
      </ng-template>
      <ng-template #decided let-d>
        <span class="chip warn">{{ label(d) }}</span>
        <button type="button" class="ghost" (click)="state.undecide(d.externalId)">Annuler</button>
      </ng-template>
    }
  `,
  styles: `
    .calm { margin-bottom: 10px; }
    .warn-note { background: #fbe9cc; color: var(--warn); }
    .meta { margin: 8px 0 0; }
    .stats button.stat { background: none; color: inherit; border: 0; border-right: 1px solid var(--line); border-radius: 0; text-align: left; font-weight: 400; min-width: 112px; white-space: normal; }
    .stats button.stat:hover { background: var(--accent-soft); }
    .stats button.stat.on { background: var(--accent-soft); box-shadow: inset 0 -3px 0 var(--signal); }
    .stats button.stat.apart { background: #f6f8f8; }
    .stats button.stat.apart.on { background: var(--accent-soft); }
    .stats button.stat b { color: var(--ink); }
    .recap { border-left: 4px solid var(--signal); }
    .recap ul { margin: 0 0 12px; padding-left: 20px; }
    .check { display: flex; gap: 10px; align-items: flex-start; }
    .check input { margin-top: 3px; }
    .tools.top { margin: 14px 0 0; }
    .bilan { border-left: 4px solid var(--ok); }
    .inline { display: inline-flex; gap: 6px; align-items: center; }
    .warn-text { color: var(--warn); }
    td.actions { white-space: nowrap; }
    td.actions button, td.actions .chip { margin-left: 6px; }
    th.actions-h { text-align: right; }
    th.chk, td.chk { width: 34px; text-align: center; }
    .diff { white-space: normal; overflow-wrap: anywhere; }
    .diff .arrow { margin: 0 4px; }
    .empty-note { padding: 18px 14px; margin: 0; }
    .more { display: flex; gap: 12px; align-items: center; padding: 10px 14px; }
    input[type=search] { min-width: 240px; }
    td select { max-width: 380px; width: 100%; }
  `,
})
export class ReconcileTab {
  readonly state = inject(SourceState);
  private api = inject(Api);
  readonly pageSize = PAGE;

  report = signal<Report | null>(null);
  view = signal<View>('proposé');
  lines = signal<Line[]>([]);
  total = signal(0);
  search = signal('');
  onlyDiff = signal(false);
  selected = signal<Set<string>>(new Set());
  chosen = signal<Record<string, string>>({});
  diffCount = signal<number | null>(null);
  diffAtLeast = signal(false);
  sync = signal(true);
  running = signal(false);
  applying = signal(false);
  loadingLines = signal(false);
  error = signal('');
  bilan = signal<ApplyResult | null>(null);

  views = computed(() => {
    const r = this.report();
    if (!r) return [];
    const c = r.counts;
    return [
      { key: 'lié' as View, label: 'Liés', count: c['lié'] ?? 0, apart: false },
      { key: 'proposé' as View, label: 'Proposés', count: c['proposé'] ?? 0, apart: false },
      { key: 'nouveau' as View, label: 'Nouveaux', count: c['nouveau'] ?? 0, apart: false },
      { key: 'ambigu' as View, label: 'Ambigus', count: c['ambigu'] ?? 0, apart: false },
      { key: 'ignoré' as View, label: 'Ignorés', count: c['ignoré'] ?? 0, apart: false },
      { key: 'invalide' as View, label: 'Invalides', count: c['invalide'] ?? 0, apart: false },
      { key: 'disparus' as View, label: 'Disparus de la source', count: r.missing.length, apart: true },
      { key: 'sans-lien' as View, label: 'Objets du référentiel sans lien', count: r.unlinkedCount, apart: true },
    ];
  });

  shown = computed(() => {
    const q = plain(this.search().trim());
    const onlyDiff = this.view() === 'lié' && this.onlyDiff();
    return this.lines().filter((l) => (!q || plain(l.name).includes(q) || plain(l.objectName).includes(q) || plain(l.externalId).includes(q))
      && (!onlyDiff || l.differences.length > 0));
  });

  selectedCount = computed(() => this.selected().size);
  allSelected = computed(() => {
    const free = this.shown().filter((l) => !this.state.decisionOf(l.externalId));
    return free.length > 0 && free.every((l) => this.selected().has(l.externalId));
  });

  /** Propositions qui seront liées sans décision individuelle : toutes, moins celles qui ont reçu une décision. */
  autoAccepted = computed(() => {
    if (!this.state.acceptAll()) return 0;
    const decided = this.state.pending().filter((d) => d.status === 'proposé').length;
    return Math.max(0, (this.report()?.counts['proposé'] ?? 0) - decided);
  });

  recap = computed(() => {
    const by = (a: Pending['action']) => this.state.pending().filter((d) => d.action === a).length;
    const out: string[] = [];
    const links = by('lier') + this.autoAccepted();
    if (links) out.push(`${links} objet(s) du référentiel seront liés à la source` + (this.autoAccepted() ? ` (dont ${this.autoAccepted()} proposition(s) acceptée(s) en bloc)` : '') + '.');
    if (by('creer')) out.push(`${by('creer')} objet(s) seront créés, à l'état « Proposé » dans le circuit de validation.`);
    if (by('ignorer')) out.push(`${by('ignorer')} ligne(s) de la source seront ignorées.`);
    if (by('delier')) out.push(`${by('delier')} lien(s) seront supprimés ou ligne(s) ignorée(s) rétablie(s).`);
    if (this.sync() && (this.diffCount() ?? 0) > 0) out.push(`${this.diffLabel()} : leurs propriétés seront alignées sur la source.`);
    if (!out.length) out.push('Aucun changement dans le référentiel.');
    return out;
  });

  hasRecap = computed(() => this.state.pending().length > 0 || this.state.acceptAll() || (this.diffCount() ?? 0) > 0);
  nothingToApply = computed(() => !this.state.pending().length && !this.state.acceptAll() && !(this.sync() && (this.diffCount() ?? 0) > 0));

  constructor() {
    if (this.state.profile()?.hasReport) void this.loadReport(true);
  }

  diffLabel(): string {
    const n = this.diffCount() ?? 0;
    return `${this.diffAtLeast() ? 'au moins ' : ''}${n} objet(s) déjà lié(s) présentent un écart avec la source`;
  }

  label(d: Pending): string {
    return `${d.action === 'delier' && d.status === 'ignoré' ? 'Rétablir' : ACTION_LABELS[d.action]} (en attente)`;
  }

  pickFile(event: Event): void {
    this.state.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
    this.error.set('');
  }

  async run(): Promise<void> {
    const p = this.state.profile()!;
    this.running.set(true);
    this.error.set('');
    this.bilan.set(null);
    try {
      const file = this.state.file();
      const path = `/sources/${p.key}/run?take=1` + (file ? `&name=${encodeURIComponent(file.name)}` : '');
      const r = p.kind === 'fichier' ? await this.api.upload<Report>(path, file!) : await this.api.post<Report>(path, {});
      // Un nouveau calcul repart de zéro : les décisions précédentes visaient l'ancien rapport.
      this.state.clearDecisions();
      this.selected.set(new Set());
      this.setReport(r, true);
      await this.state.refreshProfile();
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.running.set(false);
    }
  }

  async loadReport(initial: boolean): Promise<void> {
    try {
      this.setReport(await this.api.get<Report>(`/sources/${this.state.profile()!.key}/report?take=1`), initial);
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  private setReport(r: Report, chooseView: boolean): void {
    this.report.set(r);
    // Après un calcul, ou quand la liste ouverte vient de se vider, on passe à ce qui demande encore une décision.
    const current = this.view();
    const empty = this.isLineView() && (r.counts[current] ?? 0) === 0;
    if (chooseView || empty) this.view.set(this.firstView(r));
    void this.loadLines();
    void this.loadDiffs();
  }

  /** On ouvre d'abord ce qui demande une décision. */
  private firstView(r: Report): View {
    const order: View[] = ['proposé', 'ambigu', 'nouveau', 'invalide', 'lié', 'ignoré'];
    return order.find((s) => (r.counts[s] ?? 0) > 0) ?? 'proposé';
  }

  show(view: View): void {
    this.view.set(view);
    this.search.set('');
    this.selected.set(new Set());
    void this.loadLines();
  }

  private isLineView(): boolean {
    return this.view() !== 'disparus' && this.view() !== 'sans-lien';
  }

  async loadLines(append = false): Promise<void> {
    if (!this.isLineView()) return;
    const view = this.view();
    const skip = append ? this.lines().length : 0;
    if (!append) this.lines.set([]);
    this.loadingLines.set(true);
    try {
      const r = await this.api.get<Report>(`/sources/${this.state.profile()!.key}/report?status=${encodeURIComponent(view)}&skip=${skip}&take=${PAGE}`);
      if (view !== this.view()) return;
      this.total.set(r.linesTotal);
      this.lines.update((all) => (append ? [...all, ...r.lines] : r.lines));
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.loadingLines.set(false);
    }
  }

  more(): void {
    void this.loadLines(true);
  }

  /** Nombre d'objets liés qui diffèrent de la source, pour le récapitulatif (le nom ne compte que si la source fait foi). */
  private async loadDiffs(): Promise<void> {
    const r = this.report();
    if (!r || !(r.counts['lié'] > 0)) { this.diffCount.set(0); return; }
    try {
      const l = await this.api.get<Report>(`/sources/${this.state.profile()!.key}/report?status=${encodeURIComponent('lié')}&skip=0&take=${DIFF_PAGE}`);
      const master = this.state.profile()!.sourceIsMaster;
      this.diffCount.set(l.lines.filter((x) => x.differences.some((d) => master || d.field !== 'name')).length);
      this.diffAtLeast.set(l.linesTotal > l.lines.length);
    } catch {
      this.diffCount.set(null);
    }
  }

  // ---------- Décisions ----------

  link(l: Line, objectId: string, objectName: string): void {
    this.state.decide({ externalId: l.externalId, name: l.name, action: 'lier', objectId, objectName, status: l.status });
  }

  ignore(l: Line): void {
    this.state.decide({ externalId: l.externalId, name: l.name, action: 'ignorer', status: l.status });
  }

  unlink(l: Line): void {
    this.state.decide({ externalId: l.externalId, name: l.name, action: 'delier', status: l.status });
  }

  choose(externalId: string, objectId: string): void {
    this.chosen.update((c) => ({ ...c, [externalId]: objectId }));
  }

  linkChosen(l: Line): void {
    const c: Candidate | undefined = l.candidates.find((x) => x.id === this.chosen()[l.externalId]);
    if (c) this.link(l, c.id, c.name);
  }

  toggle(id: string, on: boolean): void {
    this.selected.update((s) => { const n = new Set(s); if (on) n.add(id); else n.delete(id); return n; });
  }

  toggleAll(on: boolean): void {
    this.selected.set(on ? new Set(this.shown().filter((l) => !this.state.decisionOf(l.externalId)).map((l) => l.externalId)) : new Set());
  }

  createSelected(): void {
    for (const l of this.lines().filter((x) => this.selected().has(x.externalId)))
      this.state.decide({ externalId: l.externalId, name: l.name, action: 'creer', status: l.status });
    this.selected.set(new Set());
  }

  async apply(): Promise<void> {
    this.applying.set(true);
    this.error.set('');
    try {
      const body = {
        decisions: this.state.pending().map((d) => ({ externalId: d.externalId, action: d.action, objectId: d.objectId })),
        acceptProposals: this.state.acceptAll(), syncLinked: this.sync(),
      };
      const r = await this.api.post<{ result: ApplyResult; report: Report }>(`/sources/${this.state.profile()!.key}/apply`, body);
      this.bilan.set(r.result);
      this.state.clearDecisions();
      this.selected.set(new Set());
      this.setReport(r.report, false);
      await this.state.refreshProfile();
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.applying.set(false);
    }
  }
}
