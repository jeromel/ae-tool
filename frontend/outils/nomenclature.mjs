// Nomenclature logicielle (SBOM, format CycloneDX 1.5) des paquets livrés avec l'interface.
// Lit package-lock.json : aucun accès réseau, aucun paquet supplémentaire.
// Usage (depuis frontend/) : node outils/nomenclature.mjs > ../docs/nomenclature/interface.cdx.json
import { readFileSync } from 'node:fs';

const lock = JSON.parse(readFileSync(new URL('../package-lock.json', import.meta.url), 'utf8'));
const props = readFileSync(new URL('../../backend/Directory.Build.props', import.meta.url), 'utf8');
const version = /<Version>([^<]+)<\/Version>/.exec(props)?.[1] ?? '0.0.0';

// Les paquets marqués « dev » ne servent qu'à construire : ils ne sont pas livrés.
const shipped = Object.entries(lock.packages)
  .filter(([path, p]) => path.startsWith('node_modules/') && !p.dev)
  .map(([path, p]) => ({ name: path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length), ...p }))
  .sort((a, b) => a.name.localeCompare(b.name));

const component = (p) => ({
  type: 'library',
  name: p.name,
  version: p.version,
  purl: `pkg:npm/${p.name.replace('@', '%40')}@${p.version}`,
  ...(p.license ? { licenses: [{ license: { id: p.license } }] } : {}),
  ...(p.integrity?.startsWith('sha512-')
    ? { hashes: [{ alg: 'SHA-512', content: Buffer.from(p.integrity.slice('sha512-'.length), 'base64').toString('hex') }] }
    : {}),
});

const bom = {
  bomFormat: 'CycloneDX',
  specVersion: '1.5',
  version: 1,
  metadata: { component: { type: 'application', name: 'ae-tool-interface', version } },
  components: shipped.map(component),
};
process.stdout.write(JSON.stringify(bom, null, 2) + '\n');
