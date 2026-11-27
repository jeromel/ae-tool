// Tests de object-card.ts (propriétés de la fiche, maquette carte objet §6), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cardProperties } from '../src/app/object-card.ts';

const P = (key: string, value: string | null, label = key.toUpperCase(), labeled = true) => ({ key, label, labeled, value });

test('jamais une clé : sans libellé, caché au lecteur, montré à l\'administrateur avec sa clé', () => {
  const props = [P('lifecycle.state', 'En service', 'Cycle de vie'), P('x.tech', 'v', 'x.tech', false)];
  assert.deepEqual(cardProperties(props, false).front.map((p) => p.label), ['Cycle de vie']);
  const admin = cardProperties(props, true).front;
  assert.deepEqual(admin.map((p) => [p.label, p.unlabeled]), [['Cycle de vie', false], ['x.tech', true]]);
  const first = cardProperties([P('x.tech', 'v', 'x.tech', false), P('b', 'w', 'B')], true).front;
  assert.deepEqual(first.map((p) => p.label), ['B', 'x.tech'], 'sans libellé après les autres');
});

test('valeurs vides retirées, doublons de libellé et de valeur fusionnés', () => {
  const props = [P('a', 'x', 'A'), P('a2', 'x', 'A'), P('b', '  '), P('c', null), P('d', 'y', 'A')];
  assert.deepEqual(cardProperties(props, false).front.map((p) => `${p.label}=${p.value}`), ['A=x', 'A=y']);
});

test('six au premier plan, le reste replié, dans l\'ordre reçu', () => {
  const props = Array.from({ length: 9 }, (_, i) => P(`p${i}`, `v${i}`));
  const { front, rest } = cardProperties(props, false);
  assert.deepEqual(front.map((p) => p.key), ['p0', 'p1', 'p2', 'p3', 'p4', 'p5']);
  assert.deepEqual(rest.map((p) => p.key), ['p6', 'p7', 'p8']);
});

test('le responsable compte parmi les six', () => {
  const props = Array.from({ length: 7 }, (_, i) => P(`p${i}`, `v${i}`));
  assert.equal(cardProperties(props, false, 1).front.length, 5);
});
