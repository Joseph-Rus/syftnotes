// Study Stash · Canvas: where this copy reaches its Study Stash library, and what to tell the student when it can't.
// Shared by the service worker and the toolbar popup.

// {app, key, canvas, files, protocol}, read afresh every time so a new key, library or Canvas address needs no reload:
// config.json in this folder (Study Stash rewrites it when something changes), else a connection saved in Chrome's
// storage, else the config.js of a folder from before 1.4 (STUDY_STASH). Null when there's none of them.
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
    if (got && got.connection && got.connection.app) return got.connection;
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
    case 'no_config': return "This extension isn't connected to Study Stash yet. Open Study Stash and add the extension again.";
    case 'library_refused': return "This extension's key was refused. Open Study Stash and add the extension again.";
    case 'library_unreachable': return "Can't reach your library at " + libraryHost(status.library) + '.';
    case 'signed_out': return 'Sign in to Canvas in Chrome.';
    default: return '';
  }
}
