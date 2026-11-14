import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Api } from '../api';

interface Backup {
  name: string;
  reason: string;
  at: string;
  size: number;
}

interface Status {
  schemaVersion: number;
  currentSchemaVersion: number;
  dataDirectory: string;
  files: { path: string; size: number }[];
  totalSize: number;
  backupCount: number;
  lastBackup: Backup | null;
  journalEntries: number;
  authMode: string;
  covered: string[];
}

const REASONS: Record<string, string> = {
  auto: 'Automatique (première écriture du jour)',
  manuelle: 'Manuelle',
  'import-archi': 'Avant un import Archi',
  'import-urbanisation': 'Avant une reprise du classeur d\'urbanisation',
  'import-metamodele': 'Avant un import de métamodèle',
  'matrice-reappliquee': 'Avant la réapplication de la matrice',
  'avant-restauration': 'Précaution avant une restauration',
  'remise-a-zero': 'Avant une remise à zéro',
};

/** État du stockage et sauvegardes : liste, sauvegarde immédiate, téléchargement, restauration avec confirmation dans la page. */
@Component({
  imports: [DatePipe],
  template: `
    <h1>Sauvegardes</h1>
    <p class="lead">Le référentiel et son journal sont sauvegardés automatiquement à la première écriture de chaque jour et avant chaque opération risquée.</p>
    @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
    @if (message()) { <div class="note" role="status">{{ message() }}</div> }

    @if (status(); as s) {
      <div class="stats">
        <div class="stat"><b>{{ s.backupCount }}</b><span>sauvegarde(s) conservée(s)</span></div>
        <div class="stat"><b class="date">{{ s.lastBackup ? (s.lastBackup.at | date: 'dd/MM HH:mm') : 'aucune' }}</b><span>dernière sauvegarde</span></div>
        <div class="stat"><b>{{ s.journalEntries }}</b><span>entrées du journal</span></div>
        <div class="stat"><b>{{ s.schemaVersion }}</b><span>version de schéma</span></div>
        <div class="stat"><b class="date">{{ s.authMode }}</b><span>authentification</span></div>
      </div>
    }

    <div class="card flush">
      <div class="tools">
        <button type="button" [disabled]="busy()" (click)="backupNow()">Sauvegarder maintenant</button>
        <span class="muted small">Heures affichées dans votre fuseau horaire.</span>
      </div>
      @if (restoring(); as name) {
        <div class="confirm" role="alertdialog" aria-label="Confirmer la restauration">
          <b>Restaurer {{ name }} ?</b>
          <p>Le référentiel actuel sera remplacé par le contenu de cette sauvegarde. Une sauvegarde de précaution de l'état actuel est prise juste avant, et le journal est conservé en entier : la restauration y sera tracée.</p>
          <button type="button" [disabled]="busy()" (click)="restore(name)">Oui, restaurer</button>
          <button type="button" class="ghost" (click)="restoring.set(null)">Annuler</button>
        </div>
      }
      <div class="scroll">
        <table>
          <tr><th>Date</th><th>Motif</th><th class="num">Taille</th><th></th></tr>
          @for (b of backups(); track b.name) {
            <tr>
              <td class="nowrap">{{ b.at | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
              <td>{{ reasonLabel(b.reason) }}<div class="muted small">{{ b.name }}</div></td>
              <td class="num nowrap">{{ size(b.size) }}</td>
              <td class="actions">
                <a class="btn ghost" [href]="api.url('/admin/backups/' + b.name)" download>Télécharger</a>
                <button type="button" class="ghost" style="margin-left: 6px" [disabled]="busy()" (click)="restoring.set(b.name)">Restaurer…</button>
              </td>
            </tr>
          }
          @if (backups().length === 0) { <tr><td colspan="4" class="muted">Aucune sauvegarde pour l'instant.</td></tr> }
        </table>
      </div>
    </div>

    @if (status(); as s) {
      <div class="card">
        <h2>Fichiers du dossier de données</h2>
        <p class="muted small">Dossier : <code>{{ s.dataDirectory }}</code>. Les sauvegardes ne couvrent que le référentiel et le journal : le reste est à sauvegarder par vos propres moyens.</p>
        <table>
          <tr><th>Fichier</th><th class="num">Taille</th><th>Sauvegarde</th></tr>
          @for (f of s.files; track f.path) {
            <tr>
              <td><code>{{ f.path }}</code></td>
              <td class="num nowrap">{{ size(f.size) }}</td>
              <td>
                @if (s.covered.includes(f.path)) { <span class="chip ok">Couvert par les sauvegardes</span> }
                @else if (f.path.startsWith('sauvegardes/')) { <span class="muted small">Copie de sauvegarde</span> }
                @else if (f.path.includes('.avant-migration-')) { <span class="muted small">Original conservé avant une migration de format</span> }
                @else { <span class="chip warn">À sauvegarder par vos soins</span> }
              </td>
            </tr>
          }
        </table>
      </div>
    }
  `,
  styles: `
    .nowrap { white-space: nowrap; }
    .stat b.date { font-size: 18px; line-height: 1.6; }
    .confirm { padding: 12px 16px; background: #fffaf0; border-bottom: 1px solid var(--line); }
    .confirm p { margin: 4px 0 10px; max-width: 720px; }
    .confirm button + button { margin-left: 8px; }
    code { font-size: 12px; overflow-wrap: anywhere; }
  `,
})
export class BackupsPage {
  api = inject(Api);

  backups = signal<Backup[]>([]);
  status = signal<Status | null>(null);
  error = signal('');
  message = signal('');
  busy = signal(false);
  restoring = signal<string | null>(null);

  constructor() {
    this.load();
  }

  reasonLabel(reason: string): string {
    return REASONS[reason] ?? reason;
  }

  size(bytes: number): string {
    if (bytes < 1024) return `${bytes} o`;
    return bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1).replace('.', ',')} Ko` : `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} Mo`;
  }

  async load(): Promise<void> {
    try {
      const [backups, status] = await Promise.all([this.api.get<Backup[]>('/admin/backups'), this.api.get<Status>('/admin/status')]);
      this.backups.set(backups);
      this.status.set(status);
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  backupNow(): Promise<void> {
    return this.run(async () => {
      const b = await this.api.post<Backup>('/admin/backups', {});
      this.message.set(`Sauvegarde créée : ${b.name}.`);
    });
  }

  restore(name: string): Promise<void> {
    return this.run(async () => {
      await this.api.post(`/admin/backups/${name}/restore`, {});
      this.restoring.set(null);
      this.message.set(`Sauvegarde ${name} restaurée. Une sauvegarde de précaution de l'état précédent a été prise.`);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await action();
      await this.load();
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
