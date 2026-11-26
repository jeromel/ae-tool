// Tests de label-fit.ts (charte §2.1 : on ne coupe jamais un mot), hors navigateur : node --test tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { LABEL_MIN_FONT, emWidth, fitLabel, wrapWords } from '../src/app/label-fit.ts';

/** Mesure simple et prévisible : chaque caractère vaut 0,5 em. */
const half = (t: string) => t.length * 0.5;

test('emWidth : espaces et lettres étroites plus courtes que les larges', () => {
  assert.equal(emWidth(''), 0);
  assert.ok(emWidth('iii') < emWidth('aaa'));
  assert.ok(emWidth('aaa') < emWidth('AAA'));
  assert.ok(emWidth('AAA') < emWidth('MMM'));
  assert.ok(Math.abs(emWidth('a b') - (0.56 * 2 + 0.3)) < 1e-9);
  assert.ok(emWidth('É') === emWidth('E'), 'capitale accentuée comptée comme capitale');
});

test('wrapWords : range les mots, refuse de couper un mot trop long', () => {
  assert.deepEqual(wrapWords(['ab', 'cd', 'ef'], 2.5, half), ['ab cd', 'ef']);
  assert.deepEqual(wrapWords(['ab', 'cd'], 10, half), ['ab cd']);
  assert.equal(wrapWords(['abcdefgh'], 2, half), null);
  assert.deepEqual(wrapWords([], 2, half), []);
});

test('fitLabel : nom vide ou case sans largeur, aucune ligne', () => {
  assert.deepEqual(fitLabel('', 100, 50, { max: 14 }), { font: 14, lines: [], truncated: false });
  assert.deepEqual(fitLabel('   ', 100, 50, { max: 14 }), { font: 14, lines: [], truncated: false });
  assert.deepEqual(fitLabel('Nom', 0, 50, { max: 14 }), { font: 14, lines: [], truncated: false });
});

test('fitLabel : palier 1, le nom tient à la plus grande police, sur une puis plusieurs lignes', () => {
  // « Gestion clients » : 15 car. × 0,5 × 14 = 105 px.
  assert.deepEqual(fitLabel('Gestion clients', 200, 20, { max: 14, measure: half }), { font: 14, lines: ['Gestion clients'], truncated: false });
  // Trop étroit pour une ligne, assez haut pour deux.
  assert.deepEqual(fitLabel('Gestion clients', 60, 40, { max: 14, measure: half }), { font: 14, lines: ['Gestion', 'clients'], truncated: false });
});

test('fitLabel : palier 2, la police baisse avant de tronquer, jamais sous le minimum', () => {
  // Une ligne permise ; à 14 px il faut 105 px, la case en offre 90 : 12 px.
  assert.deepEqual(fitLabel('Gestion clients', 91, 14, { max: 14, measure: half, maxLines: 1 }), { font: 12, lines: ['Gestion clients'], truncated: false });
  // À 11 px il faudrait 82,5 px pour 80 : on tronque plutôt que descendre sous le minimum.
  const fit = fitLabel('Gestion clients', 81, 14, { max: 14, measure: half, maxLines: 1 });
  assert.deepEqual(fit, { font: LABEL_MIN_FONT, lines: ['Gestion …'], truncated: true });
});

test('fitLabel : palier 4, tronqué à la fin d\'un mot, « … » ajouté, nom complet signalé', () => {
  const fit = fitLabel('Référentiel des contrats fournisseurs groupe', 80, 14, { max: 14, measure: half, maxLines: 1 });
  assert.equal(fit.truncated, true);
  assert.equal(fit.font, LABEL_MIN_FONT);
  assert.equal(fit.lines.length, 1);
  assert.match(fit.lines[0], / …$/);
});

test('fitLabel : un seul mot trop long, police réduite jusqu\'au plancher, jamais coupé', () => {
  const fit = fitLabel('Interopérabilité', 50, 14, { max: 14, measure: half, maxLines: 1 });
  assert.deepEqual(fit.lines, ['Interopérabilité']);
  assert.ok(fit.font < LABEL_MIN_FONT && fit.font >= 7, `police ${fit.font}`);
  // Même au plancher il ne tient pas : signalé tronqué (débordement), le nom reste entier.
  const tiny = fitLabel('Interopérabilité', 20, 14, { max: 14, measure: half, maxLines: 1 });
  assert.deepEqual(tiny.lines, ['Interopérabilité']);
  assert.equal(tiny.font, 7);
  assert.equal(tiny.truncated, true);
});

/** Générateur pseudo-aléatoire reproductible. */
function rng(seed: number): () => number {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 2 ** 32);
}

test('fitLabel, 5 000 tirages : aucun mot coupé, au plus 3 lignes, chaque ligne tient sauf au plancher', () => {
  const random = rng(42);
  const vocab = ['SI', 'Gestion', 'des', 'clients', 'Référentiel', 'interopérabilité', 'API', 'paie', 'Facturation', 'MDM',
    'CRM', 'contrats', 'fournisseurs', 'Plateforme', 'données', 'à', 'et', 'Wwwwwwwwww', 'iiiiiiii', 'Échanges'];
  for (let n = 0; n < 5000; n++) {
    const words = Array.from({ length: 1 + Math.floor(random() * 7) }, () => vocab[Math.floor(random() * vocab.length)]);
    const text = words.join(' ');
    const w = 20 + random() * 220;
    const h = 10 + random() * 70;
    const fit = fitLabel(text, w, h, { max: 14 });
    const ctx = `« ${text} » dans ${w.toFixed(1)} × ${h.toFixed(1)} → ${JSON.stringify(fit)}`;
    assert.ok(fit.lines.length >= 1 && fit.lines.length <= 3, ctx);
    const shown = fit.lines.join(' ').split(' ').filter((x) => x !== '…');
    // Les mots affichés sont exactement les premiers mots du nom, entiers et dans l'ordre.
    assert.deepEqual(shown, words.slice(0, shown.length), ctx);
    assert.equal(fit.truncated || shown.length === words.length, true, ctx);
    if (!fit.truncated) assert.equal(shown.length, words.length, ctx);
    if (shown.length < words.length) assert.equal(fit.truncated, true, ctx);
    if (fit.font >= LABEL_MIN_FONT) {
      for (const line of fit.lines) assert.ok(emWidth(line) * fit.font <= w - 1 + 1e-9, `ligne trop large : ${ctx}`);
    }
    assert.ok(fit.font >= 7 && fit.font <= 14, ctx);
  }
});
