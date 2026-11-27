// Tests de import-report.ts (tableau des valeurs remplacées par Archi), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { type Overwrite, groupOverwrites, overwritesTitle } from '../src/app/import-report.ts';

const O = (objectId: string, label: string, after: string | null): Overwrite =>
  ({ objectId, objectName: objectId.toUpperCase(), property: label.toLowerCase(), label, before: 'ici', after });

test('titre accordé au nombre', () => {
  assert.equal(overwritesTitle(1), '1 valeur saisie dans l\'outil remplacée par Archi');
  assert.equal(overwritesTitle(3), '3 valeurs saisies dans l\'outil remplacées par Archi');
});

test('lignes groupées par objet, dans l\'ordre de l\'import', () => {
  const groups = groupOverwrites([O('a', 'Nom', 'x'), O('b', 'Cycle de vie', null), O('a', 'Description', 'y')]);
  assert.deepEqual(groups.map((g) => `${g.objectName}:${g.lines.map((l) => l.label).join('+')}`), ['A:Nom+Description', 'B:Cycle de vie']);
  assert.equal(groups[1].lines[0].after, null, 'effacée : la valeur reste nulle, l\'écran écrit « effacée »');
});
