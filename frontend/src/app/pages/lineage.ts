import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { Session } from '../session';

interface Role { appId: string; appName: string; role: string; catalogue: boolean; }
interface Attribute { name: string; nature: string | null; sourceApplication: string | null; sourceTable: string | null; sourceField: string | null;
  targetField: string | null; targetSystem: string | null; personal: string | null; sensitivity: string | null; status: string | null; comment: string | null; }
interface DataItem { id: string; name: string; typeLabel: string; code: string | null; category: string | null; sensitivity: string | null; personal: string | null;
  owner: string | null; steward: string | null; pivot: string | null; status: string | null; documentation: string | null; derived: boolean; roles: Role[]; attributes: Attribute[]; }
interface AppItem { id: string; name: string; code: string | null; zone: string | null; status: string | null; roles: string[]; hostIds: string[]; profileIds: string[]; external: boolean; }
interface Step { technicalId: string; order: number | null; status: string | null; }
interface FlowItem { id: string; code: string | null; name: string; sourceId: string; targetId: string; dataIds: string[]; kind: string | null; frequency: string | null;
  mode: string | null; lifecycle: string | null; status: string | null; steps: Step[]; }
interface Technical { id: string; name: string; code: string | null; step: string | null; protocol: string | null; format: string | null; orchestration: string | null;
  zone: string | null; account: string | null; monitoring: string | null; status: string | null; appId: string | null; appName: string | null; hostIds: string[]; }
interface Host { id: string; name: string; typeLabel: string; kind: 'serveur' | 'partage'; details: string[]; location: string | null; usage: string; appIds: string[]; files: string[]; }
interface Profile { id: string; name: string; code: string | null; appIds: string[]; workstations: number | null; bySite: Record<string, number> | null; }
interface Lineage { focus: { id: string; name: string; kind: string; typeLabel: string; supported: boolean; message: string | null };
  data: DataItem[]; applications: AppItem[]; flows: FlowItem[]; technical: Technical[]; hosts: Host[]; profiles: Profile[]; gaps: string[]; censusAt: string | null; }

type Kind = 'donnee' | 'application' | 'flux' | 'etape' | 'hote' | 'poste';
interface Box { id: string; kind: Kind; x: number; y: number; w: number; h: number; title: string; sub: string; full: string; fill: string; stroke: string;
  dashed: boolean; strong: boolean; }
interface Edge { id: string; d: string; dashed: boolean; selected: boolean; lx: number; ly: number; label: string; full: string; }
interface Leader { x1: number; y1: number; x2: number; y2: number; }
interface Band { key: string; label: string; y: number; h: number; }
interface Layout { width: number; height: number; bands: Band[]; boxes: Box[]; edges: Edge[]; chain: Edge[]; leaders: Leader[]; }

const W = 1180;
const PAD = 16;
const BAND_GAP = 26;
const LABEL_H = 22;
const COLORS: Record<Kind, [string, string]> = {
  donnee: ['#ffefc7', '#b58a1f'], application: ['#dff1f5', '#0c5a70'], flux: ['#ffffff', '#0c5a70'], etape: ['#e6f2dd', '#4f7a2d'],
  hote: ['#eef1f2', '#5c6c73'], poste: ['#f4ece0', '#8a6a3a'],
};
const USAGE: Record<string, string> = { stockage: 'stockage', fichiers: 'fichiers', transit: 'transit' };
const TRAIL_KEY = 'ae.parcours.fil';

/** Coupe un libellé pour qu'il tienne dans une largeur donnée (police de 12,5 px environ). */
function fit(text: string, width: number, size = 12.5): string {
  const max = Math.max(4, Math.floor(width / (size * 0.56)));
  return text.length <= max ? text : text.slice(0, max - 1) + '…';
}

const verified = (s: string | null | undefined) => !s || s.trim().toLowerCase() === 'constaté';

/**
 * Parcours d'une donnée : où elle vit et par où elle passe. Quatre bandes empilées (données, applications et flux applicatifs,
 * chaîne technique du flux choisi, hébergement), plus les profils de poste qui y accèdent. Un clic choisit, un double clic recentre.
 */
