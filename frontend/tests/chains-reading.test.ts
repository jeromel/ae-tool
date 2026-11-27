// Tests de chains-reading.ts (écran « Chaînes et processus », EV-06), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { OFF_CHAIN, cellLine, chainQuery, choosableLayers, domainQuery, emptyText, hueOf, layerQuery, moreDirectText, objectQuery, offMark, openChain, stepsByChain, tabMark, touchedSummary, viaText } from '../src/app/chains-reading.ts';

const I = (id: string, capability: string | null, via: string | null = null, viaKind: string | null = null) =>
  ({ id, name: id.toUpperCase(), typeLabel: 'Application', via, viaKind, viaId: null, capability });

test('« via » nomme la nature du maillon, sauf l\'application sous un serveur', () => {
  assert.equal(viaText({ via: 'Relever les compteurs', viaKind: 'capacite' }), 'via la capacité Relever les compteurs');
  assert.equal(viaText({ via: 'Souscrire un abonnement', viaKind: 'processus' }), 'via le processus Souscrire un abonnement');
  assert.equal(viaText({ via: 'ONDINE', viaKind: 'application' }), 'via ONDINE');
  assert.equal(viaText({ via: 'écrit', viaKind: 'acces' }), 'écrit');
  assert.equal(viaText({ via: null, viaKind: null }), null);
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
  assert.equal(moreDirectText(8), '+ 2 autres');
  assert.equal(moreDirectText(7), '+ 1 autre');
  assert.equal(moreDirectText(6), null);
});

test('C10 : chaque réglage ne touche que sa clé de l\'adresse ; changer de couche garde l\'objet désigné', () => {
  const merge = (q: Record<string, string | null>, change: Record<string, string | null>) => ({ ...q, ...change });
  let q: Record<string, string | null> = { chaine: 'ch-eau', couche: null, objet: null, domaine: null };
  q = merge(q, objectQuery('vm-fact-01'));
  assert.equal(q['chaine'], null, 'désigner un objet rouvre sur la première chaîne touchée (revue §4.1)');
  q = merge(q, chainQuery('ch-incident'));
  q = merge(q, layerQuery('serveurs'));
  assert.deepEqual(q, { chaine: 'ch-incident', couche: 'serveurs', objet: 'vm-fact-01', domaine: null });
  q = merge(q, domainQuery(null, 'dom-1'));
  q = merge(q, layerQuery('aucune'));
  assert.deepEqual(q, { chaine: 'ch-incident', couche: null, objet: 'vm-fact-01', domaine: 'dom-1' });
  assert.deepEqual(objectQuery(null), { objet: null }, 'effacer l\'objet garde la chaîne ouverte');
  assert.deepEqual(domainQuery('dom-1', 'dom-1'), { domaine: null }, 'second clic : annule le filtre');
});

test('ligne d\'une couche : noms dédoublonnés, directs d\'abord, indirects regroupés par nature', () => {
  const items = [I('ondine', 'c1'), I('ondine', 'c2'), I('mee', 'c1', 'Souscrire', 'processus'), I('capta', 'c3', 'Relever', 'capacite'), I('mee', 'c2', 'Autre', 'processus')];
  const line = cellLine(items, 'applications');
  assert.deepEqual(line.direct.map((i) => i.id), ['ondine'], 'ONDINE une seule fois');
  assert.deepEqual(line.indirect.map((g) => [g.label, g.items.map((i) => i.id).join(',')]), [['par processus', 'mee'], ['par sous-capacité', 'capta']]);
});

test('serveurs et projets : le « via » est leur chemin normal, tous directs', () => {
  const line = cellLine([I('vm1', 'c1', 'ONDINE', 'application'), I('vm1', 'c1', 'RECOUVR', 'application')], 'serveurs');
  assert.deepEqual([line.direct.map((i) => i.id), line.indirect.length], [['vm1'], 0]);
});

test('onglet touché : « ● N étapes », rien sinon', () => {
  assert.equal(tabMark(3), '● 3 étapes');
  assert.equal(tabMark(1), '● 1 étape');
  assert.equal(tabMark(0), null);
});

test('couches au choix : plus de bouton « Capacités », « Aucune » en dernier (arbitrage PO 27/11)', () => {
  assert.deepEqual(choosableLayers(['capacites', 'processus', 'applications', 'serveurs', 'projets']), ['processus', 'applications', 'serveurs', 'projets', 'aucune']);
  assert.deepEqual(layerQuery('aucune'), { couche: null }, '« Aucune » est la couche par défaut : rien dans l\'adresse');
});

test('onglet ouvert : celui de l\'adresse, sinon la première chaîne touchée, sinon la première chaîne', () => {
  const ids = ['ch-facture', 'ch-incident', 'ch-eau'];
  const none = new Map<string, number>();
  const touched = stepsByChain([{ chainId: 'ch-incident' }, { chainId: 'ch-eau' }, { chainId: 'ch-incident' }]);
  assert.equal(touched.get('ch-incident'), 2);
  assert.equal(openChain(ids, null, none, true), 'ch-facture');
  assert.equal(openChain(ids, null, touched, true), 'ch-incident');
  assert.equal(openChain(ids, 'ch-facture', touched, true), 'ch-facture', 'un onglet non touché reste ouvrable');
  assert.equal(openChain(ids, 'inconnue', none, true), 'ch-facture');
  assert.equal(openChain(ids, OFF_CHAIN, none, true), OFF_CHAIN);
  assert.equal(openChain(ids, OFF_CHAIN, none, false), 'ch-facture', 'pas d\'onglet Hors chaîne sans processus hors chaîne');
  assert.equal(openChain([], null, none, true), OFF_CHAIN);
  assert.equal(offMark(2), '● 2 touchés');
  assert.equal(offMark(0), null);
});

test('§6 de la revue : une application arrivée par deux capacités de la même étape est une seule entrée', () => {
  const line = cellLine([I('ondine', 'c1'), I('ondine', 'c2', 'Souscrire', 'processus'), I('mee', 'c1')], 'applications');
  assert.deepEqual(line.direct.map((i) => i.id), ['ondine', 'mee']);
  assert.equal(line.indirect.length, 0, 'déjà directe : pas répétée dans les indirects');
});
