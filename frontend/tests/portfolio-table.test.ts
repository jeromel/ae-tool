// Tests de portfolio-table.ts (tableau du portefeuille, maquette du 27/11), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { controlCell, decisionCell, sortRows } from '../src/app/portfolio-table.ts';

const R = (name: string, o: { retained?: string; computed?: string; priority?: string; control?: string; missing?: string[] } = {}) =>
  ({ name, retainedDecision: o.retained ?? null, result: { computedDecision: o.computed ?? null, priority: o.priority ?? null, control: o.control ?? null, missing: o.missing ?? [] } });

test('décision : retenue d\'abord, calculée en dessous si elle diffère', () => {
  assert.deepEqual(decisionCell(R('a', { retained: 'Éliminer', computed: 'Tolérer' })), { text: 'Éliminer', computed: false, note: 'calculée : Tolérer' });
  assert.deepEqual(decisionCell(R('a', { retained: 'Maintenir', computed: 'Maintenir' })), { text: 'Maintenir', computed: false, note: null });
});

test('décision : sans retenue, la calculée marquée comme telle ; calcul incomplet, ce qui manque', () => {
  assert.deepEqual(decisionCell(R('a', { computed: 'Maintenir' })), { text: 'Maintenir', computed: true, note: null });
  assert.deepEqual(decisionCell(R('a', { control: 'Calcul incomplet', missing: ['Besoin métier', 'Preuves / références'] })),
    { text: null, computed: false, note: 'il manque : Besoin métier, Preuves / références' });
});

test('contrôle : symbole et mot, jamais la couleur seule', () => {
  assert.deepEqual(controlCell('Conforme au calcul'), { symbol: '✓', word: 'conforme', tone: 'ok' });
  assert.equal(controlCell('Calcul incomplet').word, 'calcul incomplet');
  assert.equal(controlCell('Justification requise').symbol, '⚠');
  assert.equal(controlCell('Sans objet').word, '—');
});

test('tri : par défaut priorité haute d\'abord puis nom ; par décision ; inversé', () => {
  const rows = [R('b', { priority: 'Basse', computed: 'Maintenir' }), R('a', { priority: 'Haute', computed: 'Tolérer' }), R('c', { priority: 'Haute', retained: 'Éliminer' }), R('d')];
  assert.deepEqual(sortRows(rows, 'defaut').map((r) => r.name), ['a', 'c', 'b', 'd']);
  assert.deepEqual(sortRows(rows, 'decision').map((r) => r.name), ['c', 'b', 'a', 'd']);
  assert.deepEqual(sortRows(rows, 'priorite', true).map((r) => r.name), ['d', 'b', 'a', 'c']);
  assert.deepEqual(sortRows(rows, 'decision', true).map((r) => r.name), ['a', 'b', 'c', 'd'], 'sans décision toujours à la fin');
});
