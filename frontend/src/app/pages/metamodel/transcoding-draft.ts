import { Injectable, computed, inject, signal } from '@angular/core';
import { MetaStore } from './store';

export type RuleKind = 'elements' | 'relations';

/**
 * Brouillon de la matrice de transcodification : tout ce que l'on modifie dans l'onglet reste ici, rien ne part au serveur
 * avant « Enregistrer la matrice ». Les contrôles reprennent ceux du serveur pour montrer les erreurs près des règles concernées.
 * Les noms Archi des propriétés vivent dans les définitions de propriétés, pas dans la matrice : ils sont suivis à part.
 */
@Injectable()
export class TranscodingDraft {
  private readonly store = inject(MetaStore);
  readonly t = signal<any>(this.copy());
  /** Noms Archi modifiés, par clé de propriété (vide = identique à la clé). */
  readonly archiEdits = signal<Record<string, string>>({});
  private readonly baseline = signal(JSON.stringify(this.t()));

  readonly changedProperties = computed(() =>
    this.store.m().propertyDefinitions.filter((p: any) => p.key in this.archiEdits() && this.archiEdits()[p.key].trim() !== (p.archiName ?? '')));
  readonly dirty = computed(() => JSON.stringify(this.t()) !== this.baseline() || this.changedProperties().length > 0);

  /** Erreurs bloquantes par emplacement : « elements:3 », « relations:0 », « settings ». */
  readonly errors = computed(() => this.check(false));
  /** Avertissements non bloquants (la première règle l'emporte). */
  readonly warnings = computed(() => this.check(true));
  readonly errorCount = computed(() => [...this.errors().values()].reduce((n, l) => n + l.length, 0));

  private copy(): any {
    return structuredClone(this.store.m().transcoding);
  }

  reset(): void {
    this.t.set(this.copy());
    this.archiEdits.set({});
    this.rebase();
  }

  /** Après un enregistrement : l'état courant devient la référence. */
  rebase(): void {
    this.baseline.set(JSON.stringify(this.t()));
  }

  payload(): any {
    return this.t();
  }

  // ---- Règles ----

  add(kind: RuleKind, typeKey: string): void {
    const first = !this.t()[kind].some((r: any) => r.typeKey === typeKey);
    const rule = kind === 'elements'
      ? { typeKey, archimateType: '', whenProperty: null, whenValue: null, principal: first }
      : { typeKey, archimateType: '', sourceTypeKey: null, targetTypeKey: null, principal: first };
    this.t.update((t) => ({ ...t, [kind]: [...t[kind], rule] }));
  }

  patch(kind: RuleKind, index: number, part: Record<string, unknown>): void {
    this.t.update((t) => ({ ...t, [kind]: t[kind].map((r: any, i: number) => (i === index ? { ...r, ...part } : r)) }));
  }

  remove(kind: RuleKind, index: number): void {
    this.t.update((t) => {
      const gone = t[kind][index];
      const rest = t[kind].filter((_: any, i: number) => i !== index);
      // Sans règle principale, l'export prendrait la première : on la désigne pour que le choix reste visible.
      const orphan = gone.principal && !rest.some((r: any) => r.typeKey === gone.typeKey && r.principal);
      const heir = orphan ? rest.findIndex((r: any) => r.typeKey === gone.typeKey) : -1;
      return { ...t, [kind]: rest.map((r: any, i: number) => (i === heir ? { ...r, principal: true } : r)) };
    });
  }

  setPrincipal(kind: RuleKind, index: number): void {
    const typeKey = this.t()[kind][index].typeKey;
    this.t.update((t) => ({ ...t, [kind]: t[kind].map((r: any, i: number) => (r.typeKey === typeKey ? { ...r, principal: i === index } : r)) }));
  }

  setting(field: string, value: string): void {
    this.t.update((t) => ({ ...t, [field]: value }));
  }