@Component({
  imports: [RouterLink],
  styles: `
    .lin-split { display: flex; gap: 16px; align-items: flex-start; }
    .lin-main { flex: 1; min-width: 0; }
    .lin-aside { width: 340px; flex: none; position: sticky; top: 12px; max-height: calc(100vh - 24px); overflow: auto; }
    .canvas { background: #fff; border: 1px solid var(--line); border-radius: 8px; overflow: auto; }
    .canvas svg { display: block; width: 100%; min-width: 900px; height: auto; font-family: 'Segoe UI', system-ui, sans-serif; user-select: none; }
    .canvas .box { cursor: pointer; }
    .canvas .box:hover rect { filter: brightness(.97); }
    .canvas .pill { cursor: pointer; }
    .trail { display: flex; flex-wrap: wrap; gap: 4px; align-items: center; font-size: 12.5px; margin-bottom: 8px; }
    .trail a { cursor: pointer; }
    dl.props { display: grid; grid-template-columns: max-content 1fr; gap: 3px 10px; margin: 0 0 8px; font-size: 13px; }
    dl.props dt { color: var(--muted); } dl.props dd { margin: 0; overflow-wrap: anywhere; }
    .fiche { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr); gap: 4px 24px; }
    .roles span { margin: 0 6px 4px 0; }
    .gaps li { margin-bottom: 4px; }
    .legend { display: flex; flex-wrap: wrap; gap: 14px; font-size: 12px; color: var(--muted); margin: 8px 2px 0; }
    .legend i { display: inline-block; width: 22px; height: 0; border-top: 2px solid #0c5a70; vertical-align: middle; margin-right: 5px; }
    .legend i.d { border-top-style: dashed; }
    @media (max-width: 1200px) { .lin-split { flex-direction: column; } .lin-aside { width: 100%; position: static; } .fiche { grid-template-columns: 1fr; } }
  `,
  template: `
    @if (trail().length > 1) {
      <nav class="trail" aria-label="Parcours déjà consultés">
        @for (t of trail(); track t.id; let last = $last) {
          @if (last) { <b>{{ t.name }}</b> } @else { <a (click)="go(t.id)">{{ t.name }}</a> <span class="muted">›</span> }
        }
      </nav>
    }
    @if (error()) { <div class="error">{{ error() }}</div> }
    @if (lineage(); as l) {
      <h1>{{ l.focus.kind === 'donnee' ? 'Parcours de la donnée' : 'Parcours' }} : {{ l.focus.name }}</h1>
      <p class="lead">{{ l.focus.typeLabel }}.
        @switch (l.focus.kind) {
          @case ('donnee') { Où elle vit, par quels flux elle circule, par quelles étapes techniques chacun passe, et qui y accède. }
          @case ('application') { Les données qu'elle tient ou reçoit, ses flux entrants et sortants, et leur réalisation technique. }
          @case ('flux-technique') { Les flux applicatifs qui passent par cette étape : ce qui s'arrête si elle tombe. }
          @case ('infrastructure') { Ce qui est hébergé ici, les données qui y sont stockées ou y transitent. }
          @case ('partage') { Les fichiers déposés ici et les données qu'ils contiennent. }
          @case ('poste') { Les applications utilisées depuis ce profil de poste et les données qu'elles tiennent. }
          @default { }
        }
        <a [routerLink]="['/objets', l.focus.id]">Ouvrir la fiche</a></p>
      @if (!l.focus.supported) {
        <div class="note">{{ l.focus.message }}</div>
      } @else {
        @if (focusData(); as d) {
          <div class="card">
            <div class="fiche">
              <div>
                <h2>Fiche de la donnée @if (d.code) { <span class="chip">{{ d.code }}</span> }</h2>
                @if (d.documentation) { <p style="margin-top: 0">{{ d.documentation }}</p> }
                <div class="roles">
                  @for (r of d.roles; track r.appId) {
                    <span class="chip" [class.info]="r.role === 'Maître'" [class.warn]="!r.catalogue" [title]="r.catalogue ? 'Rôle déclaré au catalogue' : 'Accès connu d\\'Archi, absent du catalogue'">{{ r.role }} : {{ r.appName }}</span>
                  } @empty { <span class="muted small">Aucune application reliée à cette donnée.</span> }
                </div>
              </div>
              <dl class="props">
                <dt>Catégorie</dt><dd>{{ d.category ?? '–' }}</dd>
                <dt>Sensibilité</dt><dd>{{ d.sensitivity ?? '–' }}</dd>
                <dt>Données personnelles</dt><dd>@if (d.personal === 'Oui') { <span class="chip bad">Oui</span> } @else { {{ d.personal ?? '–' }} }</dd>
                <dt>Data owner</dt><dd>{{ d.owner ?? '–' }}</dd>
                <dt>Data steward</dt><dd>{{ d.steward ?? '–' }}</dd>
                <dt>Identifiant pivot</dt><dd>{{ d.pivot ?? '–' }}</dd>
                <dt>Statut de l'information</dt><dd>{{ d.status ?? '–' }}</dd>
              </dl>
            </div>
            @if (d.attributes.length) {
              <details style="margin-top: 8px">
                <summary>{{ d.attributes.length }} attribut(s) métier, dont {{ personalCount(d) }} personnel(s)</summary>
                <div class="scroll" style="max-height: 320px; margin-top: 8px">
                  <table>
                    <tr><th>Attribut</th><th>Application source</th><th>Table / champ source</th><th>{{ d.attributes[0].targetSystem ? 'Champ ' + d.attributes[0].targetSystem : 'Champ cible' }}</th><th>Personnel</th><th>Sensibilité</th><th>Statut</th></tr>
                    @for (a of d.attributes; track $index) {
                      <tr>
                        <td>{{ a.name }}</td><td>{{ a.sourceApplication ?? '–' }}</td>
                        <td class="small">{{ a.sourceTable }}@if (a.sourceField) { . {{ a.sourceField }} }</td>
                        <td class="small">{{ a.targetField ?? '–' }}</td>
                        <td>@if (a.personal === 'Oui') { <span class="chip bad">Oui</span> } @else { {{ a.personal ?? '–' }} }</td>
                        <td>{{ a.sensitivity ?? '–' }}</td><td class="small">{{ a.status ?? '–' }}</td>
                      </tr>
                    }
                  </table>
                </div>
              </details>
            }
          </div>
        }

        <div class="tools" role="group" aria-label="Affichage du parcours">
          @for (b of bandChoices; track b.key) {
            <label class="small"><input type="checkbox" [checked]="shown()[b.key]" (change)="toggleBand(b.key)" /> {{ b.label }}</label>
          }
          <span class="muted small">|</span>
          <label class="small"><input type="checkbox" [checked]="verifiedOnly()" (change)="verifiedOnly.set(!verifiedOnly())" /> Constaté seulement</label>
          @if (l.focus.kind === 'donnee') {
            <label class="small"><input type="checkbox" [checked]="derived()" (change)="toggleDerived()" /> Suivre les données dérivées</label>
          }
          <!-- Droit serveur : AccessRules.cs, écriture sous /api/diagrams → contributeur. -->
          @if (session.can('contributeur') && !api.etat) {
            <button type="button" class="btn ghost right" [disabled]="saving()" (click)="saveView()" title="Crée ou régénère une vue du référentiel, exportable vers Archi">Enregistrer comme vue</button>
            <button type="button" class="btn ghost" (click)="download()">Télécharger l'image (SVG)</button>
          } @else {
            <button type="button" class="btn ghost right" (click)="download()">Télécharger l'image (SVG)</button>
          }
        </div>
        @if (savedView(); as v) {
          <div class="note small">Vue « {{ v.name }} » enregistrée ({{ v.nodes }} élément(s), {{ v.connections }} relation(s)). <a [routerLink]="['/vues', v.id]">Ouvrir la vue</a> ;
            elle s'exporte vers Archi depuis la page Vues.</div>
        }

        <div class="lin-split">
          <div class="lin-main">
            <div class="canvas" #canvas>
              @if (layout(); as g) {
                <svg [attr.viewBox]="'0 0 ' + g.width + ' ' + g.height" role="img" [attr.aria-label]="'Parcours de ' + l.focus.name" xmlns="http://www.w3.org/2000/svg">
                  <defs>
                    <marker id="lin-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
                      <path d="M1 1L9 5L1 9" fill="none" stroke="#0c5a70" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" />
                    </marker>
                    <marker id="lin-arrow-sel" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
                      <path d="M1 1L9 5L1 9" fill="none" stroke="#e5a50a" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
                    </marker>
                  </defs>
                  @for (b of g.bands; track b.key) {
                    <rect [attr.x]="4" [attr.y]="b.y" [attr.width]="g.width - 8" [attr.height]="b.h" rx="6" fill="#f7f9f9" stroke="#e3e8ea" />
                    <text [attr.x]="PAD" [attr.y]="b.y + 15" font-size="12" fill="#5c6c73">{{ b.label }}</text>
                  }
                  @for (k of g.leaders; track $index) {
                    <line [attr.x1]="k.x1" [attr.y1]="k.y1" [attr.x2]="k.x2" [attr.y2]="k.y2" stroke="#9aa9ae" stroke-width="1" stroke-dasharray="3 3" />
                  }
                  @for (e of g.edges; track e.id) {
                    <path [attr.d]="e.d" fill="none" [attr.stroke]="e.selected ? '#e5a50a' : '#0c5a70'" [attr.stroke-width]="e.selected ? 2.6 : 1.4"
                      [attr.stroke-dasharray]="e.dashed ? '6 4' : null" [attr.marker-end]="e.selected ? 'url(#lin-arrow-sel)' : 'url(#lin-arrow)'" />
                  }
                  @for (e of g.chain; track e.id) {
                    <path [attr.d]="e.d" fill="none" stroke="#4f7a2d" stroke-width="1.4" [attr.stroke-dasharray]="e.dashed ? '6 4' : null" />
                  }
                  @for (e of g.edges; track e.id) {
                    <g class="pill" (click)="select('flux', e.id)" (dblclick)="go(e.id)">
                      <title>{{ e.full }}</title>
                      <rect [attr.x]="e.lx - pillWidth(e.label) / 2" [attr.y]="e.ly - 9" [attr.width]="pillWidth(e.label)" height="18" rx="9"
                        [attr.fill]="e.selected ? '#e5a50a' : '#ffffff'" [attr.stroke]="e.selected ? '#b88200' : '#0c5a70'" stroke-width="1" />
                      <text [attr.x]="e.lx" [attr.y]="e.ly + 4" font-size="11" text-anchor="middle" pointer-events="none" [attr.fill]="e.selected ? '#14262d' : '#0c5a70'" font-weight="600">{{ e.label }}</text>
                    </g>
                  }
                  @for (b of g.boxes; track b.kind + b.id) {
                    <g class="box" (click)="select(b.kind, b.id)" (dblclick)="go(b.id)" [attr.tabindex]="0" (keydown.enter)="select(b.kind, b.id)" role="button" [attr.aria-label]="b.full">
                      <title>{{ b.full }}</title>
                      <rect [attr.x]="b.x" [attr.y]="b.y" [attr.width]="b.w" [attr.height]="b.h" rx="6" [attr.fill]="b.fill"
                        [attr.stroke]="isSelected(b) ? '#e5a50a' : b.stroke" [attr.stroke-width]="isSelected(b) ? 2.6 : b.strong ? 2 : 1"
                        [attr.stroke-dasharray]="b.dashed ? '5 4' : null" />
                      <text [attr.x]="b.x + 9" [attr.y]="b.y + 19" font-size="12.5" font-weight="600" fill="#14262d" pointer-events="none">{{ b.title }}</text>
                      <text [attr.x]="b.x + 9" [attr.y]="b.y + 36" font-size="11" fill="#5c6c73" pointer-events="none">{{ b.sub }}</text>
                    </g>
                  }
                </svg>
              }
            </div>
            <div class="legend">
              <span><i></i>constaté</span><span><i class="d"></i>proposé, à confirmer ou à définir</span>
              <span>Clic : détail · double clic : recentrer le parcours</span>
              @if (l.censusAt) { <span>Postes comptés le {{ date(l.censusAt) }}</span> }
            </div>
            @if (l.gaps.length) {
              <div class="card" style="margin-top: 14px">
                <h2>Ce que le parcours ne sait pas dire</h2>
                <ul class="gaps small" style="margin: 0; padding-left: 18px">
                  @for (g of l.gaps; track $index) { <li>{{ g }}</li> }
                </ul>
              </div>
            }
          </div>

          <aside class="card lin-aside" aria-live="polite">
            @if (detail(); as d) {
              <div class="head"><h2 style="margin: 0">{{ d.title }}</h2></div>
              <p class="muted small" style="margin-top: 0">{{ d.kind }}</p>
              <dl class="props">
                @for (p of d.props; track p[0]) { <dt>{{ p[0] }}</dt><dd>{{ p[1] }}</dd> }
              </dl>
              @if (d.list.length) {
                <ul class="small" style="padding-left: 18px">@for (x of d.list; track $index) { <li>{{ x }}</li> }</ul>
              }
              <p>
                @if (d.id !== l.focus.id) { <button type="button" class="btn" (click)="go(d.id)">Recentrer le parcours ici</button> }
                @if (d.object) { <a class="btn ghost" style="margin-left: 6px" [routerLink]="['/objets', d.id]">Ouvrir la fiche</a> }
              </p>
            } @else {
              <h2>Détail</h2>
              <p class="muted small">Cliquez sur une case ou sur la pastille d'un flux. La chaîne technique affichée est celle du flux choisi (en jaune).</p>
              <dl class="props">
                <dt>Données</dt><dd>{{ l.data.length }}</dd><dt>Applications</dt><dd>{{ l.applications.length }}</dd>
                <dt>Flux applicatifs</dt><dd>{{ l.flows.length }}</dd><dt>Étapes techniques</dt><dd>{{ l.technical.length }}</dd>
                <dt>Serveurs et partages</dt><dd>{{ l.hosts.length }}</dd><dt>Profils de poste</dt><dd>{{ l.profiles.length }}</dd>
              </dl>
            }
          </aside>
        </div>
      }
    } @else if (!error()) {
      <p class="muted">Chargement…</p>
    }
  `,
})
export class LineagePage {
  readonly api = inject(Api);
  readonly session = inject(Session);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private canvas = viewChild<ElementRef<HTMLDivElement>>('canvas');
  readonly PAD = PAD;

