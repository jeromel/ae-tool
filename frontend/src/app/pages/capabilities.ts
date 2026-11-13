import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { MatrixView } from '../matrix-view';
import { Question, QuestionBar } from '../questions';

interface LinkedApplication { id: string; name: string; via: string; }
interface Capability {
  id: string; name: string; children: Capability[]; applications: LinkedApplication[];
  /** Applications de la capacité et de toutes ses sous-capacités, sans doublon. */
  covering: LinkedApplication[];
  parent: Capability | null;
}

const DUPLICATE_FROM = 3;

/**
 * Carte des capacités, générée depuis les relations de composition : elle ne dépend d'aucun dessin.
 * Les questions changent la coloration ; cliquer une capacité ouvre le tiroir de ses applications.
 */
@Component({
  imports: [RouterLink, NgTemplateOutlet, QuestionBar, MatrixView],
  template: `
    <h1>Capacités</h1>
    <p class="lead">Ce que l'entreprise sait faire, et les applications qui le rendent possible. La carte se lit de la capacité vers ses sous-capacités.</p>
    <app-questions [questions]="questions" [selected]="selected()" (pick)="pick($event)" />

    <div class="tools">
      <div class="segmented" role="group" aria-label="Mode d'affichage">
        <button type="button" [attr.aria-pressed]="mode() === 'carte'" (click)="mode.set('carte')">Carte</button>
        <button type="button" [attr.aria-pressed]="mode() === 'matrice'" (click)="mode.set('matrice')">Matrice</button>
      </div>
      @if (mode() === 'carte') {
        <span class="legend">
          @for (l of legend(); track l.label) { <span><i [class]="'tone ' + l.tone"></i>{{ l.label }}</span> }
        </span>
      }
      <span class="muted right">{{ total() }} capacités, {{ coveredApps() }} applications rattachées</span>
    </div>

    @if (mode() === 'matrice') {
      <div class="card flush"><app-matrix-view rows="Capability" cols="ApplicationComponent" above="330px" /></div>
    } @else {
      <div class="layout">
        <div class="map">
          <ng-template #card let-c>
            <button type="button" class="cap" [class]="'cap tone ' + tone(c)" [class.on]="c.id === picked()?.id" (click)="picked.set(c)"
              [title]="c.name + ' : ' + c.covering.length + ' application(s)'">
              <span>{{ c.name }}</span><b>{{ c.covering.length }}</b>
            </button>
          </ng-template>
          @for (root of domains(); track root.id) {
            <section class="zone">
              <header>
                <button type="button" class="title" [class.on]="root.id === picked()?.id" (click)="picked.set(root)">{{ root.name }}</button>
                <b [title]="root.covering.length + ' application(s) dans ce domaine'">{{ root.covering.length }}</b>
              </header>
              @for (c of root.children; track c.id) {
                @if (c.children.length) {
                  <div class="group">
                    <ng-container [ngTemplateOutlet]="card" [ngTemplateOutletContext]="{ $implicit: c }" />
                    <div class="leaves">
                      @for (leaf of c.children; track leaf.id) { <ng-container [ngTemplateOutlet]="card" [ngTemplateOutletContext]="{ $implicit: leaf }" /> }
                    </div>
                  </div>
                } @else {
                  <ng-container [ngTemplateOutlet]="card" [ngTemplateOutletContext]="{ $implicit: c }" />
                }
              }
            </section>
          }
          @if (loose().length) {
            <section class="zone loose">
              <header><span class="title">Capacités non rattachées à un domaine</span></header>
              @for (c of loose(); track c.id) { <ng-container [ngTemplateOutlet]="card" [ngTemplateOutletContext]="{ $implicit: c }" /> }
            </section>
          }
        </div>

        @if (picked(); as p) {
          <aside class="drawer" aria-label="Détail de la capacité">
            <div class="head">
              <h2>{{ p.name }}</h2>
              <button type="button" class="icon" aria-label="Fermer" title="Fermer" (click)="picked.set(null)">×</button>
            </div>
            @if (path(p); as trail) { @if (trail.length) { <p class="muted small">{{ trail.join(' › ') }}</p> } }

            @if (p.children.length) {
              <h3>Sous-capacités ({{ p.children.length }})</h3>
              @for (c of p.children; track c.id) {
                <div class="line"><a href="" (click)="picked.set(c); $event.preventDefault()">{{ c.name }}</a> <span class="muted small">{{ c.covering.length }} appl.</span></div>
              }
            }

            <h3>Applications ({{ p.covering.length }})</h3>
            @for (a of p.covering; track a.id) {
              <div class="line">
                <a [routerLink]="['/objets', a.id]">{{ a.name }}</a>
                <span class="chip" [class.warn]="a.via === 'vue'" [title]="a.via === 'vue' ? 'Déduit d\\'une imbrication dans une vue : pas encore une relation du référentiel' : ''">{{ a.via }}</span>
                <a class="small" routerLink="/impact" [queryParams]="{ objet: a.id }">Impact</a>
              </div>
            } @empty {
              <p class="muted">Aucune application n'est rattachée à cette capacité.</p>
            }
            @if (deduced(p) > 0) {
              <p class="note small">{{ deduced(p) }} rattachement(s) viennent d'une imbrication dans une vue Archi. Ils ne sont pas encore des relations du référentiel.</p>
            }
            <div class="foot">
              <a class="btn" [routerLink]="['/objets', p.id]">Contexte</a>
              <a class="btn ghost" routerLink="/impact" [queryParams]="{ objet: p.id }">Analyser l'impact</a>
            </div>
          </aside>
        }
      </div>
    }
  `,
  styles: `
    .legend { display: flex; gap: 14px; flex-wrap: wrap; font-size: 12.5px; color: var(--muted); }
    .legend span { display: inline-flex; align-items: center; gap: 6px; }
    .legend i { width: 14px; height: 14px; border-radius: 3px; border: 1px solid #0002; }
    .layout { display: flex; gap: 16px; align-items: flex-start; }
    .map { flex: 1; min-width: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(290px, 1fr)); gap: 12px; align-items: start; }
    .zone { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 10px; display: flex; flex-direction: column; gap: 6px; }
    .zone.loose { border-style: dashed; }
    .zone header { display: flex; align-items: baseline; gap: 8px; padding: 2px 2px 6px; border-bottom: 2px solid var(--ink); margin-bottom: 2px; }
    .zone header b { font-family: var(--display); font-size: 16px; }
    .title { flex: 1; text-align: left; white-space: normal; background: none; border: 0; padding: 0; color: var(--ink); font-family: var(--display); font-size: 16px; font-weight: 700; line-height: 1.2; }
    button.title:hover, button.title.on { background: none; text-decoration: underline; }
    .group { border: 1px solid var(--line); border-radius: 8px; padding: 5px; display: flex; flex-direction: column; gap: 5px; }
    .group > .cap { font-weight: 650; }
    .leaves { display: flex; flex-direction: column; gap: 4px; padding-left: 12px; }
    .cap { display: flex; align-items: center; gap: 8px; width: 100%; text-align: left; white-space: normal; padding: 6px 8px; border-radius: 6px;
      border: 1px solid #0000001f; color: var(--ink); font-weight: 400; line-height: 1.25; }
    .cap span { flex: 1; }
    .cap b { font-variant-numeric: tabular-nums; min-width: 1.5em; text-align: right; }
    .cap:hover { filter: brightness(.96); }
    .cap.on { outline: 3px solid var(--signal); outline-offset: 1px; }
    .tone.c0 { background: #f6d9d3; } .tone.c1 { background: #e3eef1; } .tone.c2 { background: #b9dbe5; } .tone.c3 { background: #7fbdd0; }
    .tone.alert { background: #f1b3aa; } .tone.warn { background: #f4c98a; } .tone.quiet { background: #f1f4f4; color: var(--muted); }
    .cap.tone.c0:hover, .cap.tone.c1:hover, .cap.tone.c2:hover, .cap.tone.c3:hover, .cap.tone.alert:hover, .cap.tone.warn:hover, .cap.tone.quiet:hover { color: var(--ink); }
    .drawer { width: 360px; flex: none; position: sticky; top: 16px; max-height: calc(100vh - 32px); overflow: auto; background: var(--card);
      border: 1px solid var(--line); border-radius: 10px; padding: 14px; }
    .drawer .head { display: flex; gap: 8px; align-items: flex-start; }
    .drawer h2 { flex: 1; margin: 0; }
    .drawer h3 { margin: 16px 0 6px; font-size: 12.5px; color: var(--muted); font-weight: 600; }
    .drawer .line { display: flex; gap: 8px; align-items: baseline; padding: 4px 0; border-bottom: 1px solid var(--line); }
    .drawer .line > a:first-child { flex: 1; overflow-wrap: anywhere; }
    .drawer .foot { display: flex; gap: 8px; margin-top: 14px; flex-wrap: wrap; }
    .drawer .note { margin: 12px 0 0; }
    button.icon { border: 0; background: transparent; color: var(--muted); font-size: 18px; line-height: 1; padding: 4px 8px; }
    button.icon:hover { background: var(--accent-soft); color: var(--ink); }
    @media (max-width: 900px) { .layout { flex-direction: column; } .drawer { width: 100%; position: static; } }
  `,
})
export class CapabilitiesPage {
  private api = inject(Api);
  private router = inject(Router);
  question = input<string>();

