import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Api, Brief } from '../api';
import { Session } from '../session';
import { ObjectSearch } from '../object-search';
import { ScopeNote } from '../scope-note';
import { LAYER_LABELS, NO_LAYER, OFF_CHAIN, type StepItem, type CellLine, cellLine, chainQuery, choosableLayers, domainQuery, emptyText, hueOf, layerQuery, moreDirectText, objectQuery, offMark, openChain, SHOWN_DIRECT, stepsByChain, tabMark, touchedSummary, viaText } from '../chains-reading';

interface Column { id: string; name: string; kind: string; capabilities: StepItem[]; layers: Record<string, StepItem[]>; }
interface Chain { id: string; name: string; hasSteps: boolean; ordered: boolean; columns: Column[]; whole: Column | null; }
interface ProcessView { id: string; name: string; ordered: boolean; columns: Column[]; }
interface OffChain { id: string; name: string; nature: string | null; topCapability: string | null; }
interface Grid {
  serversAvailable: boolean; natureKnown: boolean; layers: string[]; domains: { id: string; name: string }[];
  domainOf: Record<string, string>; chains: Chain[]; processes: ProcessView[]; offChain: OffChain[];
}
interface Touched { chainId: string; columnId: string; via: StepItem[]; }
interface Impact { object: Brief; layer: string | null; steps: Touched[]; offChain: string[]; }

/**
 * Chaînes et processus (EV-06, maquette `maquette-chaines-couches-impact-camille`, reprise selon
 * `review-chaines-0.15.32-camille` §4.1, §4.2). Une chaîne à la fois, en onglets (plus « Hors chaîne ») ; ses étapes en
 * chevrons, les capacités toujours dessous, puis une couche au choix (« Aucune » par défaut) en une ligne de noms
 * dédoublonnés. Un objet désigné marque les onglets et les étapes qu'il touche. Un tableau HTML (§6) : les chevrons et
 * les cartes sont du style. Chaîne, couche, objet et domaine sont dans l'adresse : le lien se partage (C10).
 */
