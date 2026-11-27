import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../api';
import { Session } from '../session';
import { isIsolated, isOrphan } from '../space-filters';

const CODE_LABELS: Record<string, string> = {
  'type-inconnu': 'Type inconnu',
  'type-archive': 'Type archivé',
  'propriete-obligatoire-absente': 'Propriété obligatoire absente',
  'valeur-hors-liste': 'Valeur hors liste',
  'valeur-nombre-invalide': 'Nombre invalide',
  'valeur-date-invalide': 'Date invalide',
  'valeur-booleen-invalide': 'Oui / non invalide',
  'relation-non-autorisee': 'Relation non autorisée',
};

/** Qualité du référentiel : ce qui manque pour que les espaces répondent, avec un lien vers chaque liste à traiter. */
@Component({
  imports: [RouterLink],
  template: `
    <h1>Qualité du référentiel</h1>
    <p class="lead">Une vue générée ne vaut que par les relations et les propriétés du référentiel. Cette page liste ce qui manque, par ordre d'effet sur les espaces.</p>
    <div class="card">
      <h2>À traiter</h2>
      <table>
        <tr><th class="num">Nombre</th><th>Constat</th><th>Ce que cela empêche</th><th></th></tr>
        @for (t of todo(); track t.label) {
          <tr>
            <td class="num"><b [style.color]="t.count ? 'var(--bad)' : 'var(--ok)'">{{ t.count }}</b> <span class="muted small">/ {{ t.total }}</span></td>
            <td>{{ t.label }}</td><td class="muted">{{ t.effect }}</td>
            <td><a [routerLink]="t.to" [queryParams]="{ question: t.question }">Voir la liste</a></td>
          </tr>
        }
      </table>
    </div>
    @if (catalogue(); as cat) {
      @if (cat.hasCatalogue) {
        <div class="card" id="catalogue">
          <h2>Cohérence du catalogue des données et des flux</h2>
          <p class="muted small">Ce que le catalogue dit, comparé à lui-même et au modèle Archi. Rien n'est corrigé automatiquement, sauf le reclassement des partages, sur demande.</p>
          @if (cat.reliability.length) {
            <div class="stats" style="margin-bottom: 12px">
              @for (r of cat.reliability; track r.kind) {
                <div class="stat" [title]="r.verified + ' constaté(s) sur ' + r.total"><b>{{ pct(r.verified, r.total) }}</b><span>{{ r.kind }} constatés ({{ r.verified }} / {{ r.total }})</span></div>
              }
            </div>
          }
          @for (f of cat.findings; track f.code) {
            <details class="finding" style="border-top: 1px solid var(--line); padding: 8px 0">
              <summary><span class="chip" [class.bad]="f.severity === 'erreur'" [class.warn]="f.severity === 'avertissement'" [class.info]="f.severity === 'information'">{{ f.items.length }}</span> {{ f.title }}</summary>
              <p class="muted small" style="margin: 6px 0">{{ f.hint }}</p>
              <!-- Droit serveur : AccessRules.cs, écriture sous /api/catalogue → administrateur (refus par défaut). -->
              @if (f.code === 'partage-sans-sous-type' && session.can('administrateur')) {
                <p><button type="button" class="btn" [disabled]="busy()" (click)="reclass()">Reclasser en partages de fichiers</button></p>
              }
              <table>
                @for (i of f.items; track $index) {
                  <tr><td><a [routerLink]="['/parcours', i.id]">{{ i.label }}</a></td><td class="small muted">{{ i.detail }}</td></tr>
                }
              </table>
            </details>
          } @empty { <p class="muted">Aucune incohérence relevée.</p> }
          @if (message()) { <div class="note small" style="margin-top: 8px">{{ message() }}</div> }
        </div>
      }
    }
    <div class="card">
      <h2>Conformité au métamodèle</h2>
      @if (conformity(); as c) {
        @if (c.total === 0) {
          <p class="muted">Toutes les données respectent le métamodèle : types connus, propriétés obligatoires renseignées, valeurs et relations conformes.</p>
        } @else {
          <p class="muted">{{ c.total }} écart(s) entre les données et le métamodèle. Ils sont signalés sans bloquer la saisie ; cliquez sur une nature pour ne voir qu'elle.</p>
          <div class="questions">
            @for (s of c.summary; track s.code) {
              <button type="button" class="question" [attr.aria-pressed]="code() === s.code" (click)="code.set(code() === s.code ? '' : s.code)">
                {{ codeLabel(s.code) }} : <b>{{ s.count }}</b>
              </button>
            }
          </div>
          <div class="scroll" style="max-height: 360px">
            <table>
              <tr><th>Nature</th><th>Constat</th><th></th></tr>
              @for (i of issues(); track $index) {
                <tr>
                  <td><span class="chip" [class.bad]="i.severity === 'erreur'" [class.warn]="i.severity !== 'erreur'">{{ codeLabel(i.code) }}</span></td>
                  <td>{{ i.message }}</td>
                  <td class="actions">@if (i.kind === 'objet') { <a [routerLink]="['/objets', i.id]">Ouvrir l'objet</a> } @else { <span class="muted small">Relation</span> }</td>
                </tr>
              }
            </table>
          </div>
          @if (c.total > c.issues.length) { <p class="muted small">Les {{ c.issues.length }} premiers constats sur {{ c.total }} sont listés ; les effectifs ci-dessus portent sur tous.</p> }
        }
      } @else {
        <p class="muted">Chargement…</p>
      }
    </div>
    <div class="card flush">
      <div class="tools"><h2 style="margin: 0">Complétude par type d'objet</h2></div>
      <div class="scroll tall" style="--above: 520px">
        <table>
          <tr><th>Type</th><th class="num">Objets</th><th class="num">Documentés</th><th class="num">Avec propriétés</th><th class="num">Sans relation</th><th class="num">Dans aucune vue</th></tr>
          @for (q of quality(); track q.typeKey) {
            <tr>
              <td>{{ q.label }}</td><td class="num">{{ q.count }}</td>
              <td class="num">{{ pct(q.documented, q.count) }}</td><td class="num">{{ pct(q.withProperties, q.count) }}</td>
              <td class="num">{{ q.orphans }}</td><td class="num">{{ q.notInDiagram }}</td>
            </tr>
          }
        </table>
      </div>
    </div>
  `,
})
export class QualityPage {
  private api = inject(Api);
  session = inject(Session);
  catalogue = signal<any>(null);
  busy = signal(false);
  message = signal('');
  quality = signal<any[]>([]);
  conformity = signal<any>(null);
  /** Nature de constat retenue pour filtrer la liste ; vide = toutes. */
  code = signal('');
  issues = computed(() => (this.conformity()?.issues ?? []).filter((i: any) => !this.code() || i.code === this.code()));
  private applications = signal<any[]>([]);
  private flows = signal<any[]>([]);
  private data = signal<any[]>([]);

