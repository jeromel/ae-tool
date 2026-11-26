import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Session } from '../session';
import { Api } from '../api';
import { readText, writeText } from '../prefs';

interface Entry { label: string; where: string; to: string; question?: string; profiles: string[]; }

const PROFILES = [
  { key: 'direction', label: 'Direction et DSI' },
  { key: 'projet', label: 'Chef de projet' },
  { key: 'architecte', label: 'Architecte' },
  { key: 'exploitation', label: 'Exploitation et cybersécurité' },
  { key: 'metier', label: 'Métier' },
];

/** Les questions auxquelles l'outil répond. Chacune mène à un espace, réglé sur la bonne question. */
const ENTRIES: Entry[] = [
  { label: 'Que maintenir, moderniser, tolérer ou éliminer ?', where: 'Portefeuille', to: '/portefeuille', profiles: ['direction', 'architecte'] },
  { label: 'Quelles capacités n\'ont aucune application ?', where: 'Capacités', to: '/capacites', question: 'non-couvertes', profiles: ['direction', 'metier', 'architecte'] },
  { label: 'Où y a-t-il des doublons applicatifs possibles ?', where: 'Capacités', to: '/capacites', question: 'doublons', profiles: ['direction', 'architecte'] },
  { label: 'Que touche mon projet, et avec quels autres est-il en adhérence ?', where: 'Projets', to: '/projets', profiles: ['projet', 'architecte', 'direction'] },
  { label: 'Si cet élément tombe ou change, qu\'est-ce qui est touché ?', where: 'Analyse d\'impact', to: '/impact', profiles: ['exploitation', 'projet', 'architecte'] },
  { label: 'Quelles applications échangent le plus ?', where: 'Applications', to: '/applications', question: 'echanges', profiles: ['projet', 'architecte'] },
  { label: 'Qu\'est-ce qui porte le plus d\'applications ?', where: 'Infrastructure', to: '/infrastructure', question: 'portant', profiles: ['exploitation'] },
  { label: 'Quels flux n\'ont pas de protocole renseigné ?', where: 'Flux', to: '/flux', question: 'sans-protocole', profiles: ['exploitation', 'architecte'] },
  { label: 'Quelles données sont les plus partagées ?', where: 'Données', to: '/donnees', question: 'partagees', profiles: ['metier', 'architecte'] },
  { label: 'Que manque-t-il dans le référentiel ?', where: 'Qualité du référentiel', to: '/qualite', profiles: ['architecte'] },
];

@Component({
  imports: [RouterLink, DatePipe],
  template: `
    <h1>{{ data()?.modelName ?? 'Référentiel d\\'architecture' }}</h1>
    <p class="lead">Partez d'une question. Chaque question ouvre l'espace qui y répond, déjà réglé.</p>
    @if (data(); as d) {
      @if (d.objects === 0) {
        <div class="note">Le référentiel est vide. Commencez par <a routerLink="/imports">importer un modèle Archi</a>.</div>
      }
      <div class="tools">
        <label>Je suis
          <select (change)="setProfile($any($event.target).value)">
            @for (p of profiles; track p.key) { <option [value]="p.key" [selected]="profile() === p.key">{{ p.label }}</option> }
          </select>
        </label>
        <span class="muted small">Le profil ne change que l'ordre des questions. Tout reste accessible.</span>
      </div>

      <div class="entries">
        @for (e of mine(); track e.label) {
          <a class="entry main" [routerLink]="e.to" [queryParams]="e.question ? { question: e.question } : {}"><b>{{ e.label }}</b><span>{{ e.where }}</span></a>
        }
      </div>
      @if (others().length) {
        <h2 style="margin-top: 20px">Autres questions</h2>
        <div class="entries">
          @for (e of others(); track e.label) {
            <a class="entry" [routerLink]="e.to" [queryParams]="e.question ? { question: e.question } : {}"><b>{{ e.label }}</b><span>{{ e.where }}</span></a>
          }
        </div>
      }

      <h2 style="margin-top: 24px">Le référentiel en chiffres</h2>
      <div class="stats">
        <div class="stat"><b>{{ d.objects }}</b><span>objets</span></div>
        <div class="stat"><b>{{ d.relationships }}</b><span>relations</span></div>
        <a class="stat" routerLink="/vues"><b>{{ d.diagrams }}</b><span>vues Archi</span></a>
        <a class="stat" routerLink="/projets"><b>{{ d.projects }}</b><span>projets</span></a>
        <a class="stat" routerLink="/portefeuille"><b>{{ d.applications }}</b><span>applications notées</span></a>
      </div>
      <div class="row">
        <div class="card">
          <h2>Objets par couche</h2>
          <table>
            @for (l of d.layers; track l.key) {
              @if (l.count > 0) {
                <tr>
                  <td style="width: 210px"><a routerLink="/referentiel" [queryParams]="{ couche: l.key }">{{ l.label }}</a></td>
                  <td class="num" style="width: 60px"><b>{{ l.count }}</b></td>
                  <td class="muted small">
                    @for (t of l.types; track t.key; let last = $last) { {{ t.label }} ({{ t.count }}){{ last ? '' : ', ' }} }
                  </td>
                </tr>
              }
            }
          </table>
        </div>
        <div class="card" style="max-width: 420px">
          <h2>Dernières opérations</h2>
          @for (j of d.journal; track $index) {
            <div class="small" style="margin-bottom: 6px"><span class="muted">{{ j.at | date: 'dd/MM HH:mm' }}, {{ j.user }}</span><br />{{ j.action }}</div>
          } @empty {
            <span class="muted">Aucune opération.</span>
          }
        </div>
      </div>
      <p><a routerLink="/referentiel">Voir tous les objets du référentiel</a></p>
    }
  `,
  styles: `
    .entries { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 10px; }
    .entry { display: flex; flex-direction: column; gap: 6px; padding: 14px 16px; background: var(--card); border: 1px solid var(--line); border-left: 4px solid var(--line);
      border-radius: 8px; text-decoration: none; color: var(--ink); }
    .entry.main { border-left-color: var(--signal); }
    .entry:hover { background: var(--accent-soft); }
    .entry b { font-family: var(--display); font-size: 16.5px; font-weight: 600; line-height: 1.25; }
    .entry span { font-size: 12.5px; color: var(--muted); }
  `,
})
export class HomePage {
  private api = inject(Api);
  private session = inject(Session);
  /** Sans l'infrastructure, une question qui y mène ouvrirait un espace vide : elle disparaît (maquette périmètre, §2). */
  private entries = computed(() => ENTRIES.filter((e) => !(this.session.business() && e.to === '/infrastructure')));
  data = signal<any>(null);
  profiles = PROFILES;
  profile = signal(readText('profil', 'architecte'));
  mine = computed(() => this.entries().filter((e) => e.profiles.includes(this.profile())));
  others = computed(() => this.entries().filter((e) => !e.profiles.includes(this.profile())));

  constructor() {
    this.api.get('/summary').then((d) => this.data.set(d));
  }

  setProfile(key: string): void {
    this.profile.set(key);
    writeText('profil', key);
  }
}
