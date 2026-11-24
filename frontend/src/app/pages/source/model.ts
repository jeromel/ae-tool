import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';

/** Un lien vers un type du métamodèle, tel que la matrice de transcodification le porte. */
export interface TypeRow { sourceValue: string; typeKey: string; }
/** Colonne de la source reprise vers une propriété ; `creating` n'existe que dans l'écran (saisie d'une nouvelle clé). */
export interface FieldRow { sourceField: string; propertyKey: string; label: string; creating?: boolean; }
export interface Mapping {
  idField: string; nameField: string; typeField: string; defaultTypeKey: string;
  types: TypeRow[]; fields: FieldRow[];
}

export interface Profile {
  key: string; label: string; kind: 'fichier' | 'api';
  connection: Record<string, string>; mapping: any; sourceIsMaster: boolean;
  ignoredCount: number; lastRunAt: string | null; lastRunBy: string | null; hasSecret: boolean; hasReport: boolean;
}

export interface Preview {
  columns: string[]; rows: Record<string, string | null>[]; rowCount: number; total: number | null; truncated: boolean;
  missingColumns: string[]; issues: { severity: string; code: string; message: string; ref?: string }[];
}

export interface Candidate { id: string; name: string; typeKey: string; }
export interface Difference { field: string; label: string; repositoryValue: string | null; sourceValue: string | null; }
export interface Line {
  externalId: string; name: string; typeValue: string | null; status: string; typeKey: string | null;
  objectId: string | null; objectName: string | null; candidates: Candidate[]; differences: Difference[]; message: string | null;
}
export interface Report {
  at: string; sourceName: string | null; rowsRead: number; sourceTotal: number | null; truncated: boolean;
  counts: Record<string, number>; linesTotal: number; lines: Line[];
  missing: { id: string; name: string; typeKey: string; externalId: string }[];
  unlinkedCount: number; unlinked: Candidate[];
  issues: { severity: string; code: string; message: string; ref?: string }[]; notes: string[];
}

/** Décision en attente : elle n'atteint le référentiel qu'à « Appliquer ». `status` sert au décompte des propositions. */
export interface Pending { externalId: string; name: string; action: 'lier' | 'creer' | 'ignorer' | 'delier'; objectId?: string; objectName?: string; status: string; }

export const ACTION_LABELS: Record<Pending['action'], string> = { lier: 'Lier', creer: 'Créer', ignorer: 'Ignorer', delier: 'Délier' };

export function toDraft(m: any): Mapping {
  return {
    idField: m.idField ?? '', nameField: m.nameField ?? '', typeField: m.typeField ?? '', defaultTypeKey: m.defaultTypeKey ?? '',
    types: (m.types ?? []).map((t: any) => ({ sourceValue: t.sourceValue, typeKey: t.typeKey })),
    fields: (m.fields ?? []).map((f: any) => ({ sourceField: f.sourceField, propertyKey: f.propertyKey, label: f.label ?? '' })),
  };
}

/** Ce que le serveur attend : sans les champs propres à l'écran, sans lignes vides. */
export function toPayload(d: Mapping): any {
  return {
    idField: d.idField.trim(), nameField: d.nameField.trim(), typeField: d.typeField.trim() || null, defaultTypeKey: d.defaultTypeKey || null,
    types: d.types.filter((t) => t.sourceValue.trim() || t.typeKey).map((t) => ({ sourceValue: t.sourceValue, typeKey: t.typeKey })),
    fields: d.fields.filter((f) => f.sourceField.trim() || f.propertyKey.trim())
      .map((f) => ({ sourceField: f.sourceField, propertyKey: f.propertyKey, label: f.label.trim() || null })),
  };
}

/**
 * État d'une source ouverte, partagé par ses trois onglets : changer d'onglet ne fait perdre ni un brouillon,
 * ni le fichier choisi, ni les décisions en attente. Fourni par la page « source ».
 */
@Injectable()
export class SourceState {
  private api = inject(Api);

  readonly profile = signal<Profile | null>(null);
  readonly error = signal('');
  readonly layers = signal<{ key: string; label: string }[]>([]);
  readonly types = signal<{ key: string; label: string; layerKey: string }[]>([]);
  readonly properties = signal<{ key: string; label: string }[]>([]);

