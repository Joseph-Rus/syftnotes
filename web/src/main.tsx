import { render } from 'preact';
import 'katex/dist/katex.min.css';
import './styles/base.css';
import './styles/kit.css';
import './styles/screens.css';
import './styles/app.css';
import './styles/syft.css';
import { App } from './App';

const root = document.getElementById('app')!;
// `npm run dev`, then /app/syft-demo: the pixel s's three splash screens and four loaders, side by side (never built in).
if (import.meta.env.DEV && location.pathname.replace(/\/+$/, '') === '/app/syft-demo')
  void import('./ui/SyftDemo').then(({ SyftDemo }) => render(<SyftDemo />, root));
else render(<App />, root);

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/app/sw.js').catch(() => {
      // offline still works for this visit; it'll register once the network's back
    });
  });
}
