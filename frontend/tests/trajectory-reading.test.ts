// Tests de trajectory-reading.ts (résumé d'une étape, maquette Trajectoires du 27/11), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { changeSpoken, changeSymbol, stepSummary } from '../src/app/trajectory-reading.ts';

const C = (op: string, issue: string | null = null) => ({ op, issue });

test('résumé : compteurs par nature, accordés, sans zéro, puis les points à revoir', () => {
  assert.deepEqual(stepSummary([C('ajout-objet'), C('ajout-lien'), C('retrait', 'encore utilisée')]), ['+ 2 ajouts', '− 1 retrait', '⚠ 1 à revoir']);
  assert.deepEqual(stepSummary([C('remplacement')]), ['⇄ 1 remplacement']);
  assert.deepEqual(stepSummary([]), []);
});

test('symbole du changement, et le mot pour le lecteur d\'écran', () => {
  assert.equal(changeSymbol('ajout-lien'), '+');
  assert.equal(changeSymbol('modification'), '~');
  assert.equal(changeSpoken('retrait'), 'retrait :');
});