  lineage = signal<Lineage | null>(null);
  saving = signal(false);
  savedView = signal<{ id: string; name: string; nodes: number; connections: number } | null>(null);
  error = signal('');
  derived = signal(false);
  verifiedOnly = signal(false);
  selected = signal<{ kind: Kind; id: string } | null>(null);
  flowId = signal<string | null>(null);
  shown = signal<Record<string, boolean>>({ donnees: true, applications: true, chaine: true, hebergement: true, postes: true });
  trail = signal<{ id: string; name: string }[]>(readTrail());
  readonly bandChoices = [
    { key: 'donnees', label: 'Données' }, { key: 'chaine', label: 'Chaîne technique' }, { key: 'hebergement', label: 'Hébergement' }, { key: 'postes', label: 'Postes' },
  ];

  focusData = computed(() => {
    const l = this.lineage();
    return l?.focus.kind === 'donnee' ? l.data.find((d) => d.id === l.focus.id) ?? null : null;
  });

  /** Le parcours après filtre « constaté seulement » : flux, étapes et applications non vérifiés disparaissent (sauf l'objet regardé). */
  private view = computed(() => {
    const l = this.lineage();
    if (!l) return null;
    if (!this.verifiedOnly()) return l;
    const tech = new Map(l.technical.map((t) => [t.id, t]));
    const flows = l.flows.filter((f) => verified(f.status)).map((f) => ({ ...f, steps: f.steps.filter((s) => verified(s.status) && verified(tech.get(s.technicalId)?.status)) }));
    const apps = l.applications.filter((a) => a.id === l.focus.id || verified(a.status));
    const ids = new Set(apps.map((a) => a.id));
    return { ...l, applications: apps, flows: flows.filter((f) => ids.has(f.sourceId) && ids.has(f.targetId)) };
  });

