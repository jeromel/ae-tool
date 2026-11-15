import { Injectable } from '@angular/core';

/** Message affiché quand le serveur refuse une action faute de rôle suffisant. */
const DENIED = 'Votre rôle ne permet pas cette action.';

/** Erreur renvoyée par le serveur : le message, et la liste des problèmes quand le serveur en détaille (valeurs de propriétés invalides). */
export class ApiError extends Error {
  constructor(message: string, readonly problems: string[] = []) {
    super(message);
  }
}

/** Accès à l'API REST. Volontairement basé sur fetch : aucune bibliothèque tierce. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly base = '/api';
  /** Jeton CSRF de la session courante, renvoyé sur chaque écriture. Renseigné par `Session`. */
  csrfToken: string | null = null;
  /** Appelé quand le serveur répond 401 hors routes d'authentification (session expirée). Renseigné par `Session`. */
  onUnauthorized: (() => void) | null = null;
  /**
   * État du SI regardé : null pour l'existant, sinon une étape d'un scénario (« scenario:etape »). Gardé pour la durée de
   * l'onglet ; toute lecture le transmet au serveur, qui répond alors sur le SI projeté, en lecture seule.
   */
  readonly etat: { scenarioId: string; stepId: string; label: string } | null = readEtat();

  get<T = any>(path: string): Promise<T> {
    return this.send<T>('GET', path);
  }

  put<T = any>(path: string, body: unknown): Promise<T> {
    return this.send<T>('PUT', path, JSON.stringify(body), 'application/json');
  }

  delete<T = any>(path: string): Promise<T> {
    return this.send<T>('DELETE', path);
  }

  post<T = any>(path: string, body: unknown): Promise<T> {
    return this.send<T>('POST', path, JSON.stringify(body), 'application/json');
  }

  /** Envoie un fichier tel quel (XML Archi, classeur Excel, métamodèle JSON). */
  upload<T = any>(path: string, file: File): Promise<T> {
    return this.send<T>('POST', path, file, 'application/octet-stream');
  }

  url(path: string): string {
    return this.base + path;
  }

  private withEtat(method: string, path: string): string {
    if (!this.etat || method !== 'GET' || /^\/(scenarios|trajectoires|auth|users|admin|journal|metamodel|import|export|sources|validation)\b/.test(path)) return path;
    return path + (path.includes('?') ? '&' : '?') + 'etat=' + encodeURIComponent(this.etat.scenarioId + ':' + this.etat.stepId);
  }

  private async send<T>(method: string, path: string, body?: BodyInit, contentType?: string): Promise<T> {
    const headers: Record<string, string> = {};
    if (contentType) headers['Content-Type'] = contentType;
    if (method !== 'GET' && this.csrfToken) headers['X-CSRF-TOKEN'] = this.csrfToken;
    const response = await fetch(this.base + this.withEtat(method, path), { method, body, headers });
    if (!response.ok) {
      let message = response.status === 403 ? DENIED : `Erreur ${response.status}`;
      let problems: string[] = [];
      try {
        const body = await response.json();
        message = body.error ?? message;
        // Deux noms selon les routes : « problems » (valeurs de propriétés) et « errors » (matrice de transcodification).
        const details = body.problems ?? body.errors;
        problems = Array.isArray(details) ? details : [];
      } catch {
        /* réponse sans corps JSON */
      }
      // Un 401 sur la connexion elle-même est un échec d'identifiants, pas une session expirée.
      if (response.status === 401 && !path.startsWith('/auth/')) this.onUnauthorized?.();
      throw new ApiError(message, problems);
    }
    return response.status === 204 ? (undefined as T) : response.json();
  }
}

export interface Brief {
  id: string;
  name: string;
  typeKey: string;
  typeLabel: string;
  layer: string;
  layerLabel: string;
  source: string;
  status: string;
  owner: string | null;
  mark?: string | null;
}

export const SOURCE_LABELS: Record<string, string> = { archi: 'Archi', urba: 'Classeur urbanisation', saisie: 'Saisie', drawio: 'draw.io' };

const ETAT_KEY = 'ae.etat';

function readEtat(): { scenarioId: string; stepId: string; label: string } | null {
  try {
    const raw = sessionStorage.getItem(ETAT_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

/** Change l'état du SI regardé puis recharge l'interface : chaque écran relit alors ses données dans ce nouvel état. */
export function enterEtat(etat: { scenarioId: string; stepId: string; label: string } | null, goto?: string): void {
  try {
    if (etat) sessionStorage.setItem(ETAT_KEY, JSON.stringify(etat));
    else sessionStorage.removeItem(ETAT_KEY);
  } catch {
    /* stockage indisponible : l'état ne survivra pas au rechargement */
  }
  if (goto) location.assign(goto);
  else location.reload();
}
