import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../api';
import { SourceState } from './model';

interface Settings { pivotField: string; siteField: string | null; entityField: string | null; modelField: string | null; filterField: string | null; filterValue: string | null; }
interface Count { total: number; bySite: Record<string, number>; byEntity: Record<string, number>; byModel: Record<string, number>; }
interface Census {
  sourceKey: string; at: string; by: string | null; pivotField: string; read: number; byProfile: Record<string, Count>; unclassified: Count;
  unknownCodes: Record<string, Count>; toAttach: { id: string; name: string; code: string | null; site: string | null; entity: string | null; model: string | null }[];
  suggestions: { entity: string; count: number; profileCode: string; profileName: string; basis: number }[]; emptyProfiles: string[];
}

const EMPTY: Settings = { pivotField: '', siteField: null, entityField: null, modelField: null, filterField: null, filterValue: null };

/**
 * Comptage des postes de travail par profil de poste. Un champ de la CMDB (le champ pivot) porte le code du profil (PP-nnn) :
 * l'outil range chaque poste dans son profil, compte par site et par entité, et liste les postes à rattacher.
 */
@Component({
  selector: 'app-workstations-tab',
  imports: [RouterLink],
  template: `
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    <div class="card">
      <h2>Réglages du comptage</h2>
      <p class="muted small">Le champ pivot porte, pour chaque poste, le code du profil de poste attribué par l'outil (PP-001…) ; le nom du profil y est aussi reconnu.
        Seuls des effectifs sont gardés : aucun nom d'utilisateur n'entre dans le référentiel.
        @if (state.columns().length === 0) { Lisez un aperçu dans l'onglet Correspondances pour choisir parmi les colonnes reçues. }</p>
      <datalist id="ws-columns">@for (c of state.columns(); track c) { <option [value]="c"></option> }</datalist>
      <div class="grid2">
        <label class="form">Champ pivot (code du profil)<input type="text" list="ws-columns" [value]="draft().pivotField" (input)="set('pivotField', $event)" /></label>
        <label class="form">Champ du site<input type="text" list="ws-columns" [value]="draft().siteField ?? ''" (input)="set('siteField', $event)" /></label>
        <label class="form">Champ de l'entité (DRH/Service paie…)<input type="text" list="ws-columns" [value]="draft().entityField ?? ''" (input)="set('entityField', $event)" /></label>
        <label class="form">Champ du modèle<input type="text" list="ws-columns" [value]="draft().modelField ?? ''" (input)="set('modelField', $event)" /></label>
        <label class="form">Ne garder que les lignes dont le champ…<input type="text" list="ws-columns" [value]="draft().filterField ?? ''" (input)="set('filterField', $event)" placeholder="Catégorie" /></label>
        <label class="form">…vaut<input type="text" [value]="draft().filterValue ?? ''" (input)="set('filterValue', $event)" placeholder="Poste de travail" /></label>
      </div>
      <p style="margin-top: 10px">
        <button type="button" class="btn" [disabled]="busy() || !draft().pivotField.trim()" (click)="save()">Enregistrer les réglages</button>
        @if (saved()) { <span class="muted small" style="margin-left: 8px">Réglages enregistrés.</span> }
      </p>
    </div>

    <div class="card">
      <h2>Compter les postes</h2>
      @if (state.profile()?.kind === 'fichier') {
        <label class="btn">Choisir l'export de la CMDB<input type="file" accept=".csv,.xlsx,.txt" hidden (change)="run($event)" /></label>
      } @else {
        <button type="button" class="btn" [disabled]="busy() || !configured()" (click)="run()">Lire la CMDB et compter</button>
      }
      @if (busy()) { <span class="muted small" style="margin-left: 10px">Lecture en cours…</span> }
      @if (!configured()) { <p class="muted small">Enregistrez d'abord le champ pivot.</p> }
    </div>

    @if (census(); as c) {
      <div class="stats">
        <div class="stat"><b>{{ c.read }}</b><span>postes lus</span></div>
        <div class="stat"><b>{{ attached(c) }}</b><span>rattachés à un profil</span></div>
        <div class="stat" [class.alert]="c.unclassified.total > 0"><b>{{ c.unclassified.total }}</b><span>sans profil (champ vide)</span></div>
        <div class="stat" [class.alert]="unknown(c) > 0"><b>{{ unknown(c) }}</b><span>avec un code inconnu</span></div>
      </div>
      <p class="muted small">Comptage du {{ date(c.at) }}@if (c.by) { par {{ c.by }}}, champ pivot « {{ c.pivotField }} ». Les effectifs s'affichent dans le parcours des données.</p>
      <div class="card">
        <h2>Postes par profil</h2>
        <table>
          <tr><th>Code</th><th>Profil de poste</th><th class="num">Postes</th><th>Par site</th><th>Par entité</th></tr>
          @for (p of profileRows(); track p.code) {
            <tr>
              <td>{{ p.code }}</td>
              <td>@if (p.id) { <a [routerLink]="['/parcours', p.id]">{{ p.name }}</a> } @else { {{ p.name }} }</td>
              <td class="num"><b>{{ p.count?.total ?? 0 }}</b></td>
              <td class="small">{{ split(p.count?.bySite) }}</td>
              <td class="small">{{ split(p.count?.byEntity) }}</td>
            </tr>
          }
        </table>
        @if (c.emptyProfiles.length) { <p class="warn small">Profils sans aucun poste dans la CMDB : {{ c.emptyProfiles.join(', ') }}.</p> }
      </div>
      @if (c.suggestions.length || c.unclassified.total || unknown(c)) {
        <div class="card">
          <h2>Postes à rattacher</h2>
          @if (c.suggestions.length) {
            <p class="small">Propositions, d'après les postes déjà rattachés de la même entité (ou de l'entité parente) :</p>
            <table>
              <tr><th>Entité</th><th class="num">Postes sans profil</th><th>Profil proposé</th><th class="muted">Base</th></tr>
              @for (s of c.suggestions; track s.entity) {
                <tr><td>{{ s.entity }}</td><td class="num">{{ s.count }}</td><td><b>{{ s.profileCode }}</b> {{ s.profileName }}</td><td class="small muted">{{ s.basis }} poste(s) de l'entité ont ce profil</td></tr>
              }
            </table>
          }
          @if (unknown(c)) {
            <p class="small" style="margin-top: 10px">Codes présents dans la CMDB mais portés par aucun profil du référentiel :
              @for (u of entries(c.unknownCodes); track u[0]) { <span class="chip warn">{{ u[0] }} : {{ u[1].total }}</span> }</p>
          }
          <details style="margin-top: 8px"><summary>{{ c.toAttach.length }} poste(s) à corriger dans la CMDB</summary>
            <div class="scroll" style="max-height: 320px">
              <table>
                <tr><th>Identifiant</th><th>Nom</th><th>Valeur du pivot</th><th>Site</th><th>Entité</th><th>Modèle</th></tr>
                @for (w of c.toAttach; track w.id) { <tr><td class="small">{{ w.id }}</td><td>{{ w.name }}</td><td>{{ w.code ?? '(vide)' }}</td><td>{{ w.site ?? '–' }}</td><td class="small">{{ w.entity ?? '–' }}</td><td class="small">{{ w.model ?? '–' }}</td></tr> }
              </table>
            </div>
          </details>
        </div>
      }
    }
  `,
})
export class WorkstationsTab {
  readonly state = inject(SourceState);
  private api = inject(Api);
  draft = signal<Settings>({ ...EMPTY, ...(this.state.profile()?.workstations ?? {}) });
  census = signal<Census | null>(null);
  profiles = signal<{ id: string; name: string; code: string | null }[]>([]);
  busy = signal(false);
  saved = signal(false);
  error = signal('');

