// Tests de auto-layout.ts, hors navigateur : node --test tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { type Box, type LayoutEdge, type LayoutNode, autoLayout, layers, snap } from '../src/app/auto-layout.ts';

const card = (id: string, parentId: string | null = null, label = id): LayoutNode =>
  ({ id, parentId, w: 120, h: 55, zone: false, element: true, label });
const zone = (id: string, parentId: string | null = null, label = id): LayoutNode =>
  ({ id, parentId, w: 400, h: 300, zone: true, element: false, label });

function weights(edges: [string, string, number?][]): Map<string, Map<string, number>> {
  const w = new Map<string, Map<string, number>>();
  for (const [a, b, k] of edges) {
    if (!w.has(a)) w.set(a, new Map());
    w.get(a)!.set(b, (w.get(a)!.get(b) ?? 0) + (k ?? 1));
  }
  return w;
}

const overlap = (a: Box, b: Box) => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
const inside = (outer: Box, inner: Box) =>
  inner.x >= outer.x && inner.y >= outer.y && inner.x + inner.w <= outer.x + outer.w && inner.y + inner.h <= outer.y + outer.h;

test('snap : pas de 10', () => {
  assert.equal(snap(0), 0);
  assert.equal(snap(14), 10);
  assert.equal(snap(15), 20);
  assert.equal(snap(-14), -10);
});

test('layers : pas d\'orientation lisible, null', () => {
  assert.equal(layers(['a', 'b'], weights([['a', 'b']])), null, 'moins de trois éléments');
  assert.equal(layers(['a', 'b', 'c'], weights([])), null, 'aucun lien');
  // Cycle symétrique : la moitié des liens remonterait l'ordre.
  assert.equal(layers(['a', 'b', 'c'], weights([['a', 'b'], ['b', 'a'], ['b', 'c'], ['c', 'b'], ['c', 'a'], ['a', 'c']])), null);
});

test('layers : chaîne et éventail, rang = plus long chemin entrant', () => {
  assert.deepEqual(layers(['a', 'b', 'c'], weights([['a', 'b'], ['b', 'c']])), [['a'], ['b'], ['c']]);
  assert.deepEqual(layers(['c', 'b', 'a'], weights([['a', 'b'], ['b', 'c']])), [['a'], ['b'], ['c']], 'indépendant de l\'ordre de départ');
  // a → c directement et via b : c au rang 2.
  assert.deepEqual(layers(['a', 'b', 'c'], weights([['a', 'b'], ['b', 'c'], ['a', 'c']])), [['a'], ['b'], ['c']]);
  const fan = layers(['s', 'x', 'y', 'z'], weights([['s', 'x'], ['s', 'y'], ['s', 'z']]))!;
  assert.deepEqual(fan[0], ['s']);
  assert.deepEqual([...fan[1]].sort(), ['x', 'y', 'z']);
});

test('layers : cycle cassé au lien le plus faible', () => {
  // a → b (×3), b → c (×3), c → a (×1) : on ignore c → a.
  assert.deepEqual(layers(['a', 'b', 'c'], weights([['a', 'b', 3], ['b', 'c', 3], ['c', 'a', 1]])), [['a'], ['b'], ['c']]);
});

test('layers : tout élément placé exactement une fois', () => {
  const ids = ['a', 'b', 'c', 'd', 'e', 'f'];
  const out = layers(ids, weights([['a', 'b'], ['a', 'c'], ['b', 'd'], ['c', 'd'], ['d', 'e']]))!;
  assert.deepEqual(out.flat().sort(), ids);
});

test('autoLayout : vue vide', () => {
  assert.deepEqual(autoLayout([], []), {});
});

test('autoLayout : chaîne de cartes posée de gauche à droite, sur la grille de 10', () => {
  const out = autoLayout([card('a'), card('b'), card('c')], [{ source: 'a', target: 'b' }, { source: 'b', target: 'c' }]);
  assert.ok(out['a'].x < out['b'].x && out['b'].x < out['c'].x, JSON.stringify(out));
  for (const b of Object.values(out)) for (const v of [b.x, b.y, b.w, b.h]) assert.equal(v % 10, 0, JSON.stringify(b));
});