  todo = computed(() => {
    const apps = this.applications();
    // Protocole et chiffrement : seuls les flux où le serveur dit qu'un protocole est attendu (règle « flux de données »).
    const flows = this.flows().filter((f) => f.protocolExpected);
    const data = this.data();
    return [
      { count: apps.filter(isIsolated).length, total: apps.length, label: 'Applications sans aucune relation',
        effect: 'Elles n\'apparaissent dans aucune analyse d\'impact ni aucun contexte.', to: '/applications', question: 'isolees' },
      { count: apps.filter((a) => a.capabilities === 0).length, total: apps.length, label: 'Applications rattachées à aucune capacité',
        effect: 'La carte des capacités ne les compte pas, et les doublons ne ressortent pas.', to: '/applications', question: 'sans-capacite' },
      { count: flows.filter((f) => !f.protocol).length, total: flows.length, label: 'Flux sans protocole renseigné',
        effect: 'Pas de pastille de protocole sur les vues, pas de lecture cybersécurité des échanges.', to: '/flux', question: 'sans-protocole' },
      { count: flows.filter((f) => !f.encryption).length, total: flows.length, label: 'Flux sans chiffrement renseigné',
        effect: 'Impossible de distinguer un flux en clair d\'un flux non documenté.', to: '/flux', question: 'sans-chiffrement' },
      { count: data.filter(isOrphan).length, total: data.length, label: 'Données utilisées par aucune application',
        effect: 'On ne sait pas où la donnée est produite ni lue.', to: '/donnees', question: 'orphelines' },
    ];
  });

  constructor() {
    this.api.get('/quality/conformity').then((c) => this.conformity.set(c));
    this.api.get('/catalogue/qualite').then((c) => this.catalogue.set(c));
    this.api.get('/quality').then((q) => this.quality.set(q));
    this.api.get('/spaces/applications').then((r) => this.applications.set(r));
    this.api.get('/spaces/flows').then((r) => this.flows.set(r));
    this.api.get('/spaces/data').then((r) => this.data.set(r));
  }

  async reclass(): Promise<void> {
    this.busy.set(true);
    try {
      const r = await this.api.post<{ count: number; names: string[] }>('/catalogue/partages/reclasser', {});
      this.message.set(r.count ? `${r.count} nœud(s) reclassé(s) en partages de fichiers : ${r.names.join(', ')}. Le prochain export vers Archi porte la propriété asset.subtype.` : 'Aucun nœud à reclasser.');
      this.catalogue.set(await this.api.get('/catalogue/qualite'));
    } catch (e) {
      this.message.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  codeLabel(code: string): string {
    return CODE_LABELS[code] ?? code;
  }

  pct(part: number, total: number): string {
    return total ? Math.round((100 * part) / total) + ' %' : '–';
  }
}
