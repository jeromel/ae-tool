// Tests de impact-reading.ts (maquette « analyse d'impact selon l'objet, et qui sert qui »), hors navigateur.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { type ReachedItem, carriers, countersOf, readingOf, servesTable, subjectOf } from '../src/app/impact-reading.ts';

/**
 * « Gestion des contrats », capacité, en amont : Contrats Web et GED Juridique la portent ; srv-bdd-03 sert les deux ;
 * srv-app-12 sert Contrats Web ; VLAN 140 est sous srv-app-12 ; Signature électronique n'a aucune infrastructure.
 * Un projet est aussi en amont (il n'est ni ligne ni cellule).
 */
const R = (id: string, depth: number, from: string[], typeKey: string, layer: string): ReachedItem =>
  ({ id, name: id, typeKey, layer, depth, via: 'sert', direction: 'amont', from });
const CAP = 'cap';
const reached: ReachedItem[] = [
  R('Contrats Web', 1, [CAP], 'application', 'applicative'),
  R('GED Juridique', 1, [CAP], 'application', 'applicative'),
  R('Signature électronique', 1, [CAP], 'application', 'applicative'),
  R('Projet X', 1, [CAP], 'projet', 'transformation'),
  R('srv-app-12', 2, ['Contrats Web'], 'noeud', 'technique'),
  R('srv-bdd-03', 2, ['Contrats Web', 'GED Juridique'], 'noeud', 'technique'),
  R('srv-ged-01', 2, ['GED Juridique'], 'noeud', 'technique'),
  R('VLAN 140', 3, ['srv-app-12'], 'reseau', 'technique'),
  { ...R('Processus aval', 1, [CAP], 'processus', 'metier'), direction: 'aval' },
];
const types = { isApplication: (t: string) => t === 'application', isCapability: (t: string) => t === 'capacite' };

test('la couche décide du sens lu en premier et des titres (I1, I2)', () => {
  assert.equal(subjectOf('strategie', ['capacite']), 'porte');
  assert.equal(subjectOf('metier', []), 'porte');
  assert.equal(subjectOf('applicative', ['application']), 'application');
  assert.equal(subjectOf('technique', ['infrastructure']), 'technique');
  assert.equal(subjectOf('donnees', ['donnee']), 'donnee');
  assert.equal(readingOf('porte').first, 'amont');
  assert.equal(readingOf('porte').titles.amont, 'Ce qui la porte');
  assert.equal(readingOf('technique').first, 'aval');
  assert.equal(readingOf('donnee').titles.amont, 'Qui l\'utilise');
  assert.equal(readingOf('application').table, null);
});

test('compteurs : pour une capacité, les applications qui la portent, pas « 0 application en aval » (I3)', () => {
  const c = countersOf('porte', reached, types);
  assert.deepEqual(c.map((x) => `${x.value} ${x.label}`), ['3 applications qui la portent', '4 serveurs et réseaux en dessous']);
});

test('qui sert qui : une ligne par application, l\'infrastructure partagée sous chacune avec « aussi » (I4)', () => {
  const rows = servesTable(reached, 'amont', (r) => types.isApplication(r.typeKey), (r) => r.layer === 'technique');
  assert.deepEqual(rows.map((r) => r.name), ['Contrats Web', 'GED Juridique', 'Signature électronique']);
  const web = rows[0];
  assert.deepEqual(web.direct.map((i) => i.name), ['srv-app-12', 'srv-bdd-03']);
  assert.deepEqual(web.direct.find((i) => i.name === 'srv-bdd-03')!.also, ['GED Juridique']);
  assert.deepEqual(web.below.map((i) => i.name), ['VLAN 140'], 'au-delà du premier niveau : replié');
  assert.deepEqual(rows[1].direct.map((i) => `${i.name}:${i.also.join('+')}`), ['srv-bdd-03:Contrats Web', 'srv-ged-01:']);
  assert.equal(rows[2].direct.length + rows[2].below.length, 0, 'Signature : aucune infrastructure connue');
});

