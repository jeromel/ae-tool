import { Injectable } from '@angular/core';

/** Accès à l'API REST. Volontairement basé sur fetch : aucune bibliothèque tierce. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly base = '/api';

  get<T = any>(path: string): Promise<T> {
    return this.send<T>('GET', path);
  }

  put<T = any>(path: string, body: unknown): Promise<T> {
    return this.send<T>('PUT', path, JSON.stringify(body), 'application/json');
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

  private async send<T>(method: string, path: string, body?: BodyInit, contentType?: string): Promise<T> {
    const response = await fetch(this.base + path, {
      method,
      body,
      headers: contentType ? { 'Content-Type': contentType } : undefined,
    });
    if (!response.ok) {
      let message = `Erreur ${response.status}`;
      try {
        message = (await response.json()).error ?? message;
      } catch {
        /* réponse sans corps JSON */
      }
      throw new Error(message);
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
}

export const SOURCE_LABELS: Record<string, string> = { archi: 'Archi', urba: 'Classeur urbanisation', saisie: 'Saisie' };
