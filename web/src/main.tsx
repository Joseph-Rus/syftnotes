import { render } from 'preact';
import 'katex/dist/katex.min.css';
import './styles/base.css';
import './styles/kit.css';
import './styles/screens.css';
import { App } from './App';

render(<App />, document.getElementById('app')!);

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/app/sw.js').catch(() => {
      // offline still works for this visit; it'll register once the network's back
    });
  });
}
