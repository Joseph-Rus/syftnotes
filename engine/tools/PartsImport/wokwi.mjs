// Reads the SVG each Wokwi element draws, as it is with every light off and nothing pressed, without a browser:
// the package's own modules run against a stand-in for Lit whose templates are plain strings.
//   node wokwi.mjs <package/dist/esm> <work dir> <out.json> tag...
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [esm, work, out, ...tags] = process.argv.slice(2);
fs.rmSync(work, { recursive: true, force: true });
fs.cpSync(esm, work, { recursive: true });

const shim = path.join(work, '_lit-shim.mjs');
fs.writeFileSync(shim, `
const flat = v => v === undefined || v === null || v === false || v === true || typeof v === 'function' ? ''
  : Array.isArray(v) ? v.map(flat).join('') : String(v);
const tag = (strings, ...values) => strings.reduce((s, part, i) => s + part + (i < values.length ? flat(values[i]) : ''), '');
export const html = tag, svg = tag, css = tag, unsafeCSS = v => String(v), nothing = '';
export class LitElement {
  constructor() {}
  addEventListener() {} removeEventListener() {} dispatchEvent() {} requestUpdate() {}
  get shadowRoot() { return null; }
}
export const customElement = () => c => c, property = () => () => {}, query = () => () => {}, state = () => () => {};
export const styleMap = o => Object.entries(o || {}).map(([k, v]) => k.replace(/[A-Z]/g, m => '-' + m.toLowerCase()) + ':' + v).join(';');
export const classMap = o => Object.entries(o || {}).filter(([, v]) => v).map(([k]) => k).join(' ');
globalThis.customElements = { define() {}, get() {} };
globalThis.HTMLElement = class {};
`);
const shimUrl = pathToFileURL(shim).href;

// Plain Node can't import 'lit' or './pin' (no extension): every import is pointed at a real file.
for (const file of fs.readdirSync(work, { recursive: true })) {
  const full = path.join(work, String(file));
  if (!full.endsWith('.js')) continue;
  let text = fs.readFileSync(full, 'utf8');
  text = text.replace(/from '(lit[^']*)'/g, `from '${shimUrl}'`);
  text = text.replace(/from '(\.{1,2}\/[^']+)'/g, (m, spec) => {
    const target = path.resolve(path.dirname(full), spec);
    return fs.existsSync(target + '.js') ? `from '${spec}.js'` : m;
  });
  fs.writeFileSync(full, text);
}

const flatSheet = s => Array.isArray(s) ? s.map(flatSheet).join('\n') : s ? String(s) : '';
const result = {};
for (const t of tags) {
  try {
    const mod = await import(pathToFileURL(path.join(work, `${t}-element.js`)).href);
    const Cls = Object.values(mod).find(v => typeof v === 'function' && v.prototype && 'render' in v.prototype);
    const el = new Cls();
    let drawn = String(el.render());
    // The element's own style sheet (its text sizes and fonts) goes in with its drawing.
    const sheet = flatSheet(Cls.styles);
    if (sheet) drawn = drawn.replace(/<svg\b[^>]*>/, m => `${m}<style>${sheet}</style>`);
    result[t] = drawn;
  } catch (e) {
    result[t] = { error: String(e && e.message || e) };
  }
}
fs.writeFileSync(out, JSON.stringify(result, null, 1));
console.log(Object.entries(result).map(([k, v]) => `${k}: ${typeof v === 'string' ? v.length + ' chars' : v.error}`).join('\n'));
