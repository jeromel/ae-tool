// Tests de archi-pending.ts (section « Pas encore dans Archi », export B), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { type ArchiPendingSummary, citedLine, exclusionLine, pendingState, pendingTotals, viewsLine } from '../src/app/archi-pending.ts';

const S = (o: Partial<ArchiPendingSummary> = {}): ArchiPendingSummary => ({
  lastImport: '2026-11-24T17:02:00Z', neverImported: false, objects: [], relations: [], excludedProposed: 0, excludedDrafts: 0,
  excludedRelations: 0, citedObjects: 0, viewsHere: 0, ...o,
});

test('quatre états : chargement, erreur, jamais importé, vide, sinon la liste', () => {
  assert.equal(pendingState(null, ''), 'chargement');
  assert.equal(pendingState(null, 'refusé'), 'erreur');
  assert.equal(pendingState(S({ neverImported: true }), ''), 'jamais');
  assert.equal(pendingState(S(), ''), 'vide');
  assert.equal(pendingState(S({ relations: [{}] }), ''), 'liste');
});

test('récapitulatif accordé, jamais « (s) »', () => {
  assert.equal(pendingTotals(S({ objects: [{}], relations: [{}, {}] })), '1 objet · 2 relations');
  assert.equal(pendingTotals(S()), '0 objet · 0 relation');
});

test('ligne d\'exclusion : libellé ratifié, accords, et rien quand tout part', () => {
  assert.equal(exclusionLine(S()), null);
  assert.equal(exclusionLine(S({ excludedProposed: 3, excludedDrafts: 2, excludedRelations: 4 })),
    '5 objets proposés ou brouillons ne sont pas inclus, ni les 4 relations qui les touchent.');
  assert.equal(exclusionLine(S({ excludedDrafts: 1 })), '1 objet proposé ou brouillon n\'est pas inclus.');
  assert.equal(exclusionLine(S({ excludedDrafts: 1, excludedRelations: 1 })), '1 objet proposé ou brouillon n\'est pas inclus, ni la relation qui le touche.');
  assert.equal(exclusionLine(S({ excludedRelations: 2 })), '2 relations ne sont pas incluses : elles touchent un objet qui ne part pas.');
});

test('objets d\'Archi cités : une information, au singulier ou au pluriel', () => {
  assert.equal(citedLine(S()), null);
  assert.equal(citedLine(S({ citedObjects: 4 })), '4 objets d\'Archi sont cités par leurs relations : nom seulement, leurs valeurs ne sont pas touchées.');
  assert.equal(citedLine(S({ citedObjects: 1 })), '1 objet d\'Archi est cité par ses relations : nom seulement, ses valeurs ne sont pas touchées.');
});

test('ligne des vues seulement s\'il existe des vues créées ici', () => {
  assert.equal(viewsLine(S()), null);
  assert.match(viewsLine(S({ viewsHere: 2 }))!, /^Les vues dessinées ici ne partent pas/);
});
