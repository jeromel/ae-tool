import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Api } from '../api';

@Component({
  imports: [DatePipe, RouterLink],
  template: `
    <h1>Imports et exports</h1>
    <p class="lead">Les imports sont rejouables : le même fichier importé deux fois ne crée aucun doublon. Les anomalies sont signalées, jamais corrigées en silence.</p>
    @if (error()) { <div class="error">{{ error() }}</div> }
    <div class="row">
      <div class="card">
        <h2>Modèle Archi</h2>
        <p class="muted small">Dans Archi : Fichier › Exporter › Modèle au format d'échange Open Group (.xml).</p>
        <label class="btn">Choisir le fichier XML<input type="file" accept=".xml" hidden (change)="run('/import/archimate', $event)" /></label>
      </div>
      <div class="card">
        <h2>Classeur d'urbanisation</h2>
        <p class="muted small">Classeur Excel : référentiel consolidé, grille des preuves, preuves de notation. À importer après le modèle Archi pour le rapprochement par nom.</p>
        <label class="btn">Choisir le fichier XLSX<input type="file" accept=".xlsx" hidden (change)="run('/import/urba', $event)" /></label>
      </div>
      <div class="card">
        <h2>Dessin draw.io</h2>
        <p class="muted small">Chaque page devient une vue ; les formes sont rapprochées des objets du référentiel, forme par forme, avant tout import.</p>
        <a class="btn" routerLink="/import-drawio">Importer un dessin</a>
      </div>
      <div class="card">
        <h2>Exports</h2>
        <p><a [href]="api.url('/export/archimate')">Référentiel au format d'échange ArchiMate</a> <span class="muted small">(réimportable dans Archi)</span></p>
        <p><a [href]="api.url('/export/json')">Référentiel complet en JSON</a> <span class="muted small">(réversibilité : tout l'état de l'outil)</span></p>
        <p><a [href]="api.url('/export/objects.csv')">Objets et propriétés en CSV</a></p>
        <p style="margin-bottom: 0"><a [href]="api.url('/metamodel/export')">Métamodèle en JSON</a></p>
      </div>
    </div>
    @if (busy()) { <div class="note">Import en cours…</div> }
    @for (r of reports(); track $index) {
      <div class="card">
        <h2>{{ r.source }} <span class="muted small">· {{ r.at | date: 'dd/MM/yyyy HH:mm' }}</span></h2>
        <table style="max-width: 760px">
          <tr><th>Catégorie</th><th class="num">Ajoutés</th><th class="num">Modifiés</th><th class="num">Inchangés</th><th class="num">Archivés</th></tr>
          @for (c of entries(r.counts); track c[0]) {
            <tr><td>{{ c[0] }}</td><td class="num">{{ c[1].added }}</td><td class="num">{{ c[1].updated }}</td><td class="num">{{ c[1].unchanged }}</td><td class="num">{{ c[1].archived }}</td></tr>
          }
        </table>
        @for (n of r.notes.slice(0, 6); track $index) { <div class="small muted" style="margin-top: 6px">{{ n }}</div> }
        @if (r.notes.length > 6) { <div class="small muted">… et {{ r.notes.length - 6 }} autre(s) note(s).</div> }
        <h2 style="margin-top: 14px">{{ r.anomalies.length }} anomalie(s)</h2>
        <div class="scroll" style="max-height: 260px">
          <table>
            @for (a of r.anomalies; track $index) {
              <tr><td style="width: 110px"><span class="chip" [class.bad]="a.severity === 'erreur'" [class.warn]="a.severity !== 'erreur'">{{ a.severity }}</span></td>
                <td style="width: 190px">{{ a.code }}</td><td>{{ a.message }}</td><td class="muted small">{{ a.ref }}</td></tr>
            }
          </table>
        </div>
      </div>
    }
    <div class="card">
      <div class="tools">
        <h2 style="margin: 0">Dernières opérations d'import</h2>
        <a routerLink="/journal">Tout le journal</a>
      </div>
      <div class="scroll" style="max-height: 300px">
        <table>
          @for (j of journal(); track $index) {
            <tr><td class="muted" style="width: 120px">{{ j.at | date: 'dd/MM HH:mm:ss' }}</td><td style="width: 110px">{{ j.user }}</td><td style="width: 220px">{{ j.action }}</td>
              <td class="small muted" style="overflow-wrap: anywhere">{{ (j.detail ?? '').slice(0, 220) }}</td></tr>
          } @empty { <tr><td class="muted">Aucun import pour l'instant.</td></tr> }
        </table>
      </div>
    </div>
  `,
})
export class ImportsPage {
  api = inject(Api);
  reports = signal<any[]>([]);
  journal = signal<any[]>([]);
  busy = signal(false);
  error = signal('');

  constructor() {
    this.refresh();
  }

  refresh(): void {
    this.api.get('/import/reports').then((r) => this.reports.set(r));
    // Le journal est paginé et filtrable : seules les opérations d'import intéressent cette page.
    this.api.get('/journal?kind=import&limit=30').then((j) => this.journal.set(j.items));
  }

  entries(counts: Record<string, any>): [string, any][] {
    return Object.entries(counts);
  }

  async run(path: string, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.upload(path, file);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      input.value = '';
      this.busy.set(false);
      this.refresh();
    }
  }
}
