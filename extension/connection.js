// Syft Notes · Canvas: where this copy reaches its Syft Notes library, and what to tell the student when it can't.
// Shared by the background script and the toolbar popup. The same files run in Chrome and the browsers built on it
// (Edge, Brave, Arc, Opera, Vivaldi) and in Firefox and the browsers built on it (Zen, LibreWolf, Waterfox); only the
// manifest differs (Extension.cs writes each one).

// Firefox and its family: an extension's pages live at moz-extension://.
const GECKO = (() => {
  try { return chrome.runtime.getURL('').indexOf('moz-extension:') === 0; } catch (e) { return false; }
})();

// Which browser this copy runs in, as its maker names it ("Chrome", "Edge", "Brave", "Firefox", "Zen"): told to the
// library, so Syft Notes can say "Is Firefox open?", and used in this extension's own words. '' until whichBrowser()
// has been asked, and for a browser that doesn't say.
let browserIs = '';
async function whichBrowser() {
  if (browserIs) return browserIs;
  try {
    // Firefox and the browsers built on it say their own name (Zen says "firefox").
    if (typeof browser !== 'undefined' && browser.runtime && browser.runtime.getBrowserInfo) {
      const name = String((await browser.runtime.getBrowserInfo()).name || 'Firefox');
      browserIs = name.charAt(0).toUpperCase() + name.slice(1);
      return browserIs;
    }
  } catch (e) { /* asked below */ }
  try {
    const nav = typeof navigator !== 'undefined' ? navigator : null;
    const brands = nav && nav.userAgentData && nav.userAgentData.brands ? nav.userAgentData.brands.map(b => b.brand) : [];
    // The browser's own brand comes before the engine's ("Microsoft Edge" beside "Chromium"). Arc says it's Chrome.
    const named = [['Microsoft Edge', 'Edge'], ['Brave', 'Brave'], ['Opera GX', 'Opera GX'], ['Opera', 'Opera'], ['Vivaldi', 'Vivaldi'],
                   ['Google Chrome', 'Chrome'], ['Chromium', 'Chromium']].find(n => brands.indexOf(n[0]) >= 0);
    const ua = nav && nav.userAgent || '';
    browserIs = named ? named[1] : GECKO || /Firefox\//.test(ua) ? 'Firefox' : /Safari\//.test(ua) && !/Chrom/.test(ua) ? 'Safari' : '';
  } catch (e) { /* a browser that doesn't say */ }
  return browserIs;
}

// {app, key, canvas, files, protocol}, read afresh every time so a new key, library or Canvas address needs no reload:
// config.json in this folder (Syft Notes rewrites it when something changes), else the connection a copy from a
// browser's store saved from a pasted code (marked stored: it may reach only what the browser was asked to allow), else
// the config.js of a folder from before 1.4 (STUDY_STASH). Null when there's none of them.
async function loadConnection() {
  try {
    const r = await fetch(chrome.runtime.getURL('config.json'), {cache: 'no-store'});
    if (r.ok) {
      const c = await r.json();
      if (c && c.app) return c;
    }
  } catch (e) { /* no config.json in this folder */ }
  try {
    const got = await chrome.storage.local.get('connection');
    if (got && got.connection && got.connection.app) return Object.assign({}, got.connection, {stored: true});
  } catch (e) { /* storage unavailable */ }
  if (typeof STUDY_STASH !== 'undefined' && STUDY_STASH && STUDY_STASH.app) return STUDY_STASH;
  return null;
}

