import { Component, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { SourceState } from './model';

/** Connexion d'un profil API REST. Le secret n'est jamais relu du serveur : on sait seulement s'il est défini. */
@Component({
  selector: 'app-connection-tab',
  template: `
    @if (state.profile(); as p) {
      @if (error()) { <div class="error" role="alert">{{ error() }}</div> }
      <form class="card form" (submit)="save(); $event.preventDefault()">
        <h2>Accès à l'API</h2>
        <p class="muted small intro">Lecture seule : l'outil interroge l'API et n'y écrit rien. Les réglages dépendent de l'API (voir sa documentation) ;
          l'aperçu de l'onglet Correspondances montre ce qui est réellement reçu.</p>
        <div class="grid">
          <label class="wide">Adresse de la ressource <span class="muted small">(sans paramètres)</span>
            <input type="text" [value]="v('url')" (input)="set('url', $event)" placeholder="https://cmdb.exemple.fr/api/elements" autocomplete="off" />
          </label>
          <label class="wide">Paramètres de requête <span class="muted small">(facultatif, ex. fields=nom,site&amp;statut=actif)</span>
            <input type="text" [value]="v('query')" (input)="set('query', $event)" autocomplete="off" />
          </label>
          <label>Taille de page
            <input type="text" inputmode="numeric" [value]="v('pageSize')" (input)="set('pageSize', $event)" autocomplete="off" />
          </label>
          <label>Paramètre de taille de page <span class="muted small">(défaut : limit)</span>
            <input type="text" [value]="v('pageSizeParam')" (input)="set('pageSizeParam', $event)" autocomplete="off" />
          </label>
          <label>Paramètre de décalage <span class="muted small">(défaut : offset)</span>
            <input type="text" [value]="v('offsetParam')" (input)="set('offsetParam', $event)" autocomplete="off" />
          </label>
          <label>Champ des éléments <span class="muted small">(vide : deviné)</span>
            <input type="text" [value]="v('itemsField')" (input)="set('itemsField', $event)" autocomplete="off" />
          </label>
          <label>Champ du total <span class="muted small">(facultatif)</span>
            <input type="text" [value]="v('totalField')" (input)="set('totalField', $event)" autocomplete="off" />
          </label>
          <label>Champ du lien de page suivante <span class="muted small">(facultatif)</span>
            <input type="text" [value]="v('nextField')" (input)="set('nextField', $event)" autocomplete="off" />
          </label>
          <label>Champ identifiant <span class="muted small">(valeur ou lien ; donne la colonne @id)</span>
            <input type="text" [value]="v('idField')" (input)="set('idField', $event)" autocomplete="off" />
          </label>
          <label>Mode d'authentification
            <select (change)="set('authMode', $event)">
              <option value="basic" [selected]="mode() === 'basic'">Identifiant et mot de passe</option>
              <option value="bearer" [selected]="mode() === 'bearer'">Jeton</option>
            </select>
          </label>
          @if (mode() === 'basic') {
            <label>Identifiant
              <input type="text" [value]="v('login')" (input)="set('login', $event)" autocomplete="off" />
            </label>
          }
        </div>

        <fieldset class="secret">
          <legend>{{ mode() === 'basic' ? 'Mot de passe' : 'Jeton' }}</legend>
          <div class="tools">
            @if (secretAction() === 'clear') { <span class="chip warn">Sera effacé à l'enregistrement</span> }
            @else if (p.hasSecret && secretAction() === 'keep') { <span class="chip ok">Défini</span> }
            @else if (!p.hasSecret && secretAction() === 'keep') { <span class="chip">Non défini</span> }
            @if (p.hasSecret && secretAction() === 'keep') {
              <button type="button" class="ghost" (click)="secretAction.set('replace')">Remplacer</button>
              <button type="button" class="ghost" (click)="secretAction.set('clear')">Effacer</button>
            }
            @if (secretAction() === 'clear') { <button type="button" class="ghost" (click)="secretAction.set('keep')">Annuler l'effacement</button> }
          </div>
          @if (secretAction() === 'replace' || !p.hasSecret) {
            <label class="secret-input">{{ p.hasSecret ? 'Nouveau secret' : 'Secret' }}
              <input type="password" [value]="secret()" (input)="secret.set($any($event.target).value)" autocomplete="new-password" />
            </label>
            @if (p.hasSecret) { <button type="button" class="ghost small-btn" (click)="secretAction.set('keep'); secret.set('')">Garder le secret actuel</button> }
          }
          <p class="muted small">Le secret est chiffré au repos et n'est jamais affiché ni renvoyé par l'outil.</p>
        </fieldset>

        <div class="tools">
          <button type="submit" [disabled]="busy() || !canSave()">Enregistrer la connexion</button>
          @if (state.connectionDirty() || secretAction() !== 'keep' || secret()) { <span class="chip warn">Modifications non enregistrées</span> }
          @else if (saved()) { <span class="chip ok">Enregistré</span> }
        </div>
      </form>

      <div class="card">
        <h2>Test de connexion</h2>
        <p class="muted small">Le test utilise la connexion enregistrée. Il lit au plus une page, sans rien modifier.</p>
        <div class="tools">
          <button type="button" class="ghost" [disabled]="testing() || !canTest()" (click)="test()">Tester la connexion</button>
          @if (!canTest()) { <span class="muted small">Enregistrez d'abord la connexion.</span> }
        </div>
        @if (result(); as r) {
          <div [class]="r.ok ? 'note' : 'error'" role="status">{{ r.message }}</div>
        }
      </div>
    }
  `,
  styles: `
    .intro { margin: -4px 0 4px; max-width: 80ch; }
    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px 16px; max-width: 860px; }
    .grid .wide { grid-column: 1 / -1; }
    .grid input, .grid select { width: 100%; }
    fieldset.secret { border: 1px solid var(--line); border-radius: 8px; padding: 10px 14px 4px; max-width: 860px; }
    fieldset.secret legend { font-weight: 600; padding: 0 6px; }
    .secret-input { max-width: 420px; }
    .small-btn { align-self: flex-start; }
    .tools { margin-bottom: 8px; }
    @media (max-width: 760px) { .grid { grid-template-columns: 1fr; } }
  `,
})
export class ConnectionTab {
  readonly state = inject(SourceState);
  private api = inject(Api);
  secretAction = signal<'keep' | 'replace' | 'clear'>('keep');
  secret = signal('');
  busy = signal(false);
  testing = signal(false);
  saved = signal(false);
  error = signal('');
  result = signal<{ ok: boolean; message: string } | null>(null);

  v(key: string): string {
    return this.state.connection()[key] ?? '';
  }

  mode(): string {
    return this.v('authMode') || 'basic';
  }

  set(key: string, event: Event): void {
    this.saved.set(false);
    this.state.connection.update((c) => ({ ...c, [key]: (event.target as HTMLInputElement | HTMLSelectElement).value }));
  }

  canSave(): boolean {
    return this.state.connectionDirty() || this.secretAction() === 'clear' || !!this.secret();
  }

  canTest(): boolean {
    return !this.state.connectionDirty() && this.secretAction() === 'keep' && !this.secret();
  }

  async save(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const action = this.secretAction();
      const secret = action === 'clear' ? '' : this.secret() ? this.secret() : undefined;
      await this.state.save('connection', secret);
      this.secret.set('');
      this.secretAction.set('keep');
      this.saved.set(true);
      this.result.set(null);
    } catch (e) {
      this.error.set((e as Error).message);
    } finally {
      this.busy.set(false);
    }
  }

  async test(): Promise<void> {
    this.testing.set(true);
    this.error.set('');
    this.result.set(null);
    try {
      this.result.set(await this.api.post(`/sources/${this.state.profile()!.key}/test`, {}));
    } catch (e) {
      this.result.set({ ok: false, message: (e as Error).message });
    } finally {
      this.testing.set(false);
    }
  }
}