  questions: Question[] = [
    { key: 'couverture', label: 'Quelles capacités sont les mieux outillées ?' },
    { key: 'non-couvertes', label: 'Lesquelles n\'ont aucune application ?' },
    { key: 'doublons', label: 'Où y a-t-il des doublons possibles ?', hint: `Capacité sans sous-capacité portant au moins ${DUPLICATE_FROM} applications.` },
    {
      key: 'fragilite', label: 'Lesquelles reposent sur des applications fragiles ?',
      missing: 'La fragilité se déduit de la notation d\'urbanisation, encore incomplète pour la plupart des applications. Complétez les notations dans le portefeuille.',
    },
  ];

  roots = signal<Capability[]>([]);
  selected = signal('couverture');
  mode = signal<'carte' | 'matrice'>('carte');
  picked = signal<Capability | null>(null);

  domains = computed(() => this.roots().filter((r) => r.children.length > 0));
  loose = computed(() => this.roots().filter((r) => r.children.length === 0));
  total = computed(() => count(this.roots()));
  coveredApps = computed(() => new Set(this.roots().flatMap((r) => r.covering.map((a) => a.id))).size);

  legend = computed(() => {
    switch (this.selected()) {
      case 'non-couvertes': return [{ tone: 'alert', label: 'Aucune application' }, { tone: 'quiet', label: 'Au moins une' }];
      case 'doublons': return [{ tone: 'warn', label: `${DUPLICATE_FROM} applications ou plus pour la même capacité` }, { tone: 'quiet', label: 'Moins' }];
      default: return [{ tone: 'c0', label: 'Aucune application' }, { tone: 'c1', label: '1 ou 2' }, { tone: 'c2', label: '3 à 5' }, { tone: 'c3', label: '6 ou plus' }];
    }
  });