  layout = computed<Layout | null>(() => {
    const l = this.view();
    if (!l || !l.focus.supported) return null;
    return this.build(l);
  });

  detail = computed(() => {
    const l = this.lineage();
    const s = this.selected();
    if (!l || !s) return null;
    const v = (x: string | null | undefined) => x ?? '–';
    const name = (id: string) => l.applications.find((a) => a.id === id)?.name ?? l.hosts.find((h) => h.id === id)?.name ?? id;
    switch (s.kind) {
      case 'donnee': {
        const d = l.data.find((x) => x.id === s.id); if (!d) return null;
        return { id: d.id, title: d.name, kind: d.derived ? `${d.typeLabel}, donnée dérivée` : d.typeLabel, object: true,
          props: [['Code', v(d.code)], ['Sensibilité', v(d.sensitivity)], ['Données personnelles', v(d.personal)], ['Data owner', v(d.owner)], ['Statut', v(d.status)]],
          list: d.roles.map((r) => `${r.role} : ${r.appName}`) };
      }
      case 'application': {
        const a = l.applications.find((x) => x.id === s.id); if (!a) return null;
        return { id: a.id, title: a.name, kind: a.external ? 'Destinataire externe' : 'Application', object: true,
          props: [['Code', v(a.code)], ['Zone', v(a.zone)], ['Rôle sur les données', a.roles.join(', ') || '–'], ['Statut', v(a.status)]],
          list: [...a.hostIds.map((h) => 'Hébergée sur ' + name(h)), ...a.profileIds.map((p) => 'Utilisée depuis ' + (l.profiles.find((x) => x.id === p)?.name ?? p))] };
      }
      case 'flux': {
        const f = l.flows.find((x) => x.id === s.id); if (!f) return null;
        const tech = new Map(l.technical.map((t) => [t.id, t]));
        return { id: f.id, title: (f.code ? f.code + ' ' : '') + f.name, kind: 'Flux applicatif', object: false,
          props: [['De', name(f.sourceId)], ['Vers', name(f.targetId)], ['Données', f.dataIds.map((d) => l.data.find((x) => x.id === d)?.name ?? d).join(', ') || '–'],
            ['Type', v(f.kind)], ['Périodicité', v(f.frequency)], ['Mode', v(f.mode)], ['Cycle de vie', v(f.lifecycle)], ['Statut', v(f.status)]],
          list: f.steps.map((st, i) => `${i + 1}. ${tech.get(st.technicalId)?.name ?? '?'} (${tech.get(st.technicalId)?.step ?? 'étape'})${verified(st.status) ? '' : ' : ' + st.status}`) };
      }
      case 'etape': {
        const t = l.technical.find((x) => x.id === s.id); if (!t) return null;
        return { id: t.id, title: t.name, kind: 'Flux technique', object: true,
          props: [['Code', v(t.code)], ['Étape', v(t.step)], ['Protocole', v(t.protocol)], ['Format', v(t.format)], ['Orchestration', v(t.orchestration)],
            ['Zone', v(t.zone)], ['Compte de service', v(t.account)], ['Supervision', v(t.monitoring)], ['Application', v(t.appName)], ['Statut', v(t.status)]],
          list: l.flows.filter((f) => f.steps.some((st) => st.technicalId === t.id)).map((f) => 'Réalise ' + (f.code ?? '') + ' ' + f.name) };
      }
      case 'hote': {
        const h = l.hosts.find((x) => x.id === s.id); if (!h) return null;
        return { id: h.id, title: h.name, kind: h.typeLabel, object: true,
          props: [['Usage dans ce parcours', USAGE[h.usage] ?? h.usage], ['Lieu', v(h.location)], ['Logiciels', h.details.join(', ') || '–']],
          list: [...h.appIds.map((a) => 'Héberge ' + name(a)), ...h.files.map((f) => 'Fichiers : ' + f)] };
      }
      case 'poste': {
        const p = l.profiles.find((x) => x.id === s.id); if (!p) return null;
        return { id: p.id, title: p.name, kind: 'Profil de poste', object: true,
          props: [['Code', v(p.code)], ['Postes (CMDB)', p.workstations === null ? 'non compté' : String(p.workstations)]],
          list: [...p.appIds.map((a) => 'Donne accès à ' + name(a)), ...Object.entries(p.bySite ?? {}).map(([site, n]) => `${site} : ${n} poste(s)`)] };
      }
    }
    return null;
  });