@Component({
  imports: [RouterLink, NgTemplateOutlet, ObjectSearch, ScopeNote],
  host: { '(document:keydown.escape)': 'onEscape()' },
  template: `
    <h1>Chaînes et processus</h1>
    <p class="lead">Ce que l'entreprise produit, étape par étape, et ce qui porte chaque étape.</p>

    <div class="settings no-print">
      <fieldset class="segmented">
        <legend>Afficher sous les étapes :</legend>
        @for (l of layers(); track l) {
          <label [class.on]="layer() === l"><input type="radio" name="couche" [value]="l" [checked]="layer() === l" (change)="setLayer(l)" />{{ label(l) }}</label>
        }
      </fieldset>
      <div class="pick">
        <span>Étapes touchées par :</span>
        @if (impact(); as im) {
          <span class="chosen">{{ im.object.name }} <button type="button" class="x" aria-label="Effacer l'objet désigné" (click)="designate(null)">×</button></span>
        } @else {
          <app-object-search placeholder="Serveur, application ou projet…" [types]="searchTypes()" [showAll]="false" (picked)="designate($event.id)" />
        }
      </div>
    </div>
    @if (grid() && !grid()!.serversAvailable) { <app-scope-note text="Serveurs non affichés pour votre accès." /> }
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }

    @if (impact(); as im) {
      <div class="impact-line" role="status">
        @if (im.layer) {
          {{ summary() }}
        } @else {
          Les étapes touchées se lisent à partir d'un serveur, d'une application ou d'un projet.
        }
        <a [routerLink]="['/objets', im.object.id]">Ouvrir la carte</a>
        <a routerLink="/impact" [queryParams]="{ objet: im.object.id }">Analyser l'impact</a>
      </div>
    }

    @if (grid(); as g) {
      @if (g.chains.length === 0 && g.offChain.length === 0) { <p>Aucune chaîne de valeur dans le référentiel.</p> }
      @else {
        <div class="tabs" role="tablist" aria-label="Chaînes de valeur">
          @for (c of g.chains; track c.id) {
            <button type="button" role="tab" [attr.aria-selected]="open() === c.id"
              (click)="setChain(c.id)">{{ c.name }}@if (mark(c.id); as m) { <span class="mark">{{ m }}</span> }</button>
          }
          @if (g.offChain.length) {
            <button type="button" role="tab" [attr.aria-selected]="open() === OFF"
              (click)="setChain(OFF)">Hors chaîne ({{ g.offChain.length }})@if (offTabMark(); as m) { <span class="mark">{{ m }}</span> }</button>
          }
        </div>
      }

      @if (openedChain(); as c) {
        <section class="chain card" role="tabpanel" [attr.aria-label]="c.name">
          <header>
            <h2>{{ c.name }}</h2>
            @if (!c.hasSteps) { <span class="muted small">Étapes non renseignées : l'ordre affiché n'est pas un enchaînement</span> }
            @else if (!c.ordered) { <span class="muted small">Ordre incomplet : l'ordre affiché n'est pas un enchaînement</span> }
          </header>
          <div class="scroll-x">
            <table [class.band]="c.ordered">
              <thead>
                <tr>
                  <th scope="col" class="corner"></th>
                  @for (s of c.columns; track s.id; let first = $first) {
                    <th scope="col" [class.hit]="isTouched(c.id, s.id)">
                      @if (c.ordered && !first) { <span class="arrow" aria-hidden="true"></span><span class="sr">puis </span> }
                      @if (c.hasSteps) {
                        <div class="chevron-wrap" [class.chevron]="c.ordered">
                          <button type="button" class="step" [id]="'etape-' + s.id" [class.chevron]="c.ordered" [class.card-step]="!c.ordered" (click)="openPanel(c.id, s.id)">
                            {{ s.name }}@if (isTouched(c.id, s.id)) { <span class="touched">● touchée</span> }
                          </button>
                        </div>
                      } @else {
                        <button type="button" class="step card-step cap-step" [id]="'etape-' + s.id" [class.faded]="faded(s.id)"
                          [style.--fond]="fond(s.id)" [style.--trait]="trait(s.id)" [style.--texte]="texte(s.id)" (click)="openPanel(c.id, s.id)">
                          {{ s.name }}@if (isTouched(c.id, s.id)) { <span class="touched">● touchée</span> }
                        </button>
                      }
                    </th>
                  }
                  @if (c.whole) { <th scope="col" class="whole" [class.hit]="isTouched(c.id, c.whole.id)"><button type="button" class="step card-step dashed" [id]="'etape-' + c.whole.id" (click)="openPanel(c.id, c.whole.id)">Toute la chaîne@if (isTouched(c.id, c.whole.id)) { <span class="touched">● touchée</span> }</button></th> }
                </tr>
              </thead>
              <tbody>
                @if (c.hasSteps) {
                  <tr class="caps-row">
                    <th scope="row">Capacités</th>
                    @for (s of columnsOf(c); track s.id) {
                      <td [class.whole]="s.kind === 'toute'"><ng-container *ngTemplateOutlet="capsTpl; context: { $implicit: s }" /></td>
                    }
                  </tr>
                }
                @if (impact()?.layer) {
                  <tr class="touch-row"><th scope="row">Touchée par</th>
                    @for (s of columnsOf(c); track s.id) {
                      <td [class.hit]="isTouched(c.id, s.id)" [class.whole]="s.kind === 'toute'">
                        @if (touch(c.id, s.id); as t) { touchée {{ t.via.length ? 'via ' + names(t.via) : '' }} } @else { — }
                      </td>
                    }
                  </tr>
                }
                @if (layer() !== NONE) {
                  <tr class="layer-row">
                    <th scope="row">{{ label(layer()) }}</th>
                    @for (s of columnsOf(c); track s.id) {
                      <td [class.hit]="isTouched(c.id, s.id)" [class.whole]="s.kind === 'toute'">
                        <ng-container *ngTemplateOutlet="lineTpl; context: { $implicit: s }" />
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <!-- Sous 640 px : une carte par étape, l'une sous l'autre ; ▶ devient ▼ ; « Toute la chaîne » en dernière carte (§6). -->
          <div class="narrow">
            @for (s of columnsOf(c); track s.id; let last = $last) {
              <div class="ncard" [class.hit]="isTouched(c.id, s.id)" [class.whole]="s.kind === 'toute'">
                <button type="button" class="step card-step" [class.dashed]="s.kind === 'toute'" (click)="openPanel(c.id, s.id)">
                  {{ s.kind === 'toute' ? 'Toute la chaîne' : s.name }}@if (isTouched(c.id, s.id)) { <span class="touched">● touchée</span> }
                </button>
                @if (c.hasSteps) { <ng-container *ngTemplateOutlet="capsTpl; context: { $implicit: s }" /> }
                @if (impact()?.layer) {
                  <p class="small">Touchée par : @if (touch(c.id, s.id); as t) { touchée {{ t.via.length ? 'via ' + names(t.via) : '' }} } @else { — }</p>
                }
                @if (layer() !== NONE) {
                  <div class="nlabel">{{ label(layer()) }}</div>
                  <ng-container *ngTemplateOutlet="lineTpl; context: { $implicit: s }" />
                }
              </div>
              @if (c.ordered && !last && columnsOf(c)[$index + 1]?.kind !== 'toute') { <div class="down" aria-hidden="true">▼</div> }
            }
          </div>
          @if (domainsShown().length) {
            <div class="legend no-print" aria-label="Domaines">
              @for (d of domainsShown(); track d.id) {
                <button type="button" class="dom" [class.off]="domain() && domain() !== d.id" [attr.aria-pressed]="domain() === d.id"
                  [style.--fond]="fond(d.id)" [style.--trait]="trait(d.id)" [style.--texte]="texte(d.id)" (click)="setDomain(d.id)">{{ d.name }}</button>
              }
            </div>
          }
          @if (panelOf(c.id); as col) {
            <div class="panel" role="region" [attr.aria-label]="col.name + ', ce que l\\'étape mobilise'">
              <div class="panel-head"><b>{{ col.name }} — ce que l'étape mobilise</b><button type="button" class="ghost" (click)="panel.set(null)">Fermer</button></div>
              <dl>
                @for (l of panelLayers(); track l) {
                  <dt>{{ label(l) }}</dt>
                  <dd>
                    @for (i of col.layers[l]; track i.id + (i.via ?? '')) {
                      <span class="pitem"><a [routerLink]="['/objets', i.id]">{{ i.name }}</a>@if (via(i); as v) { <span class="via"> ({{ v }})</span> }@if (l === 'applications' || l === 'serveurs') { · <a routerLink="/impact" [queryParams]="{ objet: i.id }">Analyser l'impact</a> }</span>
                    } @empty { <span class="gap">{{ emptyOf(l) }}</span> }
                  </dd>
                }
              </dl>
            </div>
          }
        </section>
      } @else if (open() === OFF) {
        <section class="chain card" role="tabpanel" aria-label="Processus hors de toute chaîne">
          <header><h2>{{ g.offChain.length }} processus hors de toute chaîne</h2></header>
          @if (g.natureKnown) {
            <h4>Pilotage et support : hors chaîne par nature</h4>
            <ul>@for (p of offSupport(); track p.id) { <li [class.hit]="touchedOff().includes(p.id)"><a [routerLink]="['/objets', p.id]">{{ p.name }}</a> <span class="via">{{ p.topCapability }}</span></li> }</ul>
            <h4>Rattachés à aucune chaîne</h4>
            <ul>@for (p of offOther(); track p.id) { <li [class.hit]="touchedOff().includes(p.id)"><a [routerLink]="['/objets', p.id]">{{ p.name }}</a> <span class="via">{{ p.topCapability }}</span> · <a [routerLink]="['/objets', p.id]">Rattacher</a></li> }</ul>
          } @else {
            <ul>@for (p of g.offChain; track p.id) { <li [class.hit]="touchedOff().includes(p.id)"><a [routerLink]="['/objets', p.id]">{{ p.name }}</a>@if (p.topCapability) { <span class="via">{{ p.topCapability }}</span> }@if (touchedOff().includes(p.id)) { <b> ● touché</b> }</li> }</ul>
          }
        </section>
      }

      @if (g.processes.length) {
        <h2>Processus par étapes</h2>
        @for (p of g.processes; track p.id) {
          <section class="chain card">
            <header><h3>{{ p.name }}</h3>@if (!p.ordered) { <span class="muted small" title="L'ordre affiché n'est pas un enchaînement">Ordre incomplet ⓘ</span> }</header>
            <div class="scroll-x">
              <table>
                <thead><tr><th scope="col" class="corner"></th>
                  @for (s of p.columns; track s.id; let last = $last) {
                    <th scope="col"><span class="step" [class.chevron]="p.ordered">{{ s.name }}</span>@if (p.ordered && !last) { <span class="sr"> puis</span> }</th>
                  }
                </tr></thead>
                <tbody>
                  @for (l of ['qui', 'donnees', 'applications']; track l) {
                    <tr><th scope="row">{{ label(l) }}</th>
                      @for (s of p.columns; track s.id) {
                        <td>
                          @for (i of s.layers[l]; track i.id + (i.via ?? '')) {
                            <div class="item"><a [routerLink]="['/objets', i.id]">{{ i.name }}</a>@if (via(i); as v) { <span class="via">{{ l === 'donnees' ? (i.via === 'écrit' ? '✎ ' : '◉ ') + v : v }}</span> }</div>
                          } @empty { <span class="gap">Rien de connu</span> }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        }
      }
    } @else if (!error()) {
      <div aria-busy="true" aria-label="Chargement des chaînes">
        <section class="chain card skeleton"><div class="bar title"></div><div class="row-bars">@for (b of [1, 2, 3, 4]; track b) { <div class="bar"></div> }</div><div class="row-bars">@for (b of [1, 2, 3, 4]; track b) { <div class="bar tall"></div> }</div></section>
      </div>
    }

    <!-- Capacités de l'étape, toujours sous le chevron : ■ et nom dans la teinte du domaine (revue §4.2). -->
    <ng-template #capsTpl let-s>
      <ul class="caps">
        @for (k of s.capabilities; track k.id) {
          <li [class.faded]="faded(k.id)"><span class="sq" [style.background]="trait(k.id)" aria-hidden="true"></span><a [routerLink]="['/objets', k.id]" [style.color]="texte(k.id)">{{ k.name }}</a></li>
        } @empty { <li class="gap">Aucune capacité ne sert cette étape</li> }
      </ul>
    </ng-template>

    <!-- Ligne d'une couche sous une étape : noms dédoublonnés séparés par « · », directs d'abord, indirects regroupés (§4.2). -->
    <ng-template #lineTpl let-s>
      @let line = lineOf(s);
      @if (line.direct.length || line.indirect.length) {
        <p class="line">
          @for (i of directShown(s.id, line.direct); track i.id; let last = $last) {
            <a class="name" [class.faded]="faded(i.capability)" [class.chosen-item]="impact()?.object?.id === i.id" [routerLink]="[]" [queryParams]="designation(i.id)" queryParamsHandling="merge" [replaceUrl]="true">{{ i.name }}</a>@if (!last) { <span class="via">&nbsp;· </span> }
          }
          @if (moreDirect(s.id, line.direct.length); as m) { <button type="button" class="link small" (click)="expand(s.id, 'direct')">{{ m }}</button> }
          @for (gr of line.indirect; track gr.kind) {
            @if (isExpanded(s.id, gr.kind)) {
              <span class="ind"><span class="via">{{ gr.label }}&nbsp;: </span>
                @for (i of gr.items; track i.id; let last = $last) {
                  <a class="name" [class.faded]="faded(i.capability)" [class.chosen-item]="impact()?.object?.id === i.id" [routerLink]="[]" [queryParams]="designation(i.id)" queryParamsHandling="merge" [replaceUrl]="true">{{ i.name }}</a>@if (!last) { <span class="via">&nbsp;· </span> }
                }
              </span>
            } @else {
              <button type="button" class="link small more" (click)="expand(s.id, gr.kind)">+ {{ gr.items.length }} {{ gr.label }}</button>
            }
          }
        </p>
      } @else {
        <span class="gap">{{ empty().text }}@if (empty().link) { · <a [routerLink]="['/objets', s.kind === 'capacite' ? s.id : s.capabilities[0]?.id ?? s.id]">Renseigner</a> }</span>
      }
    </ng-template>
  `,
  styles: `
    .settings { display: flex; flex-wrap: wrap; gap: 14px 28px; align-items: center; margin-bottom: 12px; }
    fieldset.segmented { border: 0; padding: 0; margin: 0; display: flex; align-items: center; gap: 0; flex-wrap: wrap; }
    fieldset.segmented legend { float: left; margin-right: 10px; font-size: 13.5px; color: var(--muted); }
    .segmented label { border: 1px solid var(--line); padding: 6px 12px; cursor: pointer; background: var(--card); font-size: 13.5px; margin-left: -1px; }
    .segmented label:first-of-type { border-radius: 6px 0 0 6px; } .segmented label:last-of-type { border-radius: 0 6px 6px 0; }
    .segmented label.on { background: var(--accent); color: #fff; border-color: var(--accent); }
    .segmented input { position: absolute; opacity: 0; width: 1px; height: 1px; }
    .segmented label:focus-within { outline: 2px solid var(--signal, #f2b705); outline-offset: 1px; }
    .pick { display: flex; align-items: center; gap: 8px; min-width: 340px; font-size: 13.5px; color: var(--muted); }
    .pick app-object-search { flex: 1; }
    .chosen { background: var(--card); border: 1px solid var(--line); border-radius: 6px; padding: 5px 8px; color: var(--ink); font-weight: 600; }
    .x { background: none; border: 0; padding: 0 4px; color: var(--muted); cursor: pointer; font-size: 16px; }
    .impact-line { margin: 0 0 12px; display: flex; gap: 14px; flex-wrap: wrap; align-items: baseline; }
    /* Onglets : style commun de la charte (styles.css), seulement le retour à la ligne en plus (petit écran). */
    .tabs { flex-wrap: wrap; }
    .tabs button { white-space: normal; }
    .legend { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 12px; }
    .dom { background: var(--fond, var(--bg)); border: 1px solid var(--trait, var(--line)); color: var(--texte, var(--ink)); border-radius: 12px; padding: 3px 10px; font-size: 12.5px; font-weight: 600; }
    .dom.off { opacity: 0.35; }
    .chain { padding: 12px 14px; }
    .chain header { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px; margin-bottom: 8px; }
    .chain h2, .chain h3 { margin: 0; font-size: 17px; }
    .scroll-x { overflow-x: auto; }
    /* Aucun mot coupé (charte §2.1, CH4) : retour à la ligne entre les mots seulement, la colonne prend la largeur du plus long. */
    table { border-collapse: separate; border-spacing: 8px 4px; width: 100%; }
    table.band { border-spacing: 22px 4px; }
    th, td { vertical-align: top; text-align: left; padding: 0; overflow-wrap: normal; }
    thead th { position: relative; }
    th.corner, tbody th { width: 92px; font-size: 12.5px; color: var(--muted); font-weight: 600; padding-top: 4px; position: sticky; left: 0; background: var(--card); z-index: 1; }
    /* ▶ entre deux étapes, dessiné et non écrit : il ne dépend pas de la police (CH8, CH9). */
    .arrow { position: absolute; left: -16px; top: calc(50% - 7px); width: 10px; height: 14px; background: var(--accent); clip-path: polygon(0 0, 100% 50%, 0 100%); }
    .step { display: block; width: 100%; height: 100%; text-align: left; font: inherit; font-weight: 700; font-size: 13.5px; color: var(--ink); background: var(--accent-soft, #e0eef2); border: 0; padding: 8px 12px; cursor: pointer; min-height: 48px; white-space: normal; line-height: 1.25; }
    /* Chevron à pointe franche (CH9) : pointe et creux d'environ un tiers de la hauteur ; le contour « --accent » est
       dessiné par quatre ombres sur l'enveloppe, une bordure ne survivant pas au découpage. */
    thead th:has(.chevron-wrap) { height: 1px; }
    .chevron-wrap { height: 100%; }
    .chevron-wrap.chevron { filter: drop-shadow(1.5px 0 0 var(--accent)) drop-shadow(-1.5px 0 0 var(--accent)) drop-shadow(0 1.5px 0 var(--accent)) drop-shadow(0 -1.5px 0 var(--accent)); }
    .step.chevron { clip-path: polygon(0 0, calc(100% - 18px) 0, 100% 50%, calc(100% - 18px) 100%, 0 100%, 18px 50%); padding: 8px 24px 8px 26px; }
    .step.card-step { border-radius: 8px; border: 1.5px solid var(--accent); }
    .step.cap-step { background: var(--fond, #f6f8f8); border: 0; border-left: 4px solid var(--trait, var(--line)); color: var(--texte, var(--ink)); border-radius: 8px; }
    .step.dashed { background: transparent; border: 1.5px dashed var(--line); color: var(--muted); }
    th.hit .step { background: var(--accent); color: #fff; }
    th.hit .step.dashed { border-color: var(--accent); color: var(--accent); background: transparent; }
    .touched, .tabs .mark { display: block; font-size: 11.5px; font-weight: 600; }
    td.whole, th.whole { border-left: 1px solid var(--line); padding-left: 6px; }
    td.hit { box-shadow: inset 3px 0 0 var(--accent); background: #f2f8fa; padding-left: 9px; }
    .touch-row td { font-size: 12.5px; color: var(--ink); padding: 2px 6px; }
    .caps { list-style: none; margin: 0; padding: 2px 0 0; font-size: 12.5px; font-weight: 600; }
    .caps li { display: flex; gap: 6px; align-items: baseline; margin-bottom: 3px; }
    .faded { opacity: 0.35; }
    .caps a { text-decoration: none; }
    .sq { flex: none; width: 9px; height: 9px; border-radius: 2px; background: var(--line); }
    .layer-row td { border-top: 1px solid var(--line); padding-top: 6px; }
    .line { margin: 0; font-size: 13px; line-height: 1.6; }
    .line .name { color: var(--ink); font-weight: 600; text-decoration: none; }
    .line .name:hover { text-decoration: underline; }
    .tabs .mark { color: var(--accent); }
    .line .name.chosen-item { outline: 2px solid var(--signal, #f2b705); }
    .line .more, .line .ind { display: block; }
    .item { background: #fff; border: 1px solid #0000001a; border-radius: 5px; padding: 3px 6px; margin-top: 3px; font-size: 12.5px; }
    .via { display: block; color: var(--muted); font-size: 11.5px; font-weight: 400; }
    .line .via { display: inline; }
    .gap { color: var(--muted); font-size: 12.5px; }
    .link { background: none; border: 0; padding: 0; color: var(--accent); text-decoration: underline; cursor: pointer; text-align: left; white-space: normal; }
    .panel { margin-top: 10px; border-top: 1px solid var(--line); padding-top: 8px; }
    .panel-head { display: flex; justify-content: space-between; align-items: center; }
    .panel dl { display: grid; grid-template-columns: 120px 1fr; gap: 4px 12px; margin: 8px 0 0; font-size: 13px; }
    .panel dt { color: var(--muted); }
    .pitem { margin-right: 14px; }
    .pitem .via { display: inline; }
    li.hit { font-weight: 600; }
    .sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
    .narrow { display: none; }
    .ncard { border: 1px solid var(--line); border-radius: 8px; padding: 8px; }
    .ncard.hit { border: 2px solid var(--accent); }
    .ncard.hit .step { background: var(--accent); color: #fff; }
    .ncard.whole { border-style: dashed; margin-top: 10px; }
    .ncard .step { margin-bottom: 6px; }
    .nlabel { font-size: 12px; color: var(--muted); font-weight: 600; margin: 4px 0; }
    .down { text-align: center; color: var(--muted); line-height: 1.6; }
    @media (max-width: 640px) { .scroll-x { display: none; } .narrow { display: block; } .settings .pick { min-width: 0; width: 100%; } }
    .skeleton .bar { background: #e6eced; border-radius: 6px; height: 34px; flex: 1; }
    .skeleton .bar.title { width: 260px; height: 20px; flex: none; margin-bottom: 10px; }
    .skeleton .bar.tall { height: 70px; }
    .skeleton .row-bars { display: flex; gap: 8px; margin-bottom: 8px; }
    @media print { .step { print-color-adjust: exact; } .panel { display: none; } .narrow { display: none !important; } .scroll-x { display: block !important; } }
  `,
})
export class ChainsPage {
  private api = inject(Api);
  private router = inject(Router);
  session = inject(Session);
  readonly NONE = NO_LAYER;
  readonly OFF = OFF_CHAIN;
  /** Réglages portés par l'adresse (`?chaine=`, `?couche=`, `?objet=`, `?domaine=`). */
  chaine = input<string>();
  couche = input<string>();
  objet = input<string>();
  domaine = input<string>();