  constructor() {
    this.api.get<Capability[]>('/spaces/capabilities').then((roots) => {
      for (const r of roots) prepare(r, null);
      this.roots.set(roots);
    });
    effect(() => this.selected.set(this.questions.find((q) => q.key === this.question())?.key ?? 'couverture'));
  }

  pick(key: string): void {
    this.router.navigate([], { queryParams: { question: key }, replaceUrl: true });
  }

  tone(c: Capability): string {
    const n = c.covering.length;
    switch (this.selected()) {
      case 'non-couvertes': return n === 0 ? 'alert' : 'quiet';
      case 'doublons': return c.children.length === 0 && n >= DUPLICATE_FROM ? 'warn' : 'quiet';
      default: return n === 0 ? 'c0' : n <= 2 ? 'c1' : n <= 5 ? 'c2' : 'c3';
    }
  }

  path(c: Capability): string[] {
    const trail: string[] = [];
    for (let p = c.parent; p; p = p.parent) trail.unshift(p.name);
    return trail;
  }

  deduced(c: Capability): number {
    return c.covering.filter((a) => a.via === 'vue').length;
  }
}

/** Rattache chaque capacité à son parent et calcule les applications qu'elle couvre avec ses sous-capacités. */
function prepare(c: Capability, parent: Capability | null): void {
  c.parent = parent;
  const byId = new Map(c.applications.map((a) => [a.id, a]));
  for (const child of c.children) {
    prepare(child, c);
    for (const a of child.covering) if (!byId.has(a.id)) byId.set(a.id, a);
  }
  c.covering = [...byId.values()].sort((a, b) => a.name.localeCompare(b.name, 'fr', { numeric: true }));
}

function count(list: Capability[]): number {
  return list.reduce((sum, c) => sum + 1 + count(c.children), 0);
}