  constructor() {
    this.route.paramMap.subscribe((p) => {
      const id = p.get('id');
      if (id) this.load(id);
    });
    this.route.queryParamMap.subscribe((q) => this.derived.set(q.get('derivees') === 'oui'));
    effect(() => { try { sessionStorage.setItem(TRAIL_KEY, JSON.stringify(this.trail())); } catch { /* stockage indisponible */ } });
  }

  private async load(id: string) {
    this.error.set('');
    this.selected.set(null);
    this.savedView.set(null);
    try {
      const l = await this.api.get<Lineage>(`/parcours/${encodeURIComponent(id)}${this.derivedQuery()}`);
      this.lineage.set(l);
      this.flowId.set(l.flows.find((f) => f.steps.length > 0)?.id ?? l.flows[0]?.id ?? null);
      const trail = this.trail().filter((t) => t.id !== id);
      const at = this.trail().findIndex((t) => t.id === id);
      this.trail.set(at >= 0 ? this.trail().slice(0, at + 1) : [...trail, { id, name: l.focus.name }].slice(-8));
    } catch (e: any) {
      this.error.set(e.message ?? 'Parcours illisible.');
    }
  }

  private derivedQuery(): string {
    return this.route.snapshot.queryParamMap.get('derivees') === 'oui' ? '?derivees=true' : '';
  }