  grid = signal<Grid | null>(null);
  impact = signal<Impact | null>(null);
  error = signal('');
  meta = signal<any>(null);
  panel = signal<{ chainId: string; columnId: string } | null>(null);
  expanded = signal<Set<string>>(new Set());

  /** Couches du panneau d'étape : toutes celles que l'API sert, capacités comprises. */
  panelLayers = computed(() => this.grid()?.layers ?? ['capacites', 'processus', 'applications', 'projets']);
  layers = computed(() => choosableLayers(this.panelLayers()));
  layer = computed(() => (this.couche() && this.layers().includes(this.couche()!) ? this.couche()! : NO_LAYER));
  domain = computed(() => this.domaine() ?? null);
  empty = computed(() => emptyText(this.layer()));
  /** Recherche limitée aux serveurs, applications et projets (et sans serveurs pour le lecteur « métier »). */
  searchTypes = computed(() => {
    const m = this.meta();
    if (!m) return [];
    const servers = this.grid()?.serversAvailable ?? false;
    return (m.objectTypes as any[]).filter((t) => t.roles.includes('application') || t.roles.includes('projet')
      || (servers && (t.concept === 'Node' || t.concept === 'Device'))).map((t) => t.key);
  });
  touchedSteps = computed(() => stepsByChain(this.impact()?.layer ? this.impact()!.steps : []));
  touchedChains = computed(() => new Set((this.impact()?.steps ?? []).map((t) => t.chainId)));
  open = computed(() => {
    const g = this.grid();
    return g ? openChain(g.chains.map((c) => c.id), this.chaine(), this.touchedSteps(), g.offChain.length > 0) : null;
  });
  openedChain = computed(() => this.grid()?.chains.find((c) => c.id === this.open()) ?? null);
  touchedOff = computed(() => this.impact()?.offChain ?? []);
  offTabMark = computed(() => offMark(this.touchedOff().length));
  offSupport = computed(() => (this.grid()?.offChain ?? []).filter((p) => p.nature === 'support' || p.nature === 'pilotage'));
  offOther = computed(() => (this.grid()?.offChain ?? []).filter((p) => p.nature !== 'support' && p.nature !== 'pilotage'));
  /** Domaines présents à l'écran, dans l'ordre de la carte des capacités. */
  domainsShown = computed(() => {
    const g = this.grid();
    if (!g) return [];
    const present = new Set(Object.values(g.domainOf));
    return g.domains.filter((d) => present.has(d.id));
  });
  summary = computed(() => {
    const im = this.impact();
    return im ? touchedSummary(im.object.name, im.object.typeLabel, im.steps.length, this.touchedChains().size) : '';
  });

