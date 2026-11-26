/**
 * Ajustement d'un nom dans sa case (charte d'interface, §2.1 : on ne coupe jamais un mot).
 *
 * Paliers, dans l'ordre : passage à la ligne entre deux mots, jusqu'à 3 lignes ; police réduite jusqu'à 11 px ; en
 * dernier recours, nom tronqué par « … » à la fin d'un mot (le nom complet reste au survol et dans le nom accessible).
 * Le palier 3, « la case grandit en hauteur », est dans card-grow.ts et s'applique au zoom courant (diagram-view, fitOf).
 *
 * Les lignes sont calculées ici, et non laissées au navigateur : c'est le seul moyen de garantir qu'aucun mot n'est
 * coupé. Dans le navigateur, la largeur est mesurée avec la police réellement affichée (« canvasMeasure ») : Segoe UI
 * manque hors de Windows, et la police de repli peut être bien plus large. Hors navigateur, une estimation prudente.
 * Module sans dépendance, pour être testé hors navigateur.
 */

export interface LabelFit {
  font: number;
  lines: string[];
  /** Vrai si le nom a été tronqué : le nom complet doit rester au survol. */
  truncated: boolean;
}

/** Largeur d'un texte en fraction de la taille de police. */
export type Measure = (text: string) => number;

export interface FitOptions {
  /** Mesure de la largeur ; par défaut, l'estimation prudente « emWidth ». Le navigateur passe une mesure réelle. */
  measure?: Measure;
  /** Plus grande police souhaitée. */
  max: number;
  /** Police minimale avant de tronquer (11 px selon la charte). */
  min?: number;
  /** Nombre de lignes au plus (3 selon la charte). */
  maxLines?: number;
  /** Interligne, en fraction de la police. */
  lineHeight?: number;
  /** Plancher absolu, quand même le premier mot ne tient pas à la police minimale. */
  floor?: number;
}

export const LABEL_MIN_FONT = 11;
const ELLIPSIS = '…';

/** Largeur estimée d'un texte, en fraction de la taille de police (graisse moyenne, police sans empattement). */
export function emWidth(text: string): number {
  let w = 0;
  for (const ch of text) {
    if (ch === ' ') w += 0.3;
    else if ('iljtfrI.,;:!\'’|()[]'.includes(ch)) w += 0.34;
    else if ('mwMW@'.includes(ch)) w += 0.9;
    else if (/[A-ZÀ-ÖØ-Þ]/.test(ch)) w += 0.7;
    else if (/[0-9]/.test(ch)) w += 0.58;
    else w += 0.56;
  }
  return w;
}

/**
 * Range les mots en lignes d'au plus « width » (en em). Retourne null si un mot, seul, dépasse la largeur :
 * il faudrait le couper, ce qui est interdit.
 */
export function wrapWords(words: string[], width: number, measure: Measure = emWidth): string[] | null {
  const lines: string[] = [];
  let current = '';
  for (const word of words) {
    if (measure(word) > width) return null;
    const candidate = current ? current + ' ' + word : word;
    if (current && measure(candidate) > width) {
      lines.push(current);
      current = word;
    } else current = candidate;
  }
  if (current) lines.push(current);
  return lines;
}

/** Lignes permises par la hauteur, plafonnées à maxLines, au moins une. */
function linesFor(h: number, font: number, lineHeight: number, maxLines: number): number {
  return Math.max(1, Math.min(maxLines, Math.floor(h / (font * lineHeight))));
}

/** Ajuste « text » dans une boîte de w × h (unités du plan). */
export function fitLabel(text: string, w: number, h: number, options: FitOptions): LabelFit {
  const words = (text ?? '').trim().split(/\s+/).filter((x) => x.length > 0);
  const lineHeight = options.lineHeight ?? 1.16;
  const maxLines = options.maxLines ?? 3;
  const min = Math.min(options.min ?? LABEL_MIN_FONT, options.max);
  const floor = Math.min(options.floor ?? 7, min);
  const measure = options.measure ?? emWidth;
  if (words.length === 0 || w <= 0) return { font: options.max, lines: [], truncated: false };
  w -= 1; // marge d'arrondi entre la mesure et le rendu

  // Paliers 1 et 2 : la plus grande police, de max à min, à laquelle le nom entier tient entre deux mots.
  for (let font = options.max; font >= min - 1e-9; font = Math.max(min, font - 0.5)) {
    const lines = wrapWords(words, w / font, measure);
    if (lines && lines.length <= linesFor(h, font, lineHeight, maxLines)) return { font, lines, truncated: false };
    if (font === min) break;
  }

  // Palier 4 : à la police minimale, on garde les mots entiers qui tiennent, et « … » à la fin du dernier.
  const truncated = truncate(words, w / min, linesFor(h, min, lineHeight, maxLines), measure);
  if (truncated) return { font: min, lines: truncated, truncated: true };

  // Même le premier mot ne tient pas à la police minimale : on réduit encore pour lui seul, jusqu'au plancher.
  const first = words.length > 1 ? words[0] + ' ' + ELLIPSIS : words[0];
  const font = Math.max(floor, Math.min(min, w / measure(first)));
  return { font, lines: [first], truncated: words.length > 1 || measure(first) * font > w };
}

/** Mots entiers rangés sur au plus « maxLines » lignes de « width » em, suivis de « … ». Null si aucun mot ne tient. */
function truncate(words: string[], width: number, maxLines: number, measure: Measure): string[] | null {
  const lines: string[] = [];
  let current = '';
  let used = 0;
  for (const word of words) {
    const candidate = current ? current + ' ' + word : word;
    if (measure(candidate) <= width) {
      current = candidate;
      used++;
      continue;
    }
    if (!current || lines.length + 1 >= maxLines || measure(word) > width) break;
    lines.push(current);
    current = word;
    used++;
  }
  if (current) lines.push(current);
  if (used === 0) return null;
  if (used === words.length) return lines; // ne devrait pas arriver : le nom tenait
  // Place pour « … » : on retire des mots de la dernière ligne jusqu'à ce qu'il tienne.
  let last = lines[lines.length - 1];
  while (measure(last + ' ' + ELLIPSIS) > width) {
    const cut = last.lastIndexOf(' ');
    if (cut < 0) {
      if (lines.length === 1) return null;
      lines.pop();
      last = lines[lines.length - 1];
      continue;
    }
    last = last.slice(0, cut);
  }
  lines[lines.length - 1] = last + ' ' + ELLIPSIS;
  return lines;
}

/**
 * Mesure réelle, dans le navigateur, avec la police de la page (graisse et famille données). Mise en cache par texte.
 * Retourne l'estimation si le canevas n'est pas disponible.
 */
export function canvasMeasure(weight: number, family: string): Measure {
  const ctx = typeof document !== 'undefined' ? document.createElement('canvas').getContext('2d') : null;
  if (!ctx) return emWidth;
  ctx.font = `${weight} 100px ${family}`;
  const cache = new Map<string, number>();
  return (text) => {
    let w = cache.get(text);
    if (w === undefined) {
      w = ctx.measureText(text).width / 100;
      cache.set(text, w);
    }
    return w;
  };
}