  async saveView() {
    const l = this.lineage();
    if (!l) return;
    this.saving.set(true);
    try {
      this.savedView.set(await this.api.post(`/diagrams/parcours/${encodeURIComponent(l.focus.id)}${this.derivedQuery()}`, {}));
    } catch (e: any) {
      this.error.set(e.message);
    } finally {
      this.saving.set(false);
    }
  }

  toggleDerived() {
    this.router.navigate([], { queryParams: { derivees: this.derived() ? null : 'oui' }, queryParamsHandling: 'merge' })
      .then(() => this.load(this.route.snapshot.paramMap.get('id')!));
  }

  toggleBand(key: string) {
    this.shown.set({ ...this.shown(), [key]: !this.shown()[key] });
  }

  select(kind: Kind, id: string) {
    this.selected.set({ kind, id });
    if (kind === 'flux') this.flowId.set(id);
  }

  isSelected(b: Box): boolean {
    const s = this.selected();
    return !!s && s.kind === b.kind && s.id === b.id;
  }

  go(id: string) {
    this.router.navigate(['/parcours', id], { queryParamsHandling: 'preserve' });
  }

  pillWidth(label: string): number {
    return Math.max(34, label.length * 6.6 + 14);
  }

  personalCount(d: DataItem): number {
    return d.attributes.filter((a) => a.personal === 'Oui').length;
  }

  date(iso: string): string {
    return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
  }

