// Tests de routing.ts (maquette « tracé des liens », étape 2), hors navigateur : node --test tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import type { Point, Rect } from '../src/app/geometry.ts';
import { type RouteRequest, SPREAD, routeAll, simplify, spread } from '../src/app/routing.ts';

const E = { x: 1, y: 0 }, W = { x: -1, y: 0 }, S = { x: 0, y: 1 }, N = { x: 0, y: -1 };

/** Lien du milieu du côté est de « a » au milieu du côté ouest de « b ». */
function eastToWest(id: string, a: Rect, b: Rect): RouteRequest {
  return { id, from: { at: { x: a.x + a.w, y: a.y + a.h / 2 }, dir: E }, to: { at: { x: b.x, y: b.y + b.h / 2 }, dir: W } };
}

function orthogonal(pts: Point[]): boolean {
  return pts.slice(1).every((q, i) => Math.abs(q.x - pts[i].x) < 0.01 || Math.abs(q.y - pts[i].y) < 0.01);
}

/** Segment qui entre dans l'intérieur strict de la boîte. */
function crosses(p: Point, q: Point, b: Rect): boolean {
  const l = Math.min(p.x, q.x), r = Math.max(p.x, q.x), t = Math.min(p.y, q.y), bt = Math.max(p.y, q.y);
  return l < b.x + b.w && r > b.x && t < b.y + b.h && bt > b.y;
}

test('simplify : points confondus et alignés retirés', () => {
  assert.deepEqual(simplify([{ x: 0, y: 0 }, { x: 0, y: 0 }, { x: 5, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }]),
    [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }]);
  assert.deepEqual(simplify([{ x: 0, y: 0 }, { x: 0, y: 5 }, { x: 0, y: 2 }]), [{ x: 0, y: 0 }, { x: 0, y: 2 }], 'retour sur soi aligné');
  assert.deepEqual(simplify([]), []);
});

test('routeAll : aucun lien, aucun tracé', () => {
  assert.equal(routeAll([], [{ x: 0, y: 0, w: 10, h: 10 }]).size, 0);
});

test('routeAll : deux cartes face à face, ligne droite', () => {
  const a = { x: 0, y: 0, w: 100, h: 60 }, b = { x: 300, y: 0, w: 100, h: 60 };
  const out = routeAll([eastToWest('l', a, b)], [a, b]);
  assert.deepEqual(out.get('l'), [{ x: 100, y: 30 }, { x: 300, y: 30 }]);
});

test('routeAll : une carte au milieu est contournée, à angle droit', () => {
  const a = { x: 0, y: 0, w: 100, h: 60 }, mid = { x: 200, y: -20, w: 100, h: 100 }, b = { x: 400, y: 0, w: 100, h: 60 };
  const pts = routeAll([eastToWest('l', a, b)], [a, mid, b]).get('l')!;
  assert.deepEqual(pts[0], { x: 100, y: 30 });
  assert.deepEqual(pts[pts.length - 1], { x: 400, y: 30 });
  assert.ok(orthogonal(pts), JSON.stringify(pts));
  for (let i = 1; i < pts.length; i++) assert.ok(!crosses(pts[i - 1], pts[i], mid), `passe sur la carte : ${JSON.stringify(pts)}`);
});

test('routeAll : lien enfermé, coude simple, il ne disparaît jamais', () => {
  // La cible est entourée d'un mur de cartes serrées : aucun chemin.
  const a = { x: 0, y: 0, w: 60, h: 40 };
  const b = { x: 500, y: 500, w: 60, h: 40 };
  const wall = [
    { x: 470, y: 470, w: 120, h: 28 }, { x: 470, y: 542, w: 120, h: 28 },
    { x: 470, y: 498, w: 28, h: 44 }, { x: 562, y: 498, w: 28, h: 44 },
  ];
  const req: RouteRequest = { id: 'l', from: { at: { x: 60, y: 20 }, dir: E }, to: { at: { x: 500, y: 520 }, dir: W } };
  const pts = routeAll([req], [a, b, ...wall]).get('l')!;
  // Coude au milieu des deux amorces (69 et 491) : il traverse le mur, mais le lien reste visible.
  assert.deepEqual(pts, [{ x: 60, y: 20 }, { x: 280, y: 20 }, { x: 280, y: 520 }, { x: 500, y: 520 }]);
});

