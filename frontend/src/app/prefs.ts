/**
 * Préférences d'affichage propres au navigateur (panneaux repliés, thème du plan).
 * Le stockage local peut être indisponible ou refusé : dans ce cas la valeur par défaut s'applique, sans erreur.
 */
export function readPref(key: string, fallback: boolean): boolean {
  try {
    const v = localStorage.getItem('ae.' + key);
    return v === null ? fallback : v === '1';
  } catch {
    return fallback;
  }
}

export function writePref(key: string, value: boolean): void {
  try {
    localStorage.setItem('ae.' + key, value ? '1' : '0');
  } catch {
    /* préférence non mémorisée : sans conséquence */
  }
}

export function readText(key: string, fallback: string): string {
  try {
    return localStorage.getItem('ae.' + key) ?? fallback;
  } catch {
    return fallback;
  }
}

export function writeText(key: string, value: string): void {
  try {
    localStorage.setItem('ae.' + key, value);
  } catch {
    /* préférence non mémorisée : sans conséquence */
  }
}
