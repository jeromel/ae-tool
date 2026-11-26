// Tests de card-grow.ts (charte §2.1, palier 3 ; revue Camille L1), hors navigateur : node --test tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { GROW_GAP, grownHeight, roomBelow } from '../src/app/card-grow.ts';

const zone = { x: 0, y: 0, w: 400, h: 200 };

test('dans une zone, la case grandit jusqu\'au bord bas de la zone, écart gardé', () => {
  const card = { x: 10, y: 30, w: 150, h: 40 };
  assert.equal(roomBelow(card, [zone, card]), Math.min(200 - GROW_GAP - 70, 40));
  const low = { x: 10, y: 140, w: 150, h: 40 };
  assert.equal(roomBelow(low, [zone, low]), 200 - GROW_GAP - 180);
});

test('une forme dessous, qui chevauche en largeur, arrête la case ; une forme à côté ne compte pas', () => {
  const card = { x: 10, y: 30, w: 150, h: 40 };
  const below = { x: 100, y: 90, w: 150, h: 40 };
  const beside = { x: 200, y: 75, w: 150, h: 40 };
  assert.equal(roomBelow(card, [zone, card, below, beside]), 90 - GROW_GAP - 70);
});

test('une forme au-dessus ou déjà collée ne donne aucune place négative', () => {
  const card = { x: 10, y: 30, w: 150, h: 40 };
  const touching = { x: 10, y: 72, w: 150, h: 40 };
  assert.equal(roomBelow(card, [zone, card, touching]), 0);
  assert.equal(roomBelow(card, [zone, card, { x: 10, y: 0, w: 150, h: 20 }]), 40);
});

test('hors zone et sans voisin, la case ne fait au plus que doubler', () => {
  const card = { x: 10, y: 30, w: 150, h: 40 };
  assert.equal(roomBelow(card, [card]), 40);
});

test('hauteur retenue : la plus petite qui suffit, ou rien si même agrandie le nom ne tient pas', () => {
  assert.equal(grownHeight(40, 20, (h) => h >= 50), 50);
  assert.equal(grownHeight(40, 20, (h) => h >= 61), null);
  assert.equal(grownHeight(40, 0, () => true), null);
  assert.equal(grownHeight(40, 5, (h) => h >= 45), 45);
});
