import { classColor, darkTokens, findTheme, isLight, lightTokens, themeCss, themes } from '../src/theme/themes';

test('there are the app’s twenty themes, Lagoon first', () => {
  expect(themes).toHaveLength(20);
  expect(themes[0]!.name).toBe('Lagoon');
  expect(themes.map((t) => t.name)).toContain('Original red');
  expect(themes[19]!.name).toBe('Mint');
});

test('a theme is found by name in any case, and an unknown one is Lagoon', () => {
  expect(findTheme('plum').name).toBe('Plum');
  expect(findTheme(' Blueprint ').name).toBe('Blueprint');
  expect(findTheme('nope').name).toBe('Lagoon');
  expect(findTheme(null).name).toBe('Lagoon');
});

test('Chalkboard, Highlighter, Lilac, Peach and Mint are light accents with dark ink on them', () => {
  expect(themes.filter(isLight).map((t) => t.name)).toEqual(['Chalkboard', 'Highlighter', 'Lilac', 'Peach', 'Mint']);
  expect(isLight(findTheme('Chalkboard'))).toBe(true);
  expect(isLight(findTheme('Lagoon'))).toBe(false);
  expect(lightTokens(findTheme('Highlighter'))['--on-accent']).toBe('#171717');
  expect(lightTokens(findTheme('Lagoon'))['--on-accent']).toBe('#ffffff');
});

test("the accent is the theme's own colour in light, a touch brighter in dark", () => {
  const lagoon = findTheme('Lagoon');
  expect(lightTokens(lagoon)['--accent']).toBe('oklch(0.58 0.12 195)');
  expect(darkTokens(lagoon)['--accent']).toBe('oklch(0.66 0.12 195)');
});

test('following the phone gives light, and dark inside a dark-scheme query', () => {
  const css = themeCss(findTheme('Plum'), 'system');
  expect(css).toMatch(/^:root\{--win:/);
  expect(css).toContain('@media (prefers-color-scheme: dark)');
  expect(themeCss(findTheme('Plum'), 'dark')).not.toContain('@media');
  expect(themeCss(findTheme('Plum'), 'dark')).toContain('color-scheme:dark');
});

test('class dots follow the library order and wrap after eight', () => {
  expect(classColor(0)).toBe('oklch(0.62 0.14 250)');
  expect(classColor(8)).toBe(classColor(0));
  expect(classColor(null)).toBe('var(--fg3)');
});
