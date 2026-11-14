import { Injectable, computed, inject, signal } from '@angular/core';
import { Api, ApiError } from '../../api';

export interface Usage {
  objectTypes: Record<string, number>;
  relationshipTypes: Record<string, number>;
  properties: Record<string, number>;
}

/** Retire les accents et la casse : sert à la recherche et à la dérivation d'une clé de couche. */
export function plain(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

/** Message d'une erreur du serveur, suivi de son détail quand il en donne (règles de la matrice en défaut, par exemple). */
function detailed(e: unknown): string {
  const problems = e instanceof ApiError ? e.problems : [];
  return [(e as Error).message, ...problems].join(' ');
}

/**
 * État partagé des onglets du métamodèle : le métamodèle lui-même, le catalogue (rôles, concepts ArchiMate, listes de choix)
 * et les effectifs. Il porte aussi l'accusé « Enregistré » et le message d'erreur du serveur, affichés par la page.
 */
@Injectable({ providedIn: 'root' })
export class MetaStore {
  private readonly api = inject(Api);
  readonly m = signal<any>(null);
  readonly catalog = signal<any>(null);
  readonly usage = signal<Usage>({ objectTypes: {}, relationshipTypes: {}, properties: {} });
  /** Change à chaque rechargement complet : les onglets s'en servent pour repartir d'un état propre. */
  readonly version = signal(0);
  readonly saved = signal(false);
  readonly error = signal('');
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** Types d'objets triés par couche puis par libellé. */
  readonly types = computed<any[]>(() => {
    const m = this.m();
    if (!m) return [];
    const order = new Map<string, number>(m.layers.map((l: any) => [l.key, l.order]));
    return [...m.objectTypes].sort((a: any, b: any) =>
      (order.get(a.layerKey) ?? 99) - (order.get(b.layerKey) ?? 99) || a.label.localeCompare(b.label, 'fr'));
  });
  readonly relTypes = computed<any[]>(() => [...(this.m()?.relationshipTypes ?? [])].sort((a: any, b: any) => a.label.localeCompare(b.label, 'fr')));

  async load(): Promise<void> {
    try {
      const [m, catalog, usage] = await Promise.all([
        this.api.get('/metamodel'), this.api.get('/metamodel/catalog'), this.api.get<Usage>('/metamodel/usage')]);
      this.m.set(m);
      this.catalog.set(catalog);
      this.usage.set(usage);
      this.version.update((v) => v + 1);
    } catch (e) {
      this.error.set(detailed(e));
    }
  }

  async refreshUsage(): Promise<void> {
    this.usage.set(await this.api.get<Usage>('/metamodel/usage'));
  }

  /** Exécute une écriture : « Enregistré » en cas de succès, message du serveur sinon. Renvoie vrai si elle a réussi. */
  async run(action: () => Promise<unknown>): Promise<boolean> {
    this.error.set('');
    try {
      await action();
      this.flash();
      return true;
    } catch (e) {
      this.error.set(detailed(e));
      return false;
    }
  }

  /** Suppression par l'API ; le refus du serveur (409 si l'élément est utilisé) arrive sous forme d'erreur. */
  remove(path: string): Promise<void> {
    return this.api.delete<void>(path);
  }

  flash(): void {
    this.saved.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.saved.set(false), 2500);
  }

  patch(part: Record<string, unknown>): void {
    this.m.update((m) => ({ ...m, ...part }));
  }

  typeLabel(key: string | null | undefined): string {
    return this.m()?.objectTypes.find((t: any) => t.key === key)?.label ?? key ?? '';
  }

  relLabel(key: string): string {
    return this.m()?.relationshipTypes.find((t: any) => t.key === key)?.label ?? key;
  }

  layerLabel(key: string): string {
    return this.m()?.layers.find((l: any) => l.key === key)?.label ?? key;
  }

  /** Libellés des types d'objets portant un rôle. */
  typesWith(role: string): string[] {
    return this.types().filter((t) => t.roles.includes(role)).map((t) => t.label);
  }

  roleLabel(key: string): string {
    const c = this.catalog();
    return [...c.objectRoles, ...c.relationshipRoles].find((r: any) => r.key === key)?.label ?? key;
  }
}
