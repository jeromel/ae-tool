/**
 * Sélection dans le parcours (maquette « parcours et sélection » : une seule sélection, qui traverse toutes les bandes).
 *
 * Pour la sélection en cours, dit ce qui est allumé (relié) dans chaque bande, le flux dont la chaîne technique est
 * montrée, et le titre de chaque bande. Le reste est estompé. Sans sélection, rien n'est estompé et aucune chaîne n'est
 * montrée. Module sans dépendance, pour être testé hors navigateur.
 */

export type Kind = 'donnee' | 'application' | 'flux' | 'etape' | 'hote' | 'poste';
export interface Selection { kind: Kind; id: string; }

/** Ce dont la sélection a besoin du parcours (les types de la page sont plus riches). */
export interface SelData { id: string; name: string; code: string | null; roles: { appId: string }[]; }
export interface SelApp { id: string; name: string; hostIds: string[]; }
export interface SelFlow { id: string; code: string | null; name: string; sourceId: string; targetId: string; dataIds: string[]; steps: { technicalId: string }[]; }
export interface SelTechnical { id: string; name: string; hostIds: string[]; }
export interface SelHost { id: string; name: string; appIds: string[]; }
export interface SelProfile { id: string; name: string; appIds: string[]; }
export interface SelLineage {
  data: SelData[]; applications: SelApp[]; flows: SelFlow[]; technical: SelTechnical[]; hosts: SelHost[]; profiles: SelProfile[];
}

export interface Lit {
  /** Vrai si une sélection est en cours : ce qui n'est pas allumé est estompé. */
  active: boolean;
  /** Clés « kind:id » allumées. */
  keys: Set<string>;
  /** Flux dont la chaîne technique est montrée, ou null (invitation à choisir un flux). */
  chainFlowId: string | null;
  titles: { donnees: string; applications: string; chaine: string; hebergement: string };
}

export const key = (kind: Kind, id: string) => `${kind}:${id}`;

/** Pastille d'un flux : son code, sinon un numéro court dans l'ordre du parcours (charte §3.3, P3). */
export function flowTag(l: Pick<SelLineage, 'flows'>, flowId: string): string {
  const i = l.flows.findIndex((f) => f.id === flowId);
  const f = l.flows[i];
  return f?.code?.trim() || String(i + 1);
}

/** Pastille avec le nombre de données transportées : « FX-12 · 2 », « 3 · ? » quand rien n'est renseigné. */
export function flowPill(l: Pick<SelLineage, 'flows'>, flowId: string): string {
  const f = l.flows.find((x) => x.id === flowId);
  return `${flowTag(l, flowId)} · ${f && f.dataIds.length ? f.dataIds.length : '?'}`;
}

const plural = (n: number, one: string, many: string) => `${n} ${n > 1 ? many : one}`;