  constructor() {
    this.api.get('/metamodel').then((m) => this.meta.set(m)).catch(() => {});
    this.api.get('/chaines').then((g) => this.grid.set(g)).catch((e) => this.error.set((e as Error).message));
    effect(() => {
      const id = this.objet();
      if (!id) { this.impact.set(null); return; }
      this.api.get(`/chaines/impact/${encodeURIComponent(id)}`).then((r) => this.impact.set(r)).catch(() => this.impact.set(null));
    });
  }

  label(l: string): string { return LAYER_LABELS[l] ?? l; }
  via = viaText;
  names(items: StepItem[]): string { return items.map((i) => i.name).join(', '); }
  emptyOf(l: string): string { return emptyText(l).text; }
  mark(chainId: string): string | null { return tabMark(this.touchedSteps().get(chainId) ?? 0); }

  columnsOf(c: Chain): Column[] { return c.whole ? [...c.columns, c.whole] : c.columns; }
  lineOf(s: Column): CellLine { return cellLine(s.layers[this.layer()] ?? [], this.layer()); }

  private key(col: string, part: string): string { return `${col}|${this.layer()}|${part}`; }
  isExpanded(col: string, part: string): boolean { return this.expanded().has(this.key(col, part)); }
  directShown(col: string, items: StepItem[]): StepItem[] {
    return this.isExpanded(col, 'direct') ? items : items.slice(0, SHOWN_DIRECT);
  }
  moreDirect(col: string, count: number): string | null {
    return this.isExpanded(col, 'direct') ? null : moreDirectText(count);
  }
  expand(col: string, part: string): void { this.expanded.update((s) => new Set(s).add(this.key(col, part))); }

