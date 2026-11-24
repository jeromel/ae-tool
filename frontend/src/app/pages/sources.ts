import { Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { SourcePage } from './source';

interface Template { name: string; description: string; profile: any; }
interface Row { profile: any; counts: Record<string, number> | null; }

const KEY_PATTERN = /^[a-z0-9][a-z0-9-]{0,39}$/;
const NATURE: Record<string, string> = { fichier: 'Fichier CSV ou Excel', api: 'API REST' };

/** Sources externes : la liste des profils quand aucune clé n'est dans l'adresse, la page d'une source sinon. */
@Component({
  imports: [RouterLink, DatePipe, SourcePage],
  template: `
    @if (key(); as k) {
      <app-source [key]="k" [onglet]="onglet()" />
    } @else {
      <h1>Sources externes</h1>
      <p class="lead">
        Une source est un outil qui maîtrise une partie de ce que le référentiel décrit : la CMDB pour les équipements, l'outil de projets pour les projets.
        L'outil la lit, la compare au référentiel et propose des liens ; il n'écrit jamais dans la source, et rien ne change dans le référentiel sans votre décision.
      </p>
      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }

      <div class="tools">
        <button type="button" (click)="creating.set(!creating())" [attr.aria-expanded]="creating()">Nouvelle source</button>
      </div>

      @if (creating()) {
        <form class="card form create" (submit)="create(); $event.preventDefault()">
          <h2>Nouvelle source</h2>
          <fieldset>
            <legend>Modèle de départ</legend>
            @for (t of templates(); track t.name) {
              <label class="choice">
                <input type="radio" name="template" [checked]="template() === t.name" (change)="template.set(t.name)" />
                <span><b>{{ t.name }}</b><br /><span class="muted small">{{ t.description }}</span></span>
              </label>
            }
          </fieldset>
          <div class="pair">
            <label>Libellé
              <input type="text" [value]="label()" maxlength="120" (input)="setLabel($any($event.target).value)" placeholder="Export CMDB" />
            </label>
            <label>Clé <span class="muted small">(courte, sans espace : minuscules, chiffres, tirets)</span>
              <input type="text" [value]="newKey()" maxlength="40" (input)="newKey.set($any($event.target).value); keyTouched = true" placeholder="export-cmdb" />
            </label>
          </div>
          @if (isApi()) {
            <div class="pair">
              <label>Adresse de la ressource
                <input type="text" [value]="conn().url" placeholder="https://cmdb.exemple.fr/api/elements" autocomplete="off" (input)="setConn('url', $event)" />
              </label>
            </div>
            <p class="muted small hint">Pagination, champs, authentification et secret se règlent ensuite, dans l'onglet Connexion de la source.</p>
          }
          @if (newKey() && !keyOk()) { <div class="error">La clé ne peut contenir que des minuscules, des chiffres et des tirets, et doit commencer par une lettre ou un chiffre.</div> }
          <div class="tools">
            <button type="submit" [disabled]="!template() || !label().trim() || !keyOk() || busy() || (isApi() && !conn().url.trim())">Créer et ouvrir</button>
            <button type="button" class="ghost" (click)="creating.set(false)">Annuler</button>
          </div>
        </form>
      }

      @if (loaded() && !rows().length) {
        <div class="card empty">
          <h2>Aucune source pour l'instant</h2>
          <p>
            Une source relie vos objets techniques aux éléments de configuration de la CMDB, ou vos projets à l'outil de gestion de projet,
            sans ressaisie. Pour commencer, un export CSV de la CMDB suffit : créez une source de nature « Fichier »,
            chargez l'export, indiquez quelles colonnes portent l'identifiant et le nom, puis lancez un premier rapprochement.
            Ce premier calcul ne modifie rien.
          </p>
          <button type="button" (click)="creating.set(true)">Créer une première source</button>
        </div>
      } @else if (rows().length) {
        <div class="card flush">
          <div class="scroll">
            <table>
              <tr>
                <th>Source</th><th>Nature</th><th>Dernier rapprochement</th>
                <th class="num">Liés</th><th class="num">Proposés</th><th class="num">Nouveaux</th><th class="num">Ambigus</th>
              </tr>
              @for (r of rows(); track r.profile.key) {
                <tr>
                  <td><a [routerLink]="['/sources', r.profile.key]"><b>{{ r.profile.label }}</b></a><div class="muted small">{{ r.profile.key }}</div></td>
                  <td><span class="chip info">{{ nature(r.profile.kind) }}</span></td>
                  <td>
                    @if (r.profile.lastRunAt) { {{ r.profile.lastRunAt | date: 'dd/MM/yyyy HH:mm' }}<div class="muted small">par {{ r.profile.lastRunBy }}</div> }
                    @else { <span class="muted">Jamais lancé</span> }
                  </td>
                  @for (s of statuses; track s) {
                    <td class="num">@if (r.counts) { <span [class.muted]="!r.counts[s]">{{ r.counts[s] ?? 0 }}</span> } @else { <span class="muted">–</span> }</td>
                  }
                </tr>
              }
            </table>
          </div>
        </div>
      }
    }
  `,
  styles: `
    .create { max-width: 760px; }
    fieldset { border: 0; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 8px; }
    legend { font-weight: 600; margin-bottom: 6px; padding: 0; }
    .choice { flex-direction: row !important; gap: 10px !important; align-items: flex-start; font-weight: 400 !important; padding: 8px 10px; border: 1px solid var(--line); border-radius: 6px; cursor: pointer; }
    .choice:has(input:checked) { border-color: var(--accent); background: var(--accent-soft); }
    .choice input { margin-top: 3px; }
    .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .pair3 { display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 12px; }
    .hint { margin: -4px 0 0; }
    .empty p { max-width: 70ch; }
  `,
})
export class SourcesPage {
  private api = inject(Api);
  private router = inject(Router);
  key = input<string>();
  onglet = input<string>();

  readonly statuses = ['lié', 'proposé', 'nouveau', 'ambigu'];
  rows = signal<Row[]>([]);
  loaded = signal(false);
  error = signal('');
  templates = signal<Template[]>([]);
  creating = signal(false);
  busy = signal(false);
  template = signal('');
  label = signal('');
  newKey = signal('');
  keyTouched = false;
  conn = signal({ url: '' });
  keyOk = computed(() => KEY_PATTERN.test(this.newKey()));

  constructor() {
    void this.load();
  }

  isApi(): boolean {
    return this.templates().find((t) => t.name === this.template())?.profile.kind === 'api';
  }

  setConn(field: 'url', event: Event): void {
    this.conn.update((c) => ({ ...c, [field]: (event.target as HTMLInputElement).value }));
  }

  nature(kind: string): string {
    return NATURE[kind] ?? kind;
  }

  private async load(): Promise<void> {
    try {
      const [profiles, templates] = await Promise.all([this.api.get<any[]>('/sources'), this.api.get<Template[]>('/sources/templates')]);
      this.templates.set(templates);
      this.template.set(templates.find((t) => t.profile.kind === 'fichier')?.name ?? templates[0]?.name ?? '');
      // Les compteurs viennent du dernier rapport de chaque source (une ligne seulement : on ne veut que les totaux).
      this.rows.set(await Promise.all(profiles.map(async (profile) => ({ profile, counts: await this.counts(profile) }))));
    } catch (e) {
      this.error.set((e as Error).message);
    }
    this.loaded.set(true);
  }

  private async counts(profile: any): Promise<Record<string, number> | null> {
    if (!profile.hasReport) return null;
    try { return (await this.api.get(`/sources/${profile.key}/report?take=1`)).counts; } catch { return null; }
  }

  /** Propose une clé tirée du libellé tant que l'utilisateur n'a pas saisi la sienne. */
  setLabel(value: string): void {
    this.label.set(value);
    if (!this.keyTouched) this.newKey.set(slug(value));
  }

  async create(): Promise<void> {
    const t = this.templates().find((x) => x.name === this.template());
    if (!t) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const p = t.profile;
      await this.api.put(`/sources/${this.newKey()}`, {
        label: this.label().trim(), kind: p.kind, connection: p.kind === 'api' ? { ...p.connection, ...this.conn() } : {}, mapping: p.mapping, sourceIsMaster: false,
      });
      await this.router.navigate(['/sources', this.newKey()]);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}

function slug(text: string): string {
  return text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40);
}