test('autoLayout : zones enchaînées posées de haut en bas', () => {
  const nodes = [zone('Z1'), card('a', 'Z1'), zone('Z2'), card('b', 'Z2'), zone('Z3'), card('c', 'Z3')];
  const out = autoLayout(nodes, [{ source: 'a', target: 'b' }, { source: 'b', target: 'c' }]);
  assert.ok(out['Z1'].y < out['Z2'].y && out['Z2'].y < out['Z3'].y, JSON.stringify(out));
});

test('autoLayout : parent inconnu traité comme racine', () => {
  const out = autoLayout([card('a', 'fantôme')], []);
  assert.deepEqual(Object.keys(out), ['a']);
});

test('autoLayout : titre de zone jamais plus large que la zone', () => {
  const label = 'Direction des systèmes d\'information groupe';
  const out = autoLayout([zone('Z', null, label), card('a', 'Z')], []);
  assert.ok(out['Z'].w >= label.length * 9.5, `${out['Z'].w} < ${label.length * 9.5}`);
});

function rng(seed: number): () => number {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 2 ** 32);
}

test('autoLayout, 300 vues tirées au hasard : enfants dans leur zone, frères sans chevauchement', () => {
  const random = rng(7);
  for (let n = 0; n < 300; n++) {
    const nodes: LayoutNode[] = [];
    const zones: string[] = [];
    const count = 2 + Math.floor(random() * 25);
    for (let i = 0; i < count; i++) {
      const parent = zones.length && random() < 0.7 ? zones[Math.floor(random() * zones.length)] : null;
      if (random() < 0.25) { nodes.push(zone(`z${i}`, parent, 'Zone ' + i)); zones.push(`z${i}`); }
      else if (random() < 0.1) nodes.push({ id: `o${i}`, parentId: parent, w: 30, h: 30, zone: false, element: false, label: '' });
      else nodes.push(card(`c${i}`, parent, 'Composant ' + 'x'.repeat(Math.floor(random() * 30))));
    }
    const ids = nodes.map((x) => x.id);
    const edges: LayoutEdge[] = Array.from({ length: Math.floor(random() * count * 1.5) }, () =>
      ({ source: ids[Math.floor(random() * ids.length)], target: ids[Math.floor(random() * ids.length)] }));
    const out = autoLayout(nodes, edges);
    const ctx = JSON.stringify({ nodes, edges });
    assert.deepEqual(Object.keys(out).sort(), [...ids].sort(), ctx);
    for (const x of nodes) {
      if (x.parentId) assert.ok(inside(out[x.parentId], out[x.id]), `${x.id} hors de ${x.parentId} : ${ctx}`);
    }
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        if (nodes[i].parentId !== nodes[j].parentId) continue;
        assert.ok(!overlap(out[nodes[i].id], out[nodes[j].id]), `${nodes[i].id} chevauche ${nodes[j].id} : ${ctx}`);
      }
    }
  }
});

test('autoLayout, déterminisme : même dessin quel que soit l\'ordre des éléments et des liens (bascule, règle 8)', () => {
  const random = rng(3);
  const shuffle = <T>(a: T[]): T[] => {
    const b = [...a];
    for (let i = b.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [b[i], b[j]] = [b[j], b[i]]; }
    return b;
  };
  for (let n = 0; n < 200; n++) {
    const nodes: LayoutNode[] = [];
    const zones: string[] = [];
    const count = 3 + Math.floor(random() * 20);
    for (let i = 0; i < count; i++) {
      const parent = zones.length && random() < 0.6 ? zones[Math.floor(random() * zones.length)] : null;
      if (random() < 0.2) { nodes.push(zone(`z${i}`, parent)); zones.push(`z${i}`); }
      else nodes.push(card(`c${i}`, parent, 'Composant ' + i));
    }
    const ids = nodes.map((x) => x.id);
    const edges: LayoutEdge[] = Array.from({ length: Math.floor(random() * count * 1.5) }, () =>
      ({ source: ids[Math.floor(random() * ids.length)], target: ids[Math.floor(random() * ids.length)] }));
    const reference = autoLayout(nodes, edges);
    for (let k = 0; k < 3; k++) {
      // Tout est mélangé, parents et enfants compris.
      const again = autoLayout(shuffle(nodes), shuffle(edges));
      assert.deepEqual(again, reference, JSON.stringify({ nodes, edges }));
    }
  }
});
