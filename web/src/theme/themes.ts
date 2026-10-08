// The Study Stash look on a phone: the app's twenty colour themes (ColourThemes.cs) and the Mac's Liquid Glass tokens
// worked out from them the way Skin.cs does, as CSS oklch() colours.

/** A colour theme: the accent's hue, chroma and lightness, and the faint hue the neutral surfaces lean to. */
export interface ColourTheme {
  name: string;
  h: number;
  c: number;
  l: number;
  nh: number;
  nc: number;
  /** The highlighter's hue. */
  hl: number;
}

const t = (name: string, h: number, c: number, l: number, nh: number, nc: number, hl: number): ColourTheme => ({
  name,
  h,
  c,
  l,
  nh,
  nc,
  hl,
});

/** The twenty themes, in the picker's order. Lagoon (teal) is the default. */
export const themes: readonly ColourTheme[] = [
  t('Lagoon', 195, 0.12, 0.58, 210, 0.012, 95),
  t('Library', 160, 0.1, 0.5, 80, 0.014, 95),
  t('Blueprint', 262, 0.19, 0.55, 250, 0.014, 200),
  t('Marmalade', 50, 0.17, 0.64, 75, 0.018, 95),
  t('Plum', 330, 0.14, 0.52, 320, 0.012, 350),
  t('Chalkboard', 95, 0.14, 0.84, 180, 0.02, 95),
  t('Highlighter', 125, 0.21, 0.86, 260, 0.004, 125),
  t('Terracotta', 35, 0.13, 0.58, 60, 0.016, 85),
  t('Graphite', 260, 0.01, 0.42, 260, 0.004, 95),
  t('Original red', 22, 0.19, 0.6, 30, 0.003, 22),
  t('Sky', 235, 0.13, 0.62, 235, 0.012, 95),
  t('Midnight', 275, 0.13, 0.42, 270, 0.016, 85),
  t('Violet', 295, 0.17, 0.55, 290, 0.012, 95),
  t('Lilac', 300, 0.1, 0.82, 300, 0.012, 320),
  t('Bubblegum', 355, 0.17, 0.64, 350, 0.012, 350),
  t('Peach', 55, 0.1, 0.84, 55, 0.016, 85),
  t('Mocha', 55, 0.07, 0.47, 65, 0.02, 85),
  t('Moss', 115, 0.11, 0.56, 110, 0.014, 95),
  t('Clover', 148, 0.16, 0.6, 150, 0.012, 95),
  t('Mint', 165, 0.11, 0.87, 165, 0.012, 165),
];

export const defaultTheme = themes[0]!;

/** The theme with this name (any case), or Lagoon. */
export function findTheme(name: string | null | undefined): ColourTheme {
  const n = (name ?? '').trim().toLowerCase();
  return themes.find((x) => x.name.toLowerCase() === n) ?? defaultTheme;
}

/** A light accent (Chalkboard, Highlighter, Lilac, Peach, Mint): text on it is dark ink. */
export const isLight = (theme: ColourTheme) => theme.l >= 0.75;

const r = (n: number) => Math.round(n * 1000) / 1000;
const ok = (l: number, c: number, h: number, a = 1) =>
  a === 1 ? `oklch(${r(l)} ${r(c)} ${r(h)})` : `oklch(${r(l)} ${r(c)} ${r(h)} / ${r(a)})`;

export type Tokens = Record<string, string>;

/** The Mac's light tokens (Skin.MacTokens, not dark). */
export function lightTokens(theme: ColourTheme): Tokens {
  const { l: L, c, h } = theme;
  const lt = isLight(theme);
  const o = (l: number, cc: number, a = 1) => ok(l, cc, h, a);
  const n = (l: number, k: number, a = 1) => ok(l, theme.nc * k, theme.nh, a);
  return {
    '--win': n(0.985, 1),
    '--raised': n(0.998, 0.5),
    '--ground': n(0.955, 1.5),
    '--group': n(0.4, 3, 0.05),
    '--glass': 'rgba(255,255,255,0.72)',
    '--fg': '#1d1d1f',
    '--fg2': 'rgba(0,0,0,0.6)',
    '--fg3': 'rgba(0,0,0,0.34)',
    '--sep': 'rgba(0,0,0,0.08)',
    '--fill': 'rgba(0,0,0,0.05)',
    '--fill2': 'rgba(0,0,0,0.08)',
    '--accent': o(L, c),
    '--accent-text': lt ? o(0.45, c * 0.9) : o(Math.min(L - 0.1, 0.48), c),
    '--tint': lt ? o(L, c, 0.92) : o(L, c, 0.88),
    '--accent-tint': o(L, c, 0.16),
    '--on-accent': lt ? '#171717' : '#ffffff',
    '--hl': ok(0.88, 0.16, theme.hl, 0.55),
    '--warn': ok(0.68, 0.15, 65),
    '--ok': ok(0.62, 0.14, 150),
    '--red': '#ff3b30',
    '--press': 'rgba(0,0,0,0.06)',
    '--shadow': '0 1px 2px rgba(0,0,0,0.06), 0 4px 16px rgba(0,0,0,0.06)',
    '--edge': 'inset 0 1px 0 rgba(255,255,255,0.85), inset 0 0 0 0.5px rgba(0,0,0,0.06)',
  };
}