test('qui sert qui : on ne traverse pas une autre ligne, et l\'autre sens n\'entre pas dans le tableau', () => {
  const chained: ReachedItem[] = [
    R('A', 1, [CAP], 'application', 'applicative'),
    R('B', 2, ['A'], 'application', 'applicative'), // B alimente A par un flux
    R('srv-b', 3, ['B'], 'noeud', 'technique'),
  ];
  const rows = servesTable(chained, 'amont', (r) => types.isApplication(r.typeKey), (r) => r.layer === 'technique');
  assert.deepEqual(rows.map((r) => `${r.name}:${[...r.direct, ...r.below].map((i) => i.name).join(',')}`), ['A:', 'B:srv-b']);
  assert.equal(servesTable(reached, 'aval', (r) => types.isApplication(r.typeKey), () => true).length, 0);
});

test('depuis un serveur : applications touchées, capacités qu\'elles portent', () => {
  const down: ReachedItem[] = [
    { ...R('ONDINE', 1, ['srv'], 'application', 'applicative'), direction: 'aval' },
    { ...R('Facturer', 2, ['ONDINE'], 'capacite', 'strategie'), direction: 'aval' },
  ];
  const rows = servesTable(down, 'aval', (r) => types.isApplication(r.typeKey), (r) => types.isCapability(r.typeKey));
  assert.deepEqual(rows.map((r) => `${r.name}:${r.direct.map((i) => i.name).join(',')}`), ['ONDINE:Facturer']);
  assert.deepEqual(countersOf('technique', down, types).map((x) => x.value), [1, 1]);
});

test('lignes : une application qui ne porte la capacité que par le flux d\'une autre, ou par la capacité parente, n\'est pas une ligne', () => {
  const list: ReachedItem[] = [
    R('A', 1, [CAP], 'application', 'applicative'),
    R('Proc', 1, [CAP], 'processus', 'metier'),
    R('Parent', 1, [CAP], 'capacite', 'strategie'),
    R('P', 2, ['Proc'], 'application', 'applicative'), // sert le processus qui réalise la capacité : la porte
    R('F', 2, ['A'], 'application', 'applicative'), // alimente A par un flux : ne la porte pas
    R('Q', 2, ['Parent'], 'application', 'applicative'), // porte la capacité parente : ne porte pas celle-ci
  ];
  const rows = carriers(CAP, 'porte', list, types);
  assert.deepEqual([...rows].sort(), ['A', 'P']);
  assert.equal(countersOf('porte', list, types, rows)[0].value, 2);
});

test('cellule : un composant partagé se montre, mais n\'élargit pas le périmètre', () => {
  const list: ReachedItem[] = [
    R('App', 1, [CAP], 'application', 'applicative'),
    R('Windows', 2, ['App'], 'logiciel', 'technique'),
    R('VM-1', 3, ['Windows'], 'noeud', 'technique'),
    R('VM-2', 3, ['Windows'], 'noeud', 'technique'),
    R('VM-A', 2, ['App'], 'noeud', 'technique'),
    R('Grappe', 3, ['VM-A'], 'noeud', 'technique'),
  ];
  const [row] = servesTable(list, 'amont', (r) => r.id === 'App', (r) => r.layer === 'technique');
  assert.deepEqual(row.direct.map((i) => i.name), ['VM-A', 'Windows']);
  assert.deepEqual(row.below.map((i) => i.name), ['Grappe'], 'la grappe de la VM, pas les VM du logiciel partagé');
});

test('compteurs : avec un tableau, le second compte les objets distincts de ses cellules', () => {
  const rows = new Set(['Contrats Web', 'GED Juridique', 'Signature électronique']);
  const c = countersOf('porte', reached, types, rows, new Set(['srv-app-12', 'srv-bdd-03']));
  assert.deepEqual(c.map((x) => x.value), [3, 2]);
});
