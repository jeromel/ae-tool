// Tests de chains-reading.ts (écran « Chaînes et processus », EV-06), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { emptyText, groupByCapability, hueOf, moreText, touchedSummary, viaText } from '../src/app/chains-reading.ts';

const I = (id: string, capability: string | null, via: string | null = null, viaKind: string | null = null) =>
  ({ id, name: id.toUpperCase(), typeLabel: 'Application', via, viaKind, viaId: null, capability });

test('« via » nomme la nature du maillon, sauf l\'application sous un serveur', () => {
  assert.equal(viaText({ via: 'Relever les compteurs', viaKind: 'capacite' }), 'via la capacité Relever les compteurs');
  assert.equal(viaText({ via: 'Souscrire un abonnement', viaKind: 'processus' }), 'via le processus Souscrire un abonnement');
  assert.equal(viaText({ via: 'ONDINE', viaKind: 'application' }), 'via ONDINE');
  assert.equal(viaText({ via: 'écrit', viaKind: 'acces' }), 'écrit');
  assert.equal(viaText({ via: null, viaKind: null }), null);
});

test('cellule groupée par capacité de l\'étape, dans leur ordre ; le reste à la fin', () => {
  const groups = groupByCapability([I('a', 'c2'), I('b', 'c1'), I('c', 'c2'), I('d', null)], [{ id: 'c1', name: 'C1' }, { id: 'c2', name: 'C2' }]);
  assert.deepEqual(groups.map((g) => [g.capability?.name ?? '—', g.items.map((i) => i.id).join('')]), [['C1', 'b'], ['C2', 'ac'], ['—', 'd']]);
});

test('teinte du domaine par son rang, neutre au-delà de 9', () => {
  assert.equal(hueOf(0), 'bleu');
  assert.equal(hueOf(8), 'indigo');
  assert.equal(hueOf(9), null);
  assert.equal(hueOf(-1), null);
});

test('cellule vide : « Renseigner » sauf pour les projets, qui ne sont pas un trou', () => {
  assert.deepEqual(emptyText('applications'), { text: 'Aucune application', link: true });
  assert.deepEqual(emptyText('projets'), { text: 'Aucun projet', link: false });
});

test('résumé de l\'impact : il compte les étapes et les chaînes, accordés', () => {
  assert.equal(touchedSummary('VM-FACT-01', 'Serveur', 3, 1), 'VM-FACT-01 · Serveur — touche 3 étapes dans 1 chaîne.');
  assert.match(touchedSummary('VM-X', 'Serveur', 0, 0), /ne touche aucune étape/);
  assert.equal(moreText(7), '+ 2 autres');
  assert.equal(moreText(5), null);
});
