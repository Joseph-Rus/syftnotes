// Study Stash · Canvas: where this copy reaches its Study Stash library, and what to tell the student when it can't.
// Shared by the service worker and the toolbar popup.

// {app, key, canvas, files, protocol}, read afresh every time so a new key, library or Canvas address needs no reload:
// config.json in this folder (Study Stash rewrites it when something changes), else the connection a copy from the
// Chrome Web Store saved from a pasted code (marked stored: it may reach only what Chrome was asked to allow), else the
// config.js of a folder from before 1.4 (STUDY_STASH). Null when there's none of them.
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

// What's wrong, in the student's words: the toolbar button's title and the popup say it. '' when all is well.
function statusWords(status) {
  switch (status && status.state) {
    case 'no_config': return "This extension isn't connected to Study Stash yet. Click it and paste the code from Study Stash.";
    case 'no_access': return 'Chrome hasn\'t allowed this extension to reach Canvas and your library. Click it and choose Allow.';
    case 'library_refused': return "This extension's key was refused. Open Study Stash and add the extension again.";
    case 'library_unreachable': return "Can't reach your library at " + libraryHost(status.library) + '.';
    case 'signed_out': return 'Sign in to Canvas in Chrome.';
    default: return '';
  }
}

// A copy from the Chrome Web Store has no folder for Study Stash to write: the student pastes a code from Study Stash
// instead. The code is config.json's text in base64url (Extension.ConnectionCode). Throws, in the student's words, when
// the text isn't one.
function decodeCode(code) {
  const wrong = new Error("That isn't a code from Study Stash. Copy it again and paste the whole thing.");
  let b64 = String(code || '').replace(/\s+/g, '').replace(/^["']|["']$/g, '').replace(/-/g, '+').replace(/_/g, '/').replace(/=+$/, '');
  if (!b64 || /[^A-Za-z0-9+\/]/.test(b64) || b64.length % 4 === 1) throw wrong;
  while (b64.length % 4) b64 += '=';
  let c;
  try { c = JSON.parse(atob(b64)); } catch (e) { throw wrong; }
  const web = /^https?:\/\/[^\/?#\s]+/i;
  if (!c || typeof c !== 'object' || !web.test(c.app || '') || !c.key) throw wrong;
  if (!web.test(c.canvas || '')) throw new Error("Add your school's Canvas address in Study Stash first, then copy the code again.");
  return {
    app: c.app.replace(/\/+$/, ''), key: String(c.key), canvas: c.canvas.replace(/\/+$/, ''),
    files: Array.isArray(c.files) ? c.files.filter(h => typeof h === 'string') : [], protocol: c.protocol || 0,
  };
}

// "https://school.instructure.com/*" from "https://school.instructure.com/courses": the match pattern Chrome grants,
// without a default port, the way Study Stash writes a folder's host permissions.
function originPattern(url) {
  const m = /^(https?):\/\/([^\/?#:]+)(?::(\d+))?/i.exec(url || '');
  if (!m) return null;
  const scheme = m[1].toLowerCase(), port = m[3] && m[3] !== (scheme === 'https' ? '443' : '80') ? ':' + m[3] : '';
  return scheme + '://' + m[2].toLowerCase() + port + '/*';
}

// What a connection needs to reach: its Canvas, Canvas's file store and the library.
function originsFor(c) {
  const out = [originPattern(c.canvas)].concat((c.files || []).map(h => 'https://' + h + '/*'), [originPattern(c.app)]);
  return out.filter((o, i) => o && out.indexOf(o) === i);
}

// Whether Chrome lets this copy reach everything its connection needs. A folder's copy has them in its manifest; a
// store copy has them once the student allowed them.
async function hasAccess(c) {
  if (!c || !c.stored) return true;
  try { return await chrome.permissions.contains({origins: originsFor(c)}); } catch (e) { return false; }
}

// The popup's Connect: ask Chrome for the code's sites while the click still counts (a permission request needs one),
// and save the connection at once, so it's kept even if the popup closes while Chrome asks. Resolves to whether Chrome
// allowed them; rejects, in the student's words, for a code that isn't one.
function connectWithCode(code) {
  let c;
  try { c = decodeCode(code); } catch (e) { return Promise.reject(e); }
  const asking = chrome.permissions.request({origins: originsFor(c)});
  return chrome.storage.local.set({connection: c}).then(() => asking).then(granted => !!granted);
}

// Ask again for a saved connection's sites (the student said no the first time, or Chrome forgot).
function allowAccess(c) {
  return chrome.permissions.request({origins: originsFor(c)}).then(granted => !!granted);
}
