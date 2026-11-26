// Tests de lineage-selection.ts (maquette « parcours et sélection », §2, §3 et §6), hors navigateur : node --test tests/
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { type SelLineage, flowPill, flowTag, key, lit, stillThere } from '../src/app/lineage-selection.ts';

/**
 * Parcours d'essai : HYDROVISION envoie FX-12 (Index, Abonné) à ONDINE ; ONDINE envoie un flux sans code ni donnée à FINEO.
 * FX-12 passe par Export SFTP (VM-CLI-02) puis Import batch (VM-FIN-01). ONDINE est hébergée sur VM-CLI-02.
 */
const L: SelLineage = {
  data: [
    { id: 'idx', name: 'Index de consommation', code: null, roles: [{ appId: 'hydro' }] },
    { id: 'abo', name: 'Abonné', code: null, roles: [{ appId: 'ondine' }] },
    { id: 'ctr', name: 'Contrat', code: null, roles: [{ appId: 'ondine' }] },
  ],
  applications: [
    { id: 'hydro', name: 'HYDROVISION', hostIds: [] },
    { id: 'ondine', name: 'ONDINE', hostIds: ['vm-cli'] },
    { id: 'fineo', name: 'FINEO', hostIds: [] },
  ],
  flows: [
    { id: 'f12', code: 'FX-12', name: 'Relevés vers facturation', sourceId: 'hydro', targetId: 'ondine', dataIds: ['idx', 'abo'],
      steps: [{ technicalId: 'sftp' }, { technicalId: 'batch' }] },
    { id: 'f2', code: null, name: 'Factures', sourceId: 'ondine', targetId: 'fineo', dataIds: [], steps: [] },
  ],
  technical: [
    { id: 'sftp', name: 'Export SFTP', hostIds: ['vm-cli'] },
    { id: 'batch', name: 'Import batch', hostIds: ['vm-fin'] },
  ],
  hosts: [
    { id: 'vm-cli', name: 'VM-CLI-02', appIds: ['ondine'] },
    { id: 'vm-fin', name: 'VM-FIN-01', appIds: [] },
    { id: 'vm-esb', name: 'VM-ESB-01', appIds: [] },
  ],
  profiles: [{ id: 'pc', name: 'PC bureautique', appIds: ['ondine'] }],
};

const on = (g: ReturnType<typeof lit>) => [...g.keys].sort();

test('aucune sélection : rien d\'estompé, aucune chaîne, une invitation (règle 3)', () => {
  const g = lit(L, null);
  assert.equal(g.active, false);
  assert.equal(g.chainFlowId, null);
  assert.match(g.titles.chaine, /choisissez un flux/);
});

test('flux : ses données, ses deux applications, sa chaîne et les serveurs de sa chaîne (S1)', () => {
  const g = lit(L, { kind: 'flux', id: 'f12' });
  assert.equal(g.chainFlowId, 'f12');
  assert.deepEqual(on(g), [
    'application:hydro', 'application:ondine', 'donnee:abo', 'donnee:idx', 'etape:batch', 'etape:sftp', 'flux:f12', 'hote:vm-cli', 'hote:vm-fin',
  ].sort());
  assert.equal(g.titles.donnees, 'Données · 2 transportées par FX-12 Relevés vers facturation');
  assert.equal(g.titles.applications, 'Applications et flux · FX-12 de HYDROVISION vers ONDINE');
  assert.equal(g.titles.chaine, 'Chaîne technique de FX-12 Relevés vers facturation · 2 étapes');
  assert.equal(g.titles.hebergement, 'Hébergement · 2 serveurs portent cette chaîne');
});

test('flux sans donnée renseignée ni chaîne : les deux le disent (§6)', () => {
  const g = lit(L, { kind: 'flux', id: 'f2' });
  assert.equal(g.titles.donnees, 'Données · 2 : données transportées non renseignées au catalogue');
  assert.equal(g.titles.chaine, 'Chaîne technique de 2 Factures : non décrite au catalogue');
  assert.ok(![...g.keys].some((k) => k.startsWith('donnee:')));
});

