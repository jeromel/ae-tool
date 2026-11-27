/** Premier type actif portant le rôle (à défaut, un type archivé), comme `Metamodel.FirstTypeWith` côté serveur. */
export function firstTypeWith(meta: any, role: string): string {
  const types = (meta.objectTypes as any[]).filter((t) => t.roles.includes(role));
  return (types.find((t) => !t.archived) ?? types[0])?.key ?? '';
}
