import { useEffect } from 'preact/hooks';
import { chromeColour, defaultTheme, themeCss } from './themes';

let style: HTMLStyleElement | null = null;
let meta: HTMLMetaElement | null = null;

/**
 * Puts the running theme's colours on the page: a `<style>` with its custom properties (light, and dark when the
 * phone is dark), and the browser chrome's colour (the status bar, Safari's tab bar) to match. There's one theme for
 * now (Lagoon); when the library can tell the phone which of its ten it's on, this is where that arrives.
 */
export function useTheme(theme = defaultTheme) {
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
    style.textContent = themeCss(theme, 'system');
    const media = window.matchMedia?.('(prefers-color-scheme: dark)');
    const paint = () => meta!.setAttribute('content', chromeColour(theme, media?.matches ?? false));
    paint();
    media?.addEventListener('change', paint);
    return () => media?.removeEventListener('change', paint);
  }, [theme]);
}
