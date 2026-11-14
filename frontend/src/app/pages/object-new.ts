import { Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api } from '../api';
import { ObjectForm } from './object-form';

/** Création d'un objet dans l'outil, guidée par le métamodèle. Le type peut être présélectionné par l'adresse (?type=…). */
@Component({
  selector: 'app-object-new',
  imports: [RouterLink, ObjectForm],
  template: `
    <a routerLink="/referentiel">← Tous les objets</a>
    <h1>Nouvel objet</h1>
    <p class="lead">L'objet est créé dans l'outil : il en est le maître et reste entièrement modifiable. Le type détermine les propriétés proposées.</p>
    <div class="card">
      @if (meta(); as m) {
        <app-object-form [meta]="m" [presetType]="type()" (saved)="open($event)" (cancelled)="cancel()" />
      } @else {
        <p class="muted">Chargement du métamodèle…</p>
      }
    </div>
  `,
})
export class ObjectNewPage {
  private router = inject(Router);
  /** Clé du type présélectionné, transmise par l'adresse. */
  type = input<string>();
  meta = signal<any>(null);

  constructor() {
    inject(Api).get('/metamodel').then((m) => this.meta.set(m));
  }

  open(id: string): void {
    this.router.navigate(['/objets', id]);
  }

  cancel(): void {
    history.length > 1 ? history.back() : this.router.navigate(['/referentiel']);
  }
}