/** The Mac's dark tokens (Skin.MacTokens, dark). */
export function darkTokens(theme: ColourTheme): Tokens {
  const { l: L, c, h } = theme;
  const lt = isLight(theme);
  const o = (l: number, cc: number, a = 1) => ok(l, cc, h, a);
  const n = (l: number, k: number, a = 1) => ok(l, theme.nc * k, theme.nh, a);
  return {
    '--win': n(0.2, 1.5),
    '--raised': n(0.255, 1.5),
    '--ground': n(0.16, 1.5),
    '--group': n(0.9, 2, 0.06),
    '--glass': 'rgba(34,34,38,0.72)',
    '--fg': '#f5f5f7',
    '--fg2': 'rgba(255,255,255,0.62)',
    '--fg3': 'rgba(255,255,255,0.34)',
    '--sep': 'rgba(255,255,255,0.09)',
    '--fill': 'rgba(255,255,255,0.08)',
    '--fill2': 'rgba(255,255,255,0.12)',
    '--accent': lt ? o(L, c) : o(Math.min(L + 0.08, 0.72), c),
    '--accent-text': lt ? o(L, c) : o(0.8, c * 0.8),
    '--tint': lt ? o(L, c, 0.9) : o(Math.min(L + 0.04, 0.68), c, 0.85),
    '--accent-tint': o(0.7, c, 0.22),
    '--on-accent': lt ? '#171717' : '#ffffff',
    '--hl': ok(0.75, 0.15, theme.hl, 0.35),
    '--warn': ok(0.8, 0.14, 75),
    '--ok': ok(0.74, 0.15, 150),
    '--red': '#ff453a',
    '--press': 'rgba(255,255,255,0.08)',
    '--shadow': '0 1px 2px rgba(0,0,0,0.3), 0 4px 16px rgba(0,0,0,0.25)',
    '--edge': 'inset 0 1px 0 rgba(255,255,255,0.08), inset 0 0 0 0.5px rgba(255,255,255,0.08)',
  };
}

/** Each class's dot, from its place in the library (ClassColors.Palette). */
const classPalette: readonly [number, number, number][] = [
  [0.62, 0.14, 250],
  [0.64, 0.14, 155],
  [0.6, 0.14, 295],
  [0.7, 0.13, 70],
  [0.63, 0.14, 20],
  [0.66, 0.12, 200],
  [0.62, 0.14, 330],
  [0.66, 0.13, 120],
];

export function classColor(index: number | null | undefined): string {
  if (index === null || index === undefined || Number.isNaN(index)) return 'var(--fg3)';
  const n = classPalette.length;
  const [l, c, h] = classPalette[((index % n) + n) % n]!;
  return ok(l, c, h);
}

export type Appearance = 'system' | 'light' | 'dark';

const block = (selector: string, tokens: Tokens) =>
  `${selector}{${Object.entries(tokens)
    .map(([k, v]) => `${k}:${v}`)
    .join(';')}}`;

/**
 * The stylesheet that sets a theme: light, dark when the phone is dark (unless the student chose light or dark
 * themselves), each as custom properties on the root.
 */
export function themeCss(theme: ColourTheme, appearance: Appearance): string {
  const light = lightTokens(theme);
  const dark = darkTokens(theme);
  if (appearance === 'light') return block(':root', light) + ':root{color-scheme:light}';
  if (appearance === 'dark') return block(':root', dark) + ':root{color-scheme:dark}';
  return (
    block(':root', light) +
    ':root{color-scheme:light dark}' +
    `@media (prefers-color-scheme: dark){${block(':root', dark)}}`
  );
}

/** The browser chrome's colour for this theme and scheme (the status bar on Android, the tab bar in Safari). */
export function chromeColour(theme: ColourTheme, dark: boolean): string {
  return (dark ? darkTokens(theme) : lightTokens(theme))['--win']!;
}