  /** Télécharge le dessin tel qu'affiché : les couleurs sont portées par les attributs, l'image se suffit à elle-même. */
  download() {
    const svg = this.canvas()?.nativeElement.querySelector('svg');
    if (!svg) return;
    const clone = svg.cloneNode(true) as SVGSVGElement;
    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    clone.setAttribute('font-family', 'Segoe UI, Arial, sans-serif');
    const blob = new Blob(['<?xml version="1.0" encoding="UTF-8"?>\n' + clone.outerHTML], { type: 'image/svg+xml' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'parcours-' + (this.lineage()?.focus.name ?? 'donnee').replace(/[^\p{L}\p{N}]+/gu, '-').toLowerCase() + '.svg';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  // ------------------------------------------------------------ Disposition

  private build(l: Lineage): Layout {
    const boxes: Box[] = [];
    const edges: Edge[] = [];
    const chain: Edge[] = [];
    const leaders: Leader[] = [];
    const bands: Band[] = [];
    const show = this.shown();
    let y = 8;
    const tech = new Map(l.technical.map((t) => [t.id, t]));
    const box = (b: Omit<Box, 'fill' | 'stroke'>): Box => {
      const [fill, stroke] = COLORS[b.kind];
      const full = { ...b, fill, stroke };
      boxes.push(full);
      return full;
    };

    /** Range des cases sur des lignes qui retournent à la ligne. */
    const rows = (items: { w: number }[], top: number, h: number): { x: number; y: number }[] => {
      const out: { x: number; y: number }[] = [];
      let x = PAD, yy = top;
      for (const it of items) {
        if (x + it.w > W - PAD && x > PAD) { x = PAD; yy += h + 12; }
        out.push({ x, y: yy });
        x += it.w + 14;
      }
      return out;
    };

    // 1. Données.
    if (show['donnees'] && l.data.length) {
      const top = y + LABEL_H;
      const w = 250, h = 46;
      const pos = rows(l.data.map(() => ({ w })), top, h);
      l.data.forEach((d, i) => {
        const master = d.roles.find((r) => r.role === 'Maître')?.appName;
        box({ id: d.id, kind: 'donnee', x: pos[i].x, y: pos[i].y, w, h, title: fit((d.code ? d.code + ' ' : '') + d.name, w - 18),
          sub: fit([d.derived ? 'dérivée' : null, master ? 'maître : ' + master : null, d.personal === 'Oui' ? 'données personnelles' : null].filter(Boolean).join(', ') || d.typeLabel, w - 18, 11),
          full: d.name, dashed: d.derived || !verified(d.status), strong: d.id === l.focus.id });
      });
      const bottom = Math.max(...pos.map((p) => p.y)) + h;
      bands.push({ key: 'donnees', label: 'Données', y, h: bottom - y + 12 });
      y = bottom + 12 + BAND_GAP;
    }

    // 2. Applications rangées en colonnes selon le sens des flux (rang = plus long chemin entrant).
    const appIds = l.applications.map((a) => a.id);
    const rank = new Map<string, number>(appIds.map((id) => [id, 0]));
    const flowsBetween = l.flows.filter((f) => rank.has(f.sourceId) && rank.has(f.targetId) && f.sourceId !== f.targetId);
    for (let pass = 0; pass < appIds.length; pass++) {
      let changed = false;
      for (const f of flowsBetween) {
        const r = rank.get(f.sourceId)! + 1;
        if (r > rank.get(f.targetId)! && r < 6) { rank.set(f.targetId, r); changed = true; }
      }
      if (!changed) break;
    }
    // L'objet regardé, s'il est une application, reste à gauche quand rien n'entre chez lui.
    const columns = [...new Set([...rank.values()])].sort((a, b) => a - b);
    const colOf = new Map(columns.map((c, i) => [c, i]));
    const appW = 190, appH = 46, colCount = Math.max(1, columns.length);
    const colX = (c: number) => colCount === 1 ? (W - appW) / 2 : PAD + 20 + (c * (W - 2 * PAD - 40 - appW)) / (colCount - 1);
    const appBox = new Map<string, Box>();
    if (l.applications.length) {
      const top = y + LABEL_H + 6;
      const perCol = new Map<number, AppItem[]>();
      for (const a of [...l.applications].sort((x, z) => x.name.localeCompare(z.name))) {
        const c = colOf.get(rank.get(a.id)!)!;
        perCol.set(c, [...(perCol.get(c) ?? []), a]);
      }
      let bottom = top;
      for (const [c, apps] of perCol) {
        apps.forEach((a, i) => {
          const yy = top + i * (appH + 22);
          const sub = a.external ? 'destinataire externe' : [a.code, a.roles.join(', ') || null, a.zone && a.zone !== 'IT' ? a.zone : null].filter(Boolean).join(' · ');
          appBox.set(a.id, box({ id: a.id, kind: 'application', x: colX(c), y: yy, w: appW, h: appH, title: fit(a.name, appW - 18), sub: fit(sub || 'application', appW - 18, 11),
            full: a.name, dashed: !verified(a.status) || a.external, strong: a.id === l.focus.id }));
          bottom = Math.max(bottom, yy + appH);
        });
      }
      // Flux : courbe de la sortie droite de l'émetteur à l'entrée gauche du récepteur ; plusieurs flux entre deux mêmes applications s'écartent.
      const seen = new Map<string, number>();
      const incoming = new Map<string, number>();
      for (const f of l.flows) {
        const s = appBox.get(f.sourceId), t = appBox.get(f.targetId);
        if (!s || !t) continue;
        const pair = f.sourceId + '>' + f.targetId;
        const n = seen.get(pair) ?? 0;
        seen.set(pair, n + 1);
        const off = (n % 2 ? 1 : -1) * Math.ceil(n / 2) * 9;
        const forward = t.x > s.x + s.w / 2;
        const x1 = forward ? s.x + s.w : s.x + s.w / 2, y1 = forward ? s.y + s.h / 2 + off : s.y + s.h;
        const x2 = forward ? t.x - 2 : t.x + t.w / 2 + off, y2 = forward ? t.y + t.h / 2 + off : t.y - 2;
        let d: string, lx: number, ly: number;
        if (forward) {
          const dx = Math.max(40, (x2 - x1) / 2);
          d = `M${x1},${y1} C${x1 + dx},${y1} ${x2 - dx},${y2} ${x2},${y2}`;
          // Pastille côté récepteur, décalée pour chaque flux qui arrive au même récepteur : elles ne se chevauchent pas.
          const k = incoming.get(f.targetId) ?? 0;
          incoming.set(f.targetId, k + 1);
          const t = Math.max(0.3, 0.8 - 0.15 * k), u = 1 - t;
          lx = u * u * u * x1 + 3 * u * u * t * (x1 + dx) + 3 * u * t * t * (x2 - dx) + t * t * t * x2;
          ly = u * u * u * y1 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y2;
        } else {
          // Même colonne ou retour en arrière : boucle par le dessous puis par le dessus.
          const loop = Math.max(y1, y2) + 18 + n * 6;
          d = `M${x1},${y1} C${x1},${loop} ${x2},${y2 - 40} ${x2},${y2}`;
          lx = (x1 + x2) / 2 + 18; ly = (y1 + y2) / 2 + 4;
        }
        const label = f.code ?? fit(f.name, 90, 11);
        edges.push({ id: f.id, d, dashed: !verified(f.status), selected: this.flowId() === f.id, lx, ly, label, full: `${f.code ?? ''} ${f.name}`.trim() });
      }
      bands.push({ key: 'applications', label: 'Applications et flux applicatifs (clic sur une pastille : sa chaîne technique)', y, h: bottom - y + 22 });
      y = bottom + 22 + BAND_GAP;
    }

    // 3. Chaîne technique du flux choisi.
    const flow = l.flows.find((f) => f.id === this.flowId());
    const stepBox = new Map<string, Box>();
    if (show['chaine'] && flow) {
      const top = y + LABEL_H + 4;
      const stepW = 210, stepH = 48;
      if (flow.steps.length === 0) {
        bands.push({ key: 'chaine', label: `Chaîne technique de ${flow.code ?? flow.name} : non décrite au catalogue`, y, h: LABEL_H + 8 });
        y += LABEL_H + 8 + BAND_GAP;
      } else {
        const gap = Math.min(70, (W - 2 * PAD - flow.steps.length * stepW) / Math.max(1, flow.steps.length - 1));
        let x = PAD + Math.max(0, (W - 2 * PAD - flow.steps.length * stepW - (flow.steps.length - 1) * gap) / 2);
        flow.steps.forEach((st, i) => {
          const t = tech.get(st.technicalId);
          if (!t) return;
          const b = box({ id: t.id, kind: 'etape', x, y: top, w: stepW, h: stepH, title: fit(t.name, stepW - 18),
            sub: fit([t.code, t.step, t.protocol].filter(Boolean).join(' · '), stepW - 18, 11), full: t.name,
            dashed: !verified(st.status) || !verified(t.status), strong: t.id === l.focus.id });
          stepBox.set(t.id + '#' + i, b);
          if (i > 0) {
            const prev = [...stepBox.values()][stepBox.size - 2];
            chain.push({ id: 'c' + i, d: `M${prev.x + prev.w},${top + stepH / 2} L${x - 2},${top + stepH / 2}`, dashed: !verified(st.status), selected: false, lx: 0, ly: 0, label: '', full: '' });
          }
          x += stepW + gap;
        });
        bands.push({ key: 'chaine', label: `Chaîne technique de ${flow.code ?? ''} ${flow.name}`.replace('  ', ' '), y, h: stepH + LABEL_H + 18 });
        y = top + stepH + 14 + BAND_GAP;
      }
    }

    // 4. Hébergement : serveurs et partages, ceux de la chaîne choisie mis en avant.
    if (show['hebergement'] && l.hosts.length) {
      const top = y + LABEL_H;
      const w = 205, h = 46;
      const chainHosts = new Set([...stepBox.values()].flatMap((b) => tech.get(b.id)?.hostIds ?? []));
      const hosts = [...l.hosts].sort((a, b) => Number(chainHosts.has(b.id)) - Number(chainHosts.has(a.id)));
      const pos = rows(hosts.map(() => ({ w })), top, h);
      const hostBox = new Map<string, Box>();
      hosts.forEach((hst, i) => {
        const sub = [USAGE[hst.usage] ?? hst.usage, hst.kind === 'partage' ? 'partage' : null, hst.details[0] ?? null].filter(Boolean).join(' · ');
        hostBox.set(hst.id, box({ id: hst.id, kind: 'hote', x: pos[i].x, y: pos[i].y, w, h, title: fit(hst.name, w - 18), sub: fit(sub, w - 18, 11), full: `${hst.name} (${hst.typeLabel})`,
          dashed: hst.usage === 'transit', strong: chainHosts.has(hst.id) || hst.id === l.focus.id }));
      });
      for (const [key, b] of stepBox) {
        for (const hid of tech.get(b.id)?.hostIds ?? []) {
          const hb = hostBox.get(hid);
          if (hb) leaders.push({ x1: b.x + b.w / 2, y1: b.y + b.h, x2: hb.x + hb.w / 2, y2: hb.y });
        }
        void key;
      }
      const bottom = Math.max(...pos.map((p) => p.y)) + h;
      bands.push({ key: 'hebergement', label: 'Hébergement (trait plein : la donnée y est stockée ; pointillé : elle y transite)', y, h: bottom - y + 12 });
      y = bottom + 12 + BAND_GAP;
    }

    // 5. Accès utilisateurs : profils de poste.
    if (show['postes']) {
      if (l.profiles.length) {
        const top = y + LABEL_H;
        const w = 230, h = 46;
        const pos = rows(l.profiles.map(() => ({ w })), top, h);
        l.profiles.forEach((p, i) => box({ id: p.id, kind: 'poste', x: pos[i].x, y: pos[i].y, w, h, title: fit(p.name, w - 18),
          sub: fit([p.code, p.workstations === null ? 'postes non comptés' : `${p.workstations} poste(s)`].filter(Boolean).join(' · '), w - 18, 11), full: p.name,
          dashed: false, strong: p.id === l.focus.id }));
        const bottom = Math.max(...pos.map((p) => p.y)) + h;
        bands.push({ key: 'postes', label: 'Accès utilisateurs (profils de poste)', y, h: bottom - y + 12 });
        y = bottom + 12 + BAND_GAP;
      }
    }
    return { width: W, height: Math.max(120, y - BAND_GAP + 10), bands, boxes, edges, chain, leaders };
  }
}

function readTrail(): { id: string; name: string }[] {
  try { return JSON.parse(sessionStorage.getItem(TRAIL_KEY) ?? '[]'); } catch { return []; }
}