test('flux dont une donnée n\'est pas affichée (dérivée non suivie) : signalé dans le titre (§6)', () => {
  const partial: SelLineage = { ...L, data: L.data.filter((d) => d.id !== 'abo') };
  assert.match(lit(partial, { kind: 'flux', id: 'f12' }).titles.donnees, /dont 1 non affichée/);
});

test('donnée : applications qui la tiennent, flux qui la transportent ; un seul flux, sa chaîne', () => {
  const g = lit(L, { kind: 'donnee', id: 'abo' });
  assert.equal(g.chainFlowId, 'f12');
  assert.ok(g.keys.has(key('application', 'ondine')));
  assert.ok(g.keys.has(key('flux', 'f12')));
  assert.ok(g.keys.has(key('etape', 'sftp')));
  assert.ok(!g.keys.has(key('flux', 'f2')));
  assert.ok(!g.keys.has(key('donnee', 'idx')), 'une seule donnée allumée : la sienne');
});

test('donnée transportée par plusieurs flux : pas de chaîne, une invitation chiffrée', () => {
  const two: SelLineage = { ...L, flows: L.flows.map((f) => (f.id === 'f2' ? { ...f, dataIds: ['abo'] } : f)) };
  const g = lit(two, { kind: 'donnee', id: 'abo' });
  assert.equal(g.chainFlowId, null);
  assert.equal(g.titles.chaine, 'Chaîne technique : 2 flux transportent Abonné : choisissez-en un');
});

test('application : ses données tenues ou reçues, ses flux, ses serveurs ; pas de chaîne', () => {
  const g = lit(L, { kind: 'application', id: 'ondine' });
  assert.equal(g.chainFlowId, null);
  for (const k of ['donnee:abo', 'donnee:ctr', 'donnee:idx', 'flux:f12', 'flux:f2', 'hote:vm-cli', 'application:ondine']) assert.ok(g.keys.has(k), k);
  assert.ok(!g.keys.has(key('application', 'fineo')), 'les autres applications restent estompées (table du §3)');
});

test('étape : ses flux, leurs données, ses serveurs, montrée dans sa chaîne', () => {
  const g = lit(L, { kind: 'etape', id: 'batch' });
  assert.equal(g.chainFlowId, 'f12');
  assert.deepEqual(on(g), ['donnee:abo', 'donnee:idx', 'etape:batch', 'flux:f12', 'hote:vm-fin'].sort());
});

test('serveur : applications hébergées, étapes portées, flux et données qui y passent', () => {
  const g = lit(L, { kind: 'hote', id: 'vm-fin' });
  assert.deepEqual(on(g), ['donnee:abo', 'donnee:idx', 'etape:batch', 'flux:f12', 'hote:vm-fin'].sort());
  const esb = lit(L, { kind: 'hote', id: 'vm-esb' });
  assert.deepEqual(on(esb), ['hote:vm-esb'], 'serveur sans rien : lui seul');
});

test('objet inconnu : comme aucune sélection, sans erreur', () => {
  assert.equal(lit(L, { kind: 'flux', id: 'absent' }).active, false);
  assert.equal(stillThere(L, { kind: 'flux', id: 'absent' }), false);
  assert.equal(stillThere(L, { kind: 'etape', id: 'sftp' }), true);
});

test('pastille : code, sinon numéro dans l\'ordre du parcours ; nombre de données, « ? » si rien (P3)', () => {
  assert.equal(flowTag(L, 'f12'), 'FX-12');
  assert.equal(flowTag(L, 'f2'), '2');
  assert.equal(flowPill(L, 'f12'), 'FX-12 · 2');
  assert.equal(flowPill(L, 'f2'), '2 · ?');
});
