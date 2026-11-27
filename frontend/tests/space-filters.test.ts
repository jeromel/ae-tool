// Tests de space-filters.ts (recette de Haussman, question 4), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isIsolated, isOrphan } from '../src/app/space-filters.ts';

test('aucune relation : isolée seulement si le périmètre ne lui a rien retiré', () => {
  assert.equal(isIsolated({ relations: 0 }), true);
  assert.equal(isIsolated({ relations: 0, scopeTrimmed: true }), false, 'reliée à son seul serveur, masqué');
  assert.equal(isIsolated({ relations: 2 }), false);
});

test('aucune application : orpheline seulement si le périmètre ne lui a rien retiré', () => {
  assert.equal(isOrphan({ applications: 0 }), true);
  assert.equal(isOrphan({ applications: 0, scopeTrimmed: true }), false, 'utilisée par un artefact masqué');
  assert.equal(isOrphan({ applications: 1 }), false);
});