// "mini.local:8787" from "http://mini.local:8787": how the student knows their library.
function libraryHost(app) {
  const m = /^[a-z]+:\/\/([^\/?#]+)/i.exec(app || '');
  return m ? m[1] : (app || 'your library');
}

// What's wrong, in the student's words: the toolbar button's title and the popup say it. '' when all is well. The
// browser is named once whichBrowser() has found it.
function statusWords(status) {
  switch (status && status.state) {
    case 'no_config': return "This extension isn't connected to Syft Notes yet. Click it and paste the code from Syft Notes.";
    case 'no_access': return (browserIs || 'This browser') + ' hasn\'t allowed this extension to reach Canvas and your library. Click it and choose Allow.';
    case 'library_refused': return "This extension's key was refused. Open Syft Notes and add the extension again.";
    case 'library_unreachable': return "Can't reach your library at " + libraryHost(status.library) + '.';
    case 'signed_out': return 'Sign in to Canvas in ' + (browserIs || 'this browser') + '.';
    default: return '';
  }
}

// A copy from a browser's store (the Chrome Web Store, Firefox's add-ons) has no folder for Syft Notes to write: the
// student pastes a code from Syft Notes instead. The code is config.json's text in base64url (Extension.ConnectionCode). Throws, in the student's words, when
// the text isn't one.
function decodeCode(code) {
  const wrong = new Error("That isn't a code from Syft Notes. Copy it again and paste the whole thing.");
  let b64 = String(code || '').replace(/\s+/g, '').replace(/^["']|["']$/g, '').replace(/-/g, '+').replace(/_/g, '/').replace(/=+$/, '');
  if (!b64 || /[^A-Za-z0-9+\/]/.test(b64) || b64.length % 4 === 1) throw wrong;
  while (b64.length % 4) b64 += '=';
  let c;
  try { c = JSON.parse(atob(b64)); } catch (e) { throw wrong; }
  const web = /^https?:\/\/[^\/?#\s]+/i;
  if (!c || typeof c !== 'object' || !web.test(c.app || '') || !c.key) throw wrong;
  if (!web.test(c.canvas || '')) throw new Error("Add your school's Canvas address in Syft Notes first, then copy the code again.");
  return {
    app: c.app.replace(/\/+$/, ''), key: String(c.key), canvas: c.canvas.replace(/\/+$/, ''),
    files: Array.isArray(c.files) ? c.files.filter(h => typeof h === 'string') : [], protocol: c.protocol || 0,
  };
}

// "https://school.instructure.com/*" from "https://school.instructure.com/courses": the match pattern the browser
// grants, without a default port, the way Syft Notes writes a folder's host permissions. Firefox's patterns take no
// port at all: it grants "http://mini.local/*" (that host on every port), and one written with a port never matches
// what it granted.
function originPattern(url) {
  const m = /^(https?):\/\/([^\/?#:]+)(?::(\d+))?/i.exec(url || '');
  if (!m) return null;
  const scheme = m[1].toLowerCase(), port = !GECKO && m[3] && m[3] !== (scheme === 'https' ? '443' : '80') ? ':' + m[3] : '';
  return scheme + '://' + m[2].toLowerCase() + port + '/*';
}

// What a connection needs to reach: its Canvas, Canvas's file store and the library.
function originsFor(c) {
  const out = [originPattern(c.canvas)].concat((c.files || []).map(h => 'https://' + h + '/*'), [originPattern(c.app)]);
  return out.filter((o, i) => o && out.indexOf(o) === i);
}

// Whether the browser lets this copy reach everything its connection needs. A folder's copy has them in its manifest;
// a store copy has them once the student allowed them.
async function hasAccess(c) {
  if (!c || !c.stored) return true;
  try { return await chrome.permissions.contains({origins: originsWanted(c)}); } catch (e) { return false; }
}

// The sites a store copy asks for: its Canvas, the file store and the library; before its Canvas is known (1.8), every
// Canvas Cloud school's in its place, so the student's can be found.
function originsWanted(c) {
  return c.canvas ? originsFor(c) : originsToFind(c);
}

// The popup's Connect: ask the browser for the code's sites while the click still counts (a permission request needs
// one), and save the connection at once, so it's kept even if the popup closes while the browser asks (Firefox closes
// it). Resolves to whether the browser allowed them; rejects, in the student's words, for a code that isn't one.
function connectWithCode(code) {
  let c;
  try { c = decodeCode(code); } catch (e) { return Promise.reject(e); }
  const asking = chrome.permissions.request({origins: originsFor(c)});
  return chrome.storage.local.set({connection: c}).then(() => asking).then(granted => !!granted);
}

// Ask again for a saved connection's sites (the student said no the first time, or the browser forgot).
function allowAccess(c) {
  return chrome.permissions.request({origins: originsWanted(c)}).then(granted => !!granted);
}

// ---- Connecting with nothing to type (1.8) --------------------------------------------------------------------------
// Just added from a store, this copy knows nothing. Syft Notes on this computer opens a short window when the student
// presses Add Syft Notes to <browser> there: the library answers this copy (and no web page) with where it is and its
// key. Then the copy finds the student's Canvas by itself: Canvas Cloud schools are all at <school>.instructure.com,
// and the browser has a Canvas session cookie for the one the student signs in to. A school with Canvas at its own
// address is found from the toolbar button instead, on a Canvas page (popup.js). Nothing is typed or pasted.

// Where a library on this computer listens: 8787, or the next free pair after it.
const LIBRARY_PORTS = [8787, 8789, 8791, 8793, 8795, 8797];

// Canvas's own session cookies (canvas-lms names it _normandy_session; Instructure's hosting, canvas_session).
const CANVAS_COOKIES = ['canvas_session', '_normandy_session', '_legacy_normandy_session'];

// What a copy needs before it knows the student's Canvas: its library, every Canvas Cloud school and the file store.
const ANY_CANVAS = 'https://*.instructure.com/*';
function originsToFind(c) {
  const out = [originPattern(c.app), ANY_CANVAS].concat((c.files || []).map(h => 'https://' + h + '/*'));
  return out.filter((o, i) => o && out.indexOf(o) === i);
}

// The library on this computer, as it answers this copy while its window is open: {app, key, canvas, files, protocol}
// ('' canvas until it's found), or null when none answered.
async function findLibrary(ports) {
  for (const port of ports || LIBRARY_PORTS) {
    try {
      const r = await fetch('http://127.0.0.1:' + port + '/api/v2/canvas/hello', {cache: 'no-store'});
      if (!r.ok) continue;
      const c = await r.json();
      if (!c || !/^https?:\/\//i.test(c.app || '') || !c.key) continue;
      return {
        app: c.app.replace(/\/+$/, ''), key: String(c.key), canvas: String(c.canvas || '').replace(/\/+$/, ''),
        files: Array.isArray(c.files) ? c.files.filter(h => typeof h === 'string') : [], protocol: c.protocol || 0,
      };
    } catch (e) { /* nothing on that port */ }
  }
  return null;
}

// The library on this computer, kept as this copy's connection (as a pasted code's would be): the connection, or null
// when no library answered (Syft Notes isn't waiting for this copy just now).
async function adoptLibrary(ports) {
  const c = await findLibrary(ports);
  if (!c) return null;
  await chrome.storage.local.set({connection: c});
  return Object.assign({}, c, {stored: true});
}

// Whether a site is a Canvas the browser is signed in to: Canvas answers who's signed in with their id.
async function isCanvas(origin) {
  try {
    const r = await fetch(origin.replace(/\/+$/, '') + '/api/v1/users/self', {credentials: 'include', headers: {Accept: 'application/json'}});
    if (!r.ok) return false;
    const me = JSON.parse((await r.text()).replace(/^while\(1\);/, ''));
    return !!(me && me.id);
  } catch (e) {
    return false;
  }
}

// The student's Canvas, from the browser's Canvas session cookies on instructure.com (newest first), checked by asking
// Canvas who's signed in: "https://school.instructure.com", or '' when there's none (signed out, or Canvas at the
// school's own address).
async function findCanvas() {
  if (!chrome.cookies || !chrome.cookies.getAll) return '';
  let cookies = [];
  try { cookies = await chrome.cookies.getAll({domain: 'instructure.com'}); } catch (e) { return ''; }
  const hosts = [];
  for (const k of cookies.filter(k => CANVAS_COOKIES.indexOf(k.name) >= 0).sort((a, b) => (b.expirationDate || 0) - (a.expirationDate || 0))) {
    const host = String(k.domain || '').replace(/^\./, '').toLowerCase();
    if (/^[a-z0-9-]+(\.[a-z0-9-]+)*\.instructure\.com$/.test(host) && hosts.indexOf(host) < 0) hosts.push(host);
  }
  for (const host of hosts) if (await isCanvas('https://' + host)) return 'https://' + host;
  return '';
}

// Tell the library where the student's Canvas is, and keep it in this copy's connection. Resolves to the Canvas the
// library goes by (the one it had already, when Settings set another).
async function tellCanvas(c, url) {
  const r = await fetch(c.app + '/api/v2/canvas/found', {
    method: 'POST', headers: {'X-Study-Stash-Key': c.key, 'Content-Type': 'application/json'}, body: JSON.stringify({url}),
  });
  if (!r.ok) throw new Error('/api/v2/canvas/found: ' + r.status);
  const canvas = String((await r.json()).canvas || url).replace(/\/+$/, '');
  if (c.stored) {
    const keep = Object.assign({}, c, {canvas});
    delete keep.stored;
    await chrome.storage.local.set({connection: keep});
  }
  return canvas;
}
