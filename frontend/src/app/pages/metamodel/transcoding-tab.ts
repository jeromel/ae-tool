import { Component, computed, inject, signal } from '@angular/core';
import { Api } from '../../api';
import { MatrixSettings } from './matrix-settings';
import { ReapplyPanel } from './reapply-panel';
import { RulesProperties } from './rules-properties';
import { RulesTable } from './rules-table';
import { MetaStore } from './store';
import { TranscodingDraft } from './transcoding-draft';

interface Finding { severity: string; code: string; message: string; key: string | null; }
interface Report { findings: Finding[]; rules: any[]; }
const SEVERITIES: { key: string; label: string; chip: string }[] = [
  { key: 'erreur', label: 'Erreurs', chip: 'bad' }, { key: 'avertissement', label: 'Avertissements', chip: 'warn' }, { key: 'information', label: 'Informations', chip: 'info' },
];

/**
 * Matrice de transcodification vers ArchiMate. Les modifications restent en brouillon dans la page : un seul PUT les enregistre.
 * Les constats et les effectifs en tête décrivent la matrice enregistrée, pas le brouillon.
 */
@Component({
  selector: 'app-transcoding-tab',
  imports: [RulesTable, RulesProperties, MatrixSettings, ReapplyPanel],
  providers: [TranscodingDraft],
  template: `
    <div class="stats" aria-label="Synthèse de la matrice enregistrée">
      <div class="stat"><b>{{ covered().elements }} / {{ concepts().elements }}</b><span>concepts d'éléments transcodés</span></div>
      <div class="stat"><b>{{ covered().relations }} / {{ concepts().relations }}</b><span>concepts de relations transcodés</span></div>
      <div class="stat"><b>{{ typeCover().with }}</b><span>types d'objets avec règle</span></div>
      <div class="stat"><b>{{ typeCover().without }}</b><span>types sans règle (exportés en repli)</span></div>
      @for (s of severities; track s.key) { <div class="stat" [class.alert]="s.key === 'erreur' && countOf(s.key) > 0"><b>{{ countOf(s.key) }}</b><span>{{ s.label.toLowerCase() }}</span></div> }
    </div>
    <p class="muted small">Le type « {{ store.typeLabel(draft.t().unmappedTypeKey) }} » et la relation « {{ store.relLabel(draft.t().unmappedRelationTypeKey) }} » reçoivent les concepts ArchiMate sans règle : ils n'ont pas de règle par construction.</p>
    @if (report(); as r) {
      @for (s of severities; track s.key) {
        @if (byseverity(r, s.key).length) {
          <details [open]="s.key !== 'information'">
            <summary><span [class]="'chip ' + s.chip">{{ s.label }}</span> {{ byseverity(r, s.key).length }} constat(s)</summary>
            <ul class="findings">@for (f of byseverity(r, s.key); track $index) { <li>{{ f.message }} @if (f.key) { <code>{{ f.key }}</code> }</li> }</ul>
          </details>
        }
      }
    }

    <div class="tools sticky">
      <button type="button" (click)="save()" [disabled]="!draft.dirty() || draft.errorCount() > 0 || busy()">Enregistrer la matrice</button>
      <button type="button" class="ghost" (click)="draft.reset()" [disabled]="!draft.dirty()">Abandonner les modifications</button>
      @if (draft.dirty()) { <span class="chip warn" role="status">Modifications non enregistrées</span> }
      @if (draft.errorCount() > 0) { <span class="chip bad">{{ draft.errorCount() }} erreur(s) à corriger avant d'enregistrer</span> }
      @if (draft.changedProperties().length) { <span class="muted small">dont {{ draft.changedProperties().length }} nom(s) Archi de propriété</span> }
    </div>

    <div class="segmented" role="group" aria-label="Partie de la matrice">
      @for (p of parts; track p.key) { <button type="button" [attr.aria-pressed]="part() === p.key" (click)="part.set(p.key)">{{ p.label }}</button> }
    </div>
    <div class="partbody">
      @switch (part()) {
        @case ('objets') { <app-rules-table kind="elements" [usage]="report()?.rules ?? []" /> }
        @case ('relations') { <app-rules-table kind="relations" [usage]="report()?.rules ?? []" /> }
        @default { <app-rules-properties /> }
      }
    </div>
    <app-matrix-settings />
    <app-reapply-panel [blocked]="draft.dirty()" (changed)="loadReport()" />
  `,
})
export class TranscodingTab {
  protected readonly store = inject(MetaStore);
  protected readonly draft = inject(TranscodingDraft);
  private readonly api = inject(Api);
  protected readonly severities = SEVERITIES;
  protected readonly parts = [{ key: 'objets', label: 'Objets' }, { key: 'relations', label: 'Relations' }, { key: 'proprietes', label: 'Propriétés' }];
  protected readonly part = signal('objets');
  protected readonly report = signal<Report | null>(null);
  protected readonly busy = signal(false);

  protected readonly concepts = computed(() => ({ elements: this.store.catalog().archimate.elements.length, relations: this.store.catalog().archimate.relationships.length }));
  /** Concepts du catalogue couverts par au moins une règle de la matrice enregistrée. */
  protected readonly covered = computed(() => {
    const t = this.store.m().transcoding;
    const cat = this.store.catalog().archimate;
    return {
      elements: cat.elements.filter((c: string) => t.elements.some((r: any) => r.archimateType === c)).length,
      relations: cat.relationships.filter((c: string) => t.relations.some((r: any) => r.archimateType === c)).length,
    };
  });
  protected readonly typeCover = computed(() => {
    const ruled = new Set(this.store.m().transcoding.elements.map((r: any) => r.typeKey));
    const types: any[] = this.store.m().objectTypes.filter((t: any) => t.key !== this.store.m().transcoding.unmappedTypeKey);
    const with_ = types.filter((t) => ruled.has(t.key)).length;
    return { with: with_, without: types.length - with_ };
  });

  constructor() {
    void this.loadReport();
  }

  protected countOf(severity: string): number {
    return this.report()?.findings.filter((f) => f.severity === severity).length ?? 0;
  }

  protected byseverity(report: Report, severity: string): Finding[] {
    return report.findings.filter((f) => f.severity === severity);
  }

  protected async loadReport(): Promise<void> {
    try {
      this.report.set(await this.api.get<Report>('/metamodel/transcoding/checks'));
    } catch (e) {
      this.store.error.set((e as Error).message);
    }
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    const ok = await this.store.run(async () => {
      const saved = await this.api.put('/metamodel/transcoding', this.draft.payload());
      this.store.patch({ transcoding: saved });
      this.draft.rebase();
      for (const p of this.draft.changedProperties()) {
        const body = { ...p, archiName: this.draft.archiName(p).trim() || null };
        const updated = await this.api.put(`/metamodel/properties?key=${encodeURIComponent(p.key)}`, body);
        this.store.patch({ propertyDefinitions: this.store.m().propertyDefinitions.map((x: any) => (x.key === updated.key ? updated : x)) });
      }
      this.draft.archiEdits.set({});
    });
    this.busy.set(false);
    if (ok) await this.loadReport();
  }
}
