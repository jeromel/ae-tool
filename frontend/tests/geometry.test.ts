// Tests de geometry.ts, hors navigateur : node --test tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { bezier, bezierMiddle, contains, facingSides, polylineMiddle, portPoint, roundedPath } from '../src/app/geometry.ts';

const box = (x: number, y: number, w: number, h: number) => ({ x, y, w, h });

test('contains : bords communs inclus, débordement exclu', () => {
  assert.equal(contains(box(0, 0, 100, 100), box(10, 10, 20, 20)), true);
  assert.equal(contains(box(0, 0, 100, 100), box(0, 0, 100, 100)), true);
  assert.equal(contains(box(0, 0, 100, 100), box(90, 10, 20, 20)), false);
  assert.equal(contains(box(10, 10, 20, 20), box(0, 0, 100, 100)), false);
});

test('facingSides : formes séparées, côtés qui se font face selon le plus grand écart', () => {
  assert.deepEqual(facingSides(box(0, 0, 100, 50), box(300, 0, 100, 50)), ['E', 'W']);
  assert.deepEqual(facingSides(box(300, 0, 100, 50), box(0, 0, 100, 50)), ['W', 'E']);
  assert.deepEqual(facingSides(box(0, 0, 100, 50), box(0, 200, 100, 50)), ['S', 'N']);
  assert.deepEqual(facingSides(box(0, 200, 100, 50), box(0, 0, 100, 50)), ['N', 'S']);
  // En diagonale : l'écart vertical (150) l'emporte sur l'horizontal (50).
  assert.deepEqual(facingSides(box(0, 0, 100, 50), box(150, 200, 100, 50)), ['S', 'N']);
});

test('facingSides : forme contenue, côté de la forme intérieure le plus proche du bord, pour les deux', () => {
  const outer = box(0, 0, 400, 300);
  assert.deepEqual(facingSides(box(350, 100, 40, 40), outer), ['E', 'E']);
  assert.deepEqual(facingSides(outer, box(100, 10, 40, 40)), ['N', 'N']);
});

test('portPoint : un lien seul au milieu du côté, plusieurs répartis sur les 70 % centraux', () => {
  const r = box(0, 0, 200, 100);
  assert.deepEqual(portPoint(r, 'N', 0, 1), { x: 100, y: 0 });
  assert.deepEqual(portPoint(r, 'S', 0, 1), { x: 100, y: 100 });
  assert.deepEqual(portPoint(r, 'W', 0, 1), { x: 0, y: 50 });
  assert.deepEqual(portPoint(r, 'E', 0, 1), { x: 200, y: 50 });
  const xs = [0, 1, 2, 3].map((i) => portPoint(r, 'N', i, 4).x);
  for (let i = 1; i < xs.length; i++) assert.ok(xs[i] > xs[i - 1], 'ordre croissant');
  assert.ok(xs[0] >= 0.15 * 200 && xs[3] <= 0.85 * 200, `répartis sur les 70 % centraux : ${xs}`);
  // count à 0 ne divise pas par zéro.
  assert.ok(Number.isFinite(portPoint(r, 'N', 0, 0).x));
});

test('bezier : part de p, arrive en q, perpendiculaire aux côtés', () => {
  const d = bezier({ x: 0, y: 0 }, 'E', { x: 200, y: 0 }, 'W');
  assert.match(d, /^M0,0 C\d+(\.\d)?,0 \d+(\.\d)?,0 200,0$/);
  const [, c1x, , c2x] = d.match(/C([\d.]+),0 ([\d.]+),0/)!.concat('');
  assert.ok(Number(c1x) > 0 && Number(c1x) < 200, 'premier point de contrôle à droite de p');
});

test('bezierMiddle : milieu d\'une courbe symétrique', () => {
  assert.deepEqual(bezierMiddle({ x: 0, y: 0 }, 'E', { x: 200, y: 0 }, 'W'), { x: 100, y: 0 });
  // Imbriquée : contrôles aux tiers, le milieu est celui du segment.
  assert.deepEqual(bezierMiddle({ x: 0, y: 0 }, 'N', { x: 90, y: 30 }, 'N', true), { x: 45, y: 15 });
});

test('roundedPath : ligne droite, coude arrondi, coude trop court gardé anguleux', () => {
  assert.equal(roundedPath([{ x: 0, y: 0 }, { x: 10, y: 0 }], 6), 'M0,0 L10,0');
  assert.equal(roundedPath([{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }], 6), 'M0,0 L94,0 Q100,0 100,6 L100,100');
  // Segments de 4 : rayon réduit à 2.
  assert.equal(roundedPath([{ x: 0, y: 0 }, { x: 4, y: 0 }, { x: 4, y: 4 }], 6), 'M0,0 L2,0 Q4,0 4,2 L4,4');
  // Segment de 1 : rayon sous 1, coude anguleux.
  assert.equal(roundedPath([{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 50 }], 6), 'M0,0 L1,0 L1,50');
});

test('polylineMiddle : milieu mesuré le long des segments', () => {
  assert.deepEqual(polylineMiddle([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }]), { x: 10, y: 0 });
  assert.deepEqual(polylineMiddle([{ x: 0, y: 0 }, { x: 30, y: 0 }, { x: 30, y: 10 }]), { x: 20, y: 0 });
  // Points confondus : pas de division par zéro.
  assert.deepEqual(polylineMiddle([{ x: 5, y: 5 }, { x: 5, y: 5 }]), { x: 5, y: 5 });
  assert.deepEqual(polylineMiddle([{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 0, y: 20 }]), { x: 0, y: 10 });
});
