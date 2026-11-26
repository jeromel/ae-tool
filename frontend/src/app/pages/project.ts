import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Session } from '../session';
import { RouterLink } from '@angular/router';
import { Api } from '../api';
import { ProjectTrajectories } from './project-trajectories';

/** Fiche projet : vues du dossier projet et objets impactés par couche, avec la nature de chaque impact. */
@Component({
  imports: [RouterLink, ProjectTrajectories],
  template: `
    @if (d(); as d) {
      <a routerLink="/projets">← Projets</a>
      <h1>{{ d.project.name }}</h1>
      <p class="lead">
        @for (p of d.properties; track $index) { <span class="chip" style="margin-right: 6px">{{ p.label }} : {{ p.value }}</span> }
        <a [routerLink]="['/objets', d.project.id]">Fiche de l'objet</a>
      </p>
      @if (d.documentation) { <div class="card" style="white-space: pre-wrap">{{ d.documentation }}</div> }
      <!-- Scénarios et trajectoires : lus sans le filtre de périmètre, fermés à un accès sans l'infrastructure (décision 012). -->
      @if (!session.business()) { <app-project-trajectories [projectId]="id()" [projectName]="d.project.name" /> }
      <div class="card">
        <h2>Vues du projet ({{ d.diagrams.length }})</h2>
        @for (v of d.diagrams; track v.id) { <div><a [routerLink]="['/vues', v.id]">{{ v.name }}</a></div> }
        @empty { <span class="muted">Aucune vue rangée dans le dossier de ce projet.</span> }
      </div>
      <div class="card">
        <div class="tools">
          <h2 style="margin: 0">Objets impactés ({{ d.impacts.length }})</h2>
          <span class="chip warn">{{ toQualify() }} à qualifier</span>
          <select (change)="layer.set($any($event.target).value)">
            <option value="">Toutes les couches</option>
            @for (l of layers(); track l) { <option>{{ l }}</option> }
          </select>
          @if (error()) { <span class="error" style="margin: 0">{{ error() }}</span> }
        </div>
        <p class="muted small">
          « vue » : impact déduit de la présence de l'objet dans une vue du projet. Qualifier la nature, ou choisir « Hors périmètre » si l'objet
          n'est là que pour le contexte : il sort alors du calcul des adhérences.
        </p>
        <div class="scroll">
          <table>
            <tr><th>Couche</th><th>Objet</th><th>Type</th><th>Origine</th><th>Nature de l'impact</th></tr>
            @for (i of shown(); track i.object.id) {
              <tr>
                <td>{{ i.object.layerLabel }}</td>
                <td><a [routerLink]="['/objets', i.object.id]">{{ i.object.name }}</a></td>
                <td>{{ i.object.typeLabel }}</td>
                <td>{{ i.origin }} @if (i.views > 1) { <span class="muted small">({{ i.views }} vues)</span> }</td>
                <td>
                  <select (change)="setNature(i, $any($event.target).value)">
                    @for (n of d.natures; track n) { <option [selected]="i.nature === n">{{ n }}</option> }
                  </select>
                </td>
              </tr>
            }
          </table>
        </div>
      </div>
    }
  `,
})
export class ProjectPage {
  private api = inject(Api);
  readonly session = inject(Session);
  id = input.required<string>();
  d = signal<any>(null);
  layer = signal('');
  error = signal('');

  layers = computed(() => [...new Set<string>((this.d()?.impacts ?? []).map((i: any) => i.object.layerLabel))]);
  shown = computed(() => (this.d()?.impacts ?? []).filter((i: any) => !this.layer() || i.object.layerLabel === this.layer()));
  toQualify = computed(() => (this.d()?.impacts ?? []).filter((i: any) => i.nature === 'À qualifier').length);

  constructor() {
    effect(() => {
      this.api.get(`/projects/${this.id()}`).then((d) => this.d.set(d));
    });
  }

  async setNature(impact: any, nature: string): Promise<void> {
    this.error.set('');
    try {
      await this.api.put(`/projects/${this.id()}/impacts/${impact.object.id}`, { nature, comment: impact.comment });
      impact.nature = nature;
      this.d.set({ ...this.d() });
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }
}
