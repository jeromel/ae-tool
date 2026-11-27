import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { ScopeNote } from '../scope-note';
import { Session } from '../session';

@Component({
  imports: [RouterLink, ScopeNote],
  template: `
    <h1>Projets et adhérences</h1>
    <p class="lead">
      Un projet est un objet du référentiel. Ses impacts sont d'abord déduits des vues rangées dans son dossier, puis qualifiés par le chef
      de projet. Deux projets sont adhérents s'ils touchent le même objet ou deux objets directement reliés.
    </p>
    <!-- Lecteur « métier » : le nombre ne change pas, c'est ce qu'il compte qui est nommé (Camille, 27/11, §2 ; décision 012). -->
    @if (session.business()) { <app-scope-note text="Impacts sur l'infrastructure non affichés pour votre accès." /> }
    <div class="card">
      <table>
        <tr><th>Projet</th><th>Statut</th><th class="num">Vues</th><th class="num">{{ session.business() ? 'Impacts métier et applicatifs' : 'Objets impactés' }}</th><th class="num">À qualifier</th></tr>
        @for (p of projects(); track p.id) {
          <tr class="click" (click)="open(p.id)">
            <td><a [routerLink]="['/projets', p.id]">{{ p.name }}</a></td><td>{{ p.status }}</td>
            <td class="num">{{ p.diagrams }}</td><td class="num">{{ p.impacts }}</td>
            <td class="num">@if (p.toQualify) { <span class="chip warn">{{ p.toQualify }}</span> } @else { 0 }</td>
          </tr>
        }
      </table>
    </div>
    <div class="card">
      <h2>Matrice des adhérences</h2>
      <p class="muted small">Nombre d'objets partagés (+ couples d'objets reliés). Cliquer une case pour le détail. Les impacts « Hors périmètre » sont ignorés.</p>
      <div class="scroll matrix">
        <table>
          <tr><th></th>@for (p of withImpacts(); track p.id) { <th class="col"><div>{{ p.name }}</div></th> }</tr>
          @for (a of withImpacts(); track a.id) {
            <tr>
              <th class="rowh">{{ a.name }}</th>
              @for (b of withImpacts(); track b.id) {
                @if (pair(a.id, b.id); as x) {
                  <td class="cell" [class.r]="x.shared.length > 3" [class.v]="x.shared.length <= 3" style="cursor: pointer" (click)="selected.set({ a, b, x })">
                    {{ x.shared.length }}@if (x.linked.length) {+{{ x.linked.length }}}
                  </td>
                } @else { <td class="cell">{{ a.id === b.id ? '·' : '' }}</td> }
              }
            </tr>
          }
        </table>
      </div>
      @if (selected(); as s) {
        <div style="margin-top: 14px">
          <b>{{ s.a.name }} ↔ {{ s.b.name }}</b>
          <div class="row" style="margin-top: 6px">
            <div style="flex: 1">
              <span class="muted">Objets partagés</span>
              @for (o of s.x.shared; track o.id) { <div><a [routerLink]="['/objets', o.id]">{{ o.name }}</a> <span class="muted small">{{ o.typeLabel }}</span></div> }
              @empty { <div class="muted">Aucun.</div> }
            </div>
            <div style="flex: 1">
              <span class="muted">Objets directement reliés</span>
              @for (l of s.x.linked; track $index) { <div>{{ l[0] }} ↔ {{ l[1] }}</div> } @empty { <div class="muted">Aucun.</div> }
            </div>
          </div>
        </div>
      }
    </div>
  `,
})
export class ProjectsPage {
  private api = inject(Api);
  private router = inject(Router);
  projects = signal<any[]>([]);
  pairs = signal<any[]>([]);
  selected = signal<any>(null);
  readonly session = inject(Session);
  withImpacts = computed(() => this.projects().filter((p) => p.impacts > 0));

  constructor() {
    this.api.get('/projects').then((p) => this.projects.set(p));
    this.api.get('/projects/adherences').then((a) => this.pairs.set(a.pairs));
  }

  pair(a: string, b: string): any {
    return this.pairs().find((p) => (p.projectA === a && p.projectB === b) || (p.projectA === b && p.projectB === a));
  }

  open(id: string): void {
    this.router.navigate(['/projets', id]);
  }
}