test('spread : deux liens dans le même couloir écartés de 6 px', () => {
  const r1 = [{ x: 0, y: 0 }, { x: 0, y: 50 }, { x: 200, y: 50 }, { x: 200, y: 100 }];
  const r2 = [{ x: 20, y: 0 }, { x: 20, y: 50 }, { x: 180, y: 50 }, { x: 180, y: 100 }];
  spread([r1, r2], []);
  assert.equal(Math.abs(r1[1].y - r2[1].y), SPREAD);
  assert.equal(r1[1].y, r1[2].y, 'le segment reste horizontal');
  assert.deepEqual(r1[0], { x: 0, y: 0 }, 'les extrémités ne bougent pas');
});

test('spread : décalage refusé s\'il amène le lien sur une carte', () => {
  const r1 = [{ x: 0, y: 0 }, { x: 0, y: 50 }, { x: 200, y: 50 }, { x: 200, y: 100 }];
  const r2 = [{ x: 20, y: 0 }, { x: 20, y: 50 }, { x: 180, y: 50 }, { x: 180, y: 100 }];
  const card = { x: 50, y: 51, w: 100, h: 40 }; // juste sous le couloir
  spread([r1, r2], [card]);
  for (const r of [r1, r2]) assert.ok(!crosses(r[1], r[2], card), JSON.stringify(r));
});

function rng(seed: number): () => number {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 2 ** 32);
}

test('routeAll, 150 vues tirées au hasard : tracés orthogonaux, attachés, ne passant sur aucune autre carte', () => {
  const random = rng(11);
  const sides = [E, W, S, N];
  for (let n = 0; n < 150; n++) {
    // Cartes en grille irrégulière, couloirs d'au moins 40 px : jamais de lien enfermé.
    const cols = 2 + Math.floor(random() * 4), rows = 1 + Math.floor(random() * 4);
    const boxes: Rect[] = [];
    for (let i = 0; i < cols; i++) for (let j = 0; j < rows; j++) {
      boxes.push({ x: i * 220 + Math.floor(random() * 30), y: j * 140 + Math.floor(random() * 30), w: 120 + Math.floor(random() * 40), h: 50 + Math.floor(random() * 30) });
    }
    const attach = (b: Rect, d: Point): Point =>
      d.x > 0 ? { x: b.x + b.w, y: b.y + b.h / 2 } : d.x < 0 ? { x: b.x, y: b.y + b.h / 2 } : d.y > 0 ? { x: b.x + b.w / 2, y: b.y + b.h } : { x: b.x + b.w / 2, y: b.y };
    const reqs: (RouteRequest & { ends: [Rect, Rect] })[] = [];
    for (let k = 0; k < 1 + Math.floor(random() * 10); k++) {
      const s = boxes[Math.floor(random() * boxes.length)];
      const t = boxes[Math.floor(random() * boxes.length)];
      if (s === t) continue;
      const ds = sides[Math.floor(random() * 4)], dt = sides[Math.floor(random() * 4)];
      reqs.push({ id: `l${k}`, from: { at: attach(s, ds), dir: ds }, to: { at: attach(t, dt), dir: dt }, ends: [s, t] });
    }
    const out = routeAll(reqs, boxes);
    for (const r of reqs) {
      const pts = out.get(r.id)!;
      const ctx = JSON.stringify({ boxes, req: r, pts });
      assert.ok(pts && pts.length >= 2, ctx);
      assert.ok(orthogonal(pts), `pas orthogonal : ${ctx}`);
      assert.ok(Math.hypot(pts[0].x - r.from.at.x, pts[0].y - r.from.at.y) <= 0.5, `départ décroché : ${ctx}`);
      const last = pts[pts.length - 1];
      assert.ok(Math.hypot(last.x - r.to.at.x, last.y - r.to.at.y) <= 0.5, `arrivée décrochée : ${ctx}`);
      for (const b of boxes) {
        if (b === r.ends[0] || b === r.ends[1]) continue;
        for (let i = 1; i < pts.length; i++) assert.ok(!crosses(pts[i - 1], pts[i], b), `passe sur ${JSON.stringify(b)} : ${ctx}`);
      }
    }
  }
});