  configured = computed(() => !!this.state.profile()?.workstations?.pivotField);
  profileRows = computed(() => {
    const c = this.census();
    if (!c) return [];
    const known = this.profiles().filter((p) => p.code).map((p) => ({ id: p.id, code: p.code!, name: p.name, count: c.byProfile[p.code!] }));
    return known.sort((a, b) => a.code.localeCompare(b.code));
  });

  constructor() {
    this.api.get<{ census: Census | null; profiles: any[] }>('/postes').then((r) => {
      this.profiles.set(r.profiles);
      if (r.census?.sourceKey === this.state.profile()?.key) this.census.set(r.census);
    });
  }

  set(field: keyof Settings, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.draft.update((d) => ({ ...d, [field]: field === 'pivotField' ? value : value || null }));
    this.saved.set(false);
  }

  async save(): Promise<void> {
    const p = this.state.profile()!;
    this.busy.set(true);
    this.error.set('');
    try {
      const saved = await this.api.put<any>(`/sources/${p.key}`, {
        label: p.label, kind: p.kind, connection: p.connection, mapping: p.mapping, sourceIsMaster: p.sourceIsMaster, workstations: this.draft(),
      });
      this.state.adopt(saved, { mapping: this.state.mappingDirty(), connection: this.state.connectionDirty() });
      this.saved.set(true);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  async run(event?: Event): Promise<void> {
    const p = this.state.profile()!;
    const input = event?.target as HTMLInputElement | undefined;
    const file = input?.files?.[0];
    if (p.kind === 'fichier' && !file) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const path = `/sources/${p.key}/postes` + (file ? `?name=${encodeURIComponent(file.name)}` : '');
      const r = file ? await this.api.upload<{ census: Census }>(path, file) : await this.api.post<{ census: Census }>(path, {});
      this.census.set(r.census);
      this.profiles.set((await this.api.get<{ profiles: any[] }>('/postes')).profiles);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      if (input) input.value = '';
      this.busy.set(false);
    }
  }

  attached(c: Census): number { return Object.values(c.byProfile).reduce((n, x) => n + x.total, 0); }
  unknown(c: Census): number { return Object.values(c.unknownCodes).reduce((n, x) => n + x.total, 0); }
  entries(o: Record<string, Count>): [string, Count][] { return Object.entries(o); }
  split(o: Record<string, number> | undefined): string {
    return o ? Object.entries(o).sort((a, b) => b[1] - a[1]).map(([k, n]) => `${k} : ${n}`).join(', ') : '–';
  }
  date(iso: string): string { return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' }); }
}
