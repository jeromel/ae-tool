import { Component, effect, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { DiagramView } from '../diagram-view';

@Component({
  imports: [RouterLink, NgTemplateOutlet, DiagramView],
  template: `
    <h1>Vues</h1>
    <p class="lead">Les vues sont des projections du référentiel. L'arborescence est celle des dossiers d'Archi ; cliquer un élément ouvre sa fiche.</p>
    <div class="split">
      <div class="left card tree">
        <ng-template #folder let-f>
          <details [open]="isOpen(f)">
            <summary>{{ f.label || '(racine)' }} <span class="muted small">{{ count(f) }}</span></summary>
            @for (d of f.diagrams; track d.id) { <a [routerLink]="['/vues', d.id]" [class.on]="d.id === id()">{{ d.name }}</a> }
            @for (c of f.children; track $index) { <ng-container [ngTemplateOutlet]="folder" [ngTemplateOutletContext]="{ $implicit: c }" /> }
          </details>
        </ng-template>
        @for (f of tree()?.folders ?? []; track $index) {
          @if (count(f) > 0) { <ng-container [ngTemplateOutlet]="folder" [ngTemplateOutletContext]="{ $implicit: f }" /> }
        }
        @for (d of tree()?.unfiled ?? []; track d.id) { <a [routerLink]="['/vues', d.id]" [class.on]="d.id === id()">{{ d.name }}</a> }
      </div>
      <div class="main">
        @if (diagram(); as d) {
          <div class="card">
            <h2>{{ d.name }}</h2>
            @if (d.documentation) { <p class="muted">{{ d.documentation }}</p> }
            <app-diagram-view [diagram]="d" (openObject)="go(['/objets', $event])" (openView)="go(['/vues', $event])">
              <label class="right">Couleurs
                <select (change)="mode.set($any($event.target).value)">
                  <option value="diagramme">de la vue (Archi)</option>
                  <option value="metamodele">du métamodèle (type, puis élément)</option>
                </select>
              </label>
              <a class="btn ghost" [href]="api.url('/export/archimate?diagram=' + d.id)">Exporter cette vue vers Archi</a>
            </app-diagram-view>
            <p class="muted small" style="margin: 8px 0 0">{{ d.nodes.length }} occurrence(s) graphique(s), {{ d.connections.length }} connexion(s).</p>
          </div>
        } @else {
          <div class="card muted">Choisir une vue dans l'arborescence.</div>
        }
      </div>
    </div>
  `,
})
export class ViewsPage {
  api = inject(Api);
  private router = inject(Router);
  id = input<string>();
  tree = signal<any>(null);
  diagram = signal<any>(null);
  mode = signal('diagramme');

  constructor() {
    this.api.get('/folders').then((t) => this.tree.set(t));
    effect(() => {
      const id = this.id();
      const mode = this.mode();
      if (!id) return this.diagram.set(null);
      this.api.get(`/diagrams/${id}?visuel=${mode}`).then((d) => this.diagram.set(d));
    });
  }

  /** Nombre de vues dans le dossier, sous-dossiers compris. */
  count(f: any): number {
    return f.diagrams.length + f.children.reduce((sum: number, c: any) => sum + this.count(c), 0);
  }

  isOpen(f: any): boolean {
    const id = this.id();
    const contains = (x: any): boolean => x.diagrams.some((d: any) => d.id === id) || x.children.some(contains);
    return this.count(f) <= 12 || contains(f);
  }

  go(commands: unknown[]): void {
    this.router.navigate(commands);
  }
}
