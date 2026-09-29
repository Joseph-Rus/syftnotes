import { useEffect } from 'preact/hooks';
import { persistedStore, useStore } from '../data/store';
import { chromeColour, findTheme, themeCss, type Appearance } from './themes';

/** The look chosen in this phone's Settings: one of the Mac's ten colour themes, and light, dark or the phone's own. */
export const look = persistedStore<{ theme: string; appearance: Appearance }>('ss.look', { theme: 'Lagoon', appearance: 'system' });

let style: HTMLStyleElement | null = null;
let meta: HTMLMetaElement | null = null;

/**
 * Puts the chosen theme's colours on the page: a `<style>` with its custom properties (light, and dark when the
 * phone is dark or dark was chosen), and the browser chrome's colour (the status bar, Safari's tab bar) to match.
 */
export function useTheme() {
  const chosen = useStore(look);
  const theme = findTheme(chosen.theme);
  const appearance = chosen.appearance;
  useEffect(() => {
    if (typeof document === 'undefined') return;
    if (!style) {
      style = document.createElement('style');
      document.head.appendChild(style);
    }
    if (!meta) {
      meta = document.querySelector('meta[name="theme-color"]');
      if (!meta) {
        meta = document.createElement('meta');
        meta.name = 'theme-color';
        document.head.appendChild(meta);
      }
    }
    style.textContent = themeCss(theme, appearance);
    const media = window.matchMedia?.('(prefers-color-scheme: dark)');
    const paint = () =>
      meta!.setAttribute('content', chromeColour(theme, appearance === 'dark' || (appearance === 'system' && (media?.matches ?? false))));
    paint();
    media?.addEventListener('change', paint);
    return () => media?.removeEventListener('change', paint);
  }, [theme, appearance]);
}