export function lit(l: SelLineage, sel: Selection | null): Lit {
  const tech = new Map(l.technical.map((t) => [t.id, t]));
  const appName = (id: string) => l.applications.find((a) => a.id === id)?.name ?? id;
  const keys = new Set<string>();
  const add = (kind: Kind, ids: Iterable<string>) => { for (const id of ids) keys.add(key(kind, id)); };
  const flowLabel = (f: SelFlow) => `${flowTag(l, f.id)} ${f.name}`;
  const chainHosts = (f: SelFlow) => f.steps.flatMap((s) => tech.get(s.technicalId)?.hostIds ?? []);
  const hostsOfApps = (apps: Set<string>) => l.hosts.filter((h) => h.appIds.some((a) => apps.has(a))).map((h) => h.id);
  const shownData = new Set(l.data.map((d) => d.id));

  const none: Lit = {
    active: false, keys, chainFlowId: null,
    titles: { donnees: 'Données', applications: 'Applications et flux applicatifs', chaine: 'Chaîne technique : choisissez un flux pour la voir',
      hebergement: 'Hébergement' },
  };
  if (!sel) return none;

  let chain: SelFlow | null = null;
  let chainInvite = 'choisissez un flux pour la voir';
  const titles = { ...none.titles };

  switch (sel.kind) {
    case 'flux': {
      const f = l.flows.find((x) => x.id === sel.id);
      if (!f) return none;
      chain = f;
      add('flux', [f.id]);
      add('application', [f.sourceId, f.targetId]);
      add('donnee', f.dataIds);
      add('etape', f.steps.map((s) => s.technicalId));
      const hosts = new Set(chainHosts(f));
      add('hote', hosts);
      const shown = f.dataIds.filter((d) => shownData.has(d)).length;
      const hidden = f.dataIds.length - shown;
      titles.donnees = f.dataIds.length === 0
        ? `Données · ${flowTag(l, f.id)} : données transportées non renseignées au catalogue`
        : `Données · ${plural(f.dataIds.length, 'transportée', 'transportées')} par ${flowLabel(f)}`
          + (hidden ? `, dont ${plural(hidden, 'non affichée', 'non affichées')} (cocher « Suivre les données dérivées »)` : '');
      titles.applications = `Applications et flux · ${flowTag(l, f.id)} de ${appName(f.sourceId)} vers ${appName(f.targetId)}`;
      titles.hebergement = `Hébergement · ${plural(hosts.size, 'serveur porte', 'serveurs portent')} cette chaîne`;
      break;
    }
    case 'donnee': {
      const d = l.data.find((x) => x.id === sel.id);
      if (!d) return none;
      const carriers = l.flows.filter((f) => f.dataIds.includes(d.id));
      const apps = new Set(d.roles.map((r) => r.appId));
      add('donnee', [d.id]);
      add('application', apps);
      add('flux', carriers.map((f) => f.id));
      if (carriers.length === 1) chain = carriers[0];
      else if (carriers.length > 1) chainInvite = `${carriers.length} flux transportent ${d.name} : choisissez-en un`;
      else chainInvite = `aucun flux ne transporte ${d.name}`;
      if (chain) add('etape', chain.steps.map((s) => s.technicalId));
      const hosts = new Set([...hostsOfApps(apps), ...carriers.flatMap(chainHosts)]);
      add('hote', hosts);
      titles.donnees = `Données · ${d.name}`;
      titles.applications = `Applications et flux · ${plural(apps.size, 'application la tient', 'applications la tiennent')}, ${plural(carriers.length, 'flux la transporte', 'flux la transportent')}`;
      titles.hebergement = `Hébergement · ${plural(hosts.size, 'serveur où elle est stockée ou transite', 'serveurs où elle est stockée ou transite')}`;
      break;
    }
    case 'application': {
      const a = l.applications.find((x) => x.id === sel.id);
      if (!a) return none;
      const flows = l.flows.filter((f) => f.sourceId === a.id || f.targetId === a.id);
      const data = new Set([...l.data.filter((d) => d.roles.some((r) => r.appId === a.id)).map((d) => d.id),
        ...flows.filter((f) => f.targetId === a.id).flatMap((f) => f.dataIds)]);
      add('application', [a.id]);
      add('flux', flows.map((f) => f.id));
      add('donnee', data);
      const hosts = new Set([...a.hostIds, ...hostsOfApps(new Set([a.id]))]);
      add('hote', hosts);
      titles.donnees = `Données · ${plural(data.size, 'tenue ou reçue', 'tenues ou reçues')} par ${a.name}`;
      titles.applications = `Applications et flux · ${a.name} et ${plural(flows.length, 'flux', 'flux')}`;
      titles.hebergement = `Hébergement · ${plural(hosts.size, 'serveur', 'serveurs')} de ${a.name}`;
      break;
    }
    case 'etape': {
      const t = tech.get(sel.id);
      if (!t) return none;
      const flows = l.flows.filter((f) => f.steps.some((s) => s.technicalId === t.id));
      // L'étape est montrée dans sa chaîne ; si plusieurs flux passent par elle, celle du premier.
      chain = flows[0] ?? null;
      add('etape', [t.id]);
      add('flux', flows.map((f) => f.id));
      const data = new Set(flows.flatMap((f) => f.dataIds));
      add('donnee', data);
      add('hote', t.hostIds);
      titles.donnees = `Données · ${plural(data.size, 'transportée', 'transportées')} par les flux que réalise ${t.name}`;
      titles.applications = `Applications et flux · ${plural(flows.length, 'flux réalisé', 'flux réalisés')} par ${t.name}`;
      titles.hebergement = `Hébergement · ${plural(t.hostIds.length, 'serveur', 'serveurs')} de ${t.name}`;
      break;
    }
    case 'hote': {
      const h = l.hosts.find((x) => x.id === sel.id);
      if (!h) return none;
      const apps = new Set(h.appIds);
      const steps = l.technical.filter((t) => t.hostIds.includes(h.id)).map((t) => t.id);
      const stepSet = new Set(steps);
      const flows = l.flows.filter((f) => apps.has(f.sourceId) || apps.has(f.targetId) || f.steps.some((s) => stepSet.has(s.technicalId)));
      const data = new Set([...l.data.filter((d) => d.roles.some((r) => apps.has(r.appId))).map((d) => d.id),
        ...flows.filter((f) => f.steps.some((s) => stepSet.has(s.technicalId))).flatMap((f) => f.dataIds)]);
      add('hote', [h.id]);
      add('application', apps);
      add('flux', flows.map((f) => f.id));
      add('etape', steps);
      add('donnee', data);
      titles.donnees = `Données · ${plural(data.size, 'stockée ou en transit', 'stockées ou en transit')} sur ${h.name}`;
      titles.applications = `Applications et flux · ${plural(apps.size, 'application', 'applications')} et ${plural(flows.length, 'flux', 'flux')} passant par ${h.name}`;
      titles.hebergement = `Hébergement · ${h.name}`;
      break;
    }
    case 'poste': {
      // Hors de la table de la maquette : le profil et les applications auxquelles il donne accès.
      const p = l.profiles.find((x) => x.id === sel.id);
      if (!p) return none;
      add('poste', [p.id]);
      add('application', p.appIds);
      titles.applications = `Applications et flux · ${plural(p.appIds.length, 'application accessible', 'applications accessibles')} depuis ${p.name}`;
      break;
    }
  }
  titles.chaine = chain
    ? `Chaîne technique de ${flowLabel(chain)} · ${plural(chain.steps.length, 'étape', 'étapes')}`
    : `Chaîne technique : ${chainInvite}`;
  if (chain && chain.steps.length === 0) titles.chaine = `Chaîne technique de ${flowLabel(chain)} : non décrite au catalogue`;
  return { active: true, keys, chainFlowId: chain?.id ?? null, titles };
}

/** Vrai si la sélection désigne encore quelque chose du parcours affiché (filtre « constaté seulement »). */
export function stillThere(l: SelLineage, sel: Selection): boolean {
  switch (sel.kind) {
    case 'donnee': return l.data.some((x) => x.id === sel.id);
    case 'application': return l.applications.some((x) => x.id === sel.id);
    case 'flux': return l.flows.some((x) => x.id === sel.id);
    case 'etape': return l.flows.some((f) => f.steps.some((s) => s.technicalId === sel.id));
    case 'hote': return l.hosts.some((x) => x.id === sel.id);
    case 'poste': return l.profiles.some((x) => x.id === sel.id);
  }
}