  setArchiName(key: string, value: string): void {
    this.archiEdits.update((e) => ({ ...e, [key]: value }));
  }

  archiName(p: any): string {
    return this.archiEdits()[p.key] ?? p.archiName ?? '';
  }

  // ---- Contrôles ----

  private check(warnings: boolean): Map<string, string[]> {
    const out = new Map<string, string[]>();
    const add = (where: string, message: string) => out.set(where, [...(out.get(where) ?? []), message]);
    const t = this.t();
    const cat = this.store.catalog().archimate;
    const objectKeys = new Set<string>(this.store.m().objectTypes.map((x: any) => x.key));
    const relKeys = new Set<string>(this.store.m().relationshipTypes.map((x: any) => x.key));
    if (warnings) {
      this.duplicates(t.elements, (r) => !r.whenProperty?.trim()).forEach((i) => add(`elements:${i}`, 'Plusieurs règles sans discriminant pour ce concept : la première l\'emporte.'));
      this.duplicates(t.relations, (r) => !r.sourceTypeKey && !r.targetTypeKey).forEach((i) => add(`relations:${i}`, 'Plusieurs règles sans contrainte pour ce concept : la première l\'emporte.'));
      return out;
    }
    t.elements.forEach((r: any, i: number) => {
      if (!objectKeys.has(r.typeKey)) add(`elements:${i}`, `Le type « ${r.typeKey} » n'existe plus dans le métamodèle.`);
      if (!r.archimateType) add(`elements:${i}`, 'Choisissez un concept ArchiMate.');
      else if (!cat.elements.includes(r.archimateType)) add(`elements:${i}`, `« ${r.archimateType} » n'est pas un concept d'élément ArchiMate standard.`);
      if (r.whenProperty?.trim() && !r.whenValue?.trim()) add(`elements:${i}`, 'Le discriminant a une propriété mais pas de valeur.');
    });
    t.relations.forEach((r: any, i: number) => {
      if (!relKeys.has(r.typeKey)) add(`relations:${i}`, `Le type de relation « ${r.typeKey} » n'existe plus dans le métamodèle.`);
      if (!r.archimateType) add(`relations:${i}`, 'Choisissez un concept de relation ArchiMate.');
      else if (!cat.relationships.includes(r.archimateType)) add(`relations:${i}`, `« ${r.archimateType} » n'est pas un concept de relation ArchiMate standard.`);
      for (const end of [r.sourceTypeKey, r.targetTypeKey])
        if (end && !objectKeys.has(end)) add(`relations:${i}`, `Le type « ${end} » de la contrainte n'existe plus.`);
    });
    if (!cat.elements.includes(t.fallbackElementType)) add('settings', 'Le concept de repli des objets doit être un concept d\'élément ArchiMate.');
    if (!cat.relationships.includes(t.fallbackRelationType)) add('settings', 'Le concept de repli des relations doit être un concept de relation ArchiMate.');
    if (!t.typeMarkerProperty?.trim()) add('settings', 'Le nom de la propriété marqueur est obligatoire.');
    if (!objectKeys.has(t.unmappedTypeKey) && t.unmappedTypeKey !== 'autre') add('settings', 'Le type qui reçoit les concepts non transcodifiés n\'existe pas.');
    if (!relKeys.has(t.unmappedRelationTypeKey) && t.unmappedRelationTypeKey !== 'association') add('settings', 'Le type de relation qui reçoit les concepts non transcodifiés n\'existe pas.');
    return out;
  }

  /** Indices des règles « libres » qui partagent un concept avec une autre. */
  private duplicates(rules: any[], free: (r: any) => boolean): number[] {
    const seen = new Map<string, number[]>();
    rules.forEach((r, i) => { if (r.archimateType && free(r)) seen.set(r.archimateType, [...(seen.get(r.archimateType) ?? []), i]); });
    return [...seen.values()].filter((l) => l.length > 1).flat();
  }
}