  readonly mapping = signal<Mapping | null>(null);
  readonly master = signal(false);
  readonly connection = signal<Record<string, string>>({});
  readonly preview = signal<Preview | null>(null);
  readonly file = signal<File | null>(null);
  readonly pending = signal<Pending[]>([]);
  readonly acceptAll = signal(false);

  readonly typeLabel = computed(() => {
    const labels = new Map(this.types().map((t) => [t.key, t.label]));
    return (key: string | null | undefined) => (key ? (labels.get(key) ?? key) : '');
  });
  readonly mappingDirty = computed(() => {
    const p = this.profile(), m = this.mapping();
    return !!p && !!m && (JSON.stringify(toPayload(m)) !== JSON.stringify(toPayload(toDraft(p.mapping))) || this.master() !== p.sourceIsMaster);
  });
  readonly connectionDirty = computed(() => {
    const p = this.profile();
    return !!p && JSON.stringify(this.connection()) !== JSON.stringify(p.connection);
  });
  readonly columns = computed(() => this.preview()?.columns ?? []);

  async open(key: string): Promise<void> {
    this.error.set('');
    try {
      const [profile, metamodel] = await Promise.all([this.api.get<Profile>(`/sources/${key}`), this.api.get('/metamodel')]);
      this.layers.set([...metamodel.layers].sort((a: any, b: any) => a.order - b.order));
      this.types.set(metamodel.objectTypes.filter((t: any) => !t.archived));
      this.properties.set(metamodel.propertyDefinitions.map((p: any) => ({ key: p.key, label: p.label ?? p.key })));
      this.adopt(profile);
    } catch (e) {
      this.error.set((e as Error).message);
    }
  }

  /** Remplace le profil enregistré et, sauf demande contraire, les brouillons qui en dépendent. */
  adopt(profile: Profile, keep: { mapping?: boolean; connection?: boolean } = {}): void {
    this.profile.set(profile);
    if (!keep.mapping) { this.mapping.set(toDraft(profile.mapping)); this.master.set(profile.sourceIsMaster); }
    if (!keep.connection) this.connection.set({ ...profile.connection });
  }

  /** Enregistre la partie demandée ; l'autre partie du profil reste telle qu'elle est enregistrée. */
  async save(part: 'mapping' | 'connection', secret?: string): Promise<void> {
    const p = this.profile()!;
    const body: any = {
      label: p.label, kind: p.kind,
      connection: part === 'connection' ? this.connection() : p.connection,
      mapping: part === 'mapping' ? toPayload(this.mapping()!) : p.mapping,
      sourceIsMaster: part === 'mapping' ? this.master() : p.sourceIsMaster,
    };
    if (secret !== undefined) body.secret = secret;
    const saved = await this.api.put<Profile>(`/sources/${p.key}`, body);
    this.adopt(saved, { mapping: part !== 'mapping' && this.mappingDirty(), connection: part !== 'connection' && this.connectionDirty() });
  }

  async refreshProfile(): Promise<void> {
    const p = this.profile();
    if (p) this.adopt(await this.api.get<Profile>(`/sources/${p.key}`), { mapping: this.mappingDirty(), connection: this.connectionDirty() });
  }

  /** Lit un aperçu : le fichier choisi (profil fichier) ou un appel de l'API (profil API REST). */
  async loadPreview(): Promise<void> {
    const p = this.profile()!;
    const file = this.file();
    const path = `/sources/${p.key}/preview` + (file ? `?name=${encodeURIComponent(file.name)}` : '');
    this.preview.set(p.kind === 'fichier' ? await this.api.upload<Preview>(path, file!) : await this.api.post<Preview>(path, {}));
  }

  decisionOf(externalId: string): Pending | undefined {
    return this.pending().find((d) => d.externalId === externalId);
  }

  decide(d: Pending): void {
    this.pending.update((all) => [...all.filter((x) => x.externalId !== d.externalId), d]);
  }

  undecide(externalId: string): void {
    this.pending.update((all) => all.filter((x) => x.externalId !== externalId));
  }

  clearDecisions(): void {
    this.pending.set([]);
    this.acceptAll.set(false);
  }
}