  private hue(cap: string | undefined | null): string | null {
    const g = this.grid();
    if (!g || !cap) return null;
    const top = g.domainOf[cap] ?? cap;
    return hueOf(g.domains.findIndex((d) => d.id === top));
  }
  fond(cap?: string | null): string | null { const h = this.hue(cap); return h ? `var(--dom-${h}-fond)` : null; }
  trait(cap?: string | null): string | null { const h = this.hue(cap); return h ? `var(--dom-${h}-trait)` : null; }
  texte(cap?: string | null): string | null { const h = this.hue(cap); return h ? `var(--dom-${h}-texte)` : null; }
  faded(cap?: string | null): boolean {
    const d = this.domain();
    if (!d || !cap) return false;
    return (this.grid()?.domainOf[cap] ?? cap) !== d;
  }

  touch(chainId: string, columnId: string): Touched | undefined {
    return this.impact()?.steps.find((t) => t.chainId === chainId && t.columnId === columnId);
  }
  isTouched(chainId: string, columnId: string): boolean { return !!this.touch(chainId, columnId); }

  panelOf(chainId: string): Column | null {
    const p = this.panel();
    if (!p || p.chainId !== chainId) return null;
    const c = this.grid()?.chains.find((x) => x.id === chainId);
    return c ? this.columnsOf(c).find((s) => s.id === p.columnId) ?? null : null;
  }
  openPanel(chainId: string, columnId: string): void {
    const p = this.panel();
    this.panel.set(p?.chainId === chainId && p.columnId === columnId ? null : { chainId, columnId });
  }

  /** Échap : ferme le panneau d'étape et rend le focus à son en-tête ; sans panneau, efface l'objet désigné. */
  onEscape(): void {
    const p = this.panel();
    if (p) {
      this.panel.set(null);
      setTimeout(() => document.getElementById('etape-' + p.columnId)?.focus());
    } else if (this.objet()) {
      this.designate(null);
    }
  }

  private go(params: Record<string, string | null>): void {
    this.router.navigate([], { queryParams: params, queryParamsHandling: 'merge', replaceUrl: true });
  }
  setChain(id: string): void { this.panel.set(null); this.go(chainQuery(id)); }
  setLayer(l: string): void { this.go(layerQuery(l)); }
  designate(id: string | null): void { this.panel.set(null); this.go(objectQuery(id)); }
  /** Clic sur un nom de la ligne : il désigne l'objet, par l'adresse (lien et non bouton : le « · » reste collé au nom). */
  designation = objectQuery;
  setDomain(id: string): void { this.go(domainQuery(this.domain(), id)); }
}
