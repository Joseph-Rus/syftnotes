// The toolbar popup: when Canvas last synced, or what's wrong (the library refused this extension's key, can't be
// reached, the browser isn't signed in to Canvas); sync now, or open the library. A copy from a browser's store that
// isn't connected yet asks for the code from Syft Notes, then for the browser's permission to reach Canvas and the
// library.
const $ = id => document.getElementById(id);
const s = $('s'), connect = $('connect'), allowRow = $('allow-row'), main = $('main'), code = $('code'), go = $('go'), useRow = $('use-row'), use = $('use');
let conn = null;

// The status line, with its colour: 'ok' (synced), 'busy' (working on it), 'ask' (waiting on the student) or
// 'problem'.
function say(text, tone) {
  s.textContent = text;
  s.dataset.tone = tone;
}

function ago(iso) {
  if (!iso) return 'never';
  const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  return m < 1 ? 'just now' : m < 60 ? m + ' min ago' : Math.round(m / 60) + ' h ago';
}

// Show one of the popup's four parts: the code field, the Allow button, Use this Canvas, or Sync / Open.
function only(part) {
  for (const p of [connect, allowRow, main, useRow]) p.hidden = p !== part;
}

// Tell the background script to start now rather than at its next alarm.
function wake() {
  try {
    const p = chrome.runtime.sendMessage({sync: true});
    if (p && p.catch) p.catch(() => { /* the worker is starting: its alarm picks it up */ });
  } catch (e) { /* the worker is starting: its alarm picks it up */ }
}

// The tab the student clicked this button on, when it's a Canvas they're signed in to (the button's click lets this
// copy look at that one tab): its address, else ''.
async function canvasHere() {
  try {
    const tabs = await chrome.tabs.query({active: true, currentWindow: true});
    const m = /^https:\/\/[^\/?#]+/i.exec(tabs && tabs[0] && tabs[0].url || '');
    return m && await isCanvas(m[0]) ? m[0] : '';
  } catch (e) {
    return '';
  }
}

// Signed out of Canvas, the way on is Canvas itself: Open Canvas leads, and Sync steps back.
function signedOut(yes) {
  $('canvas').hidden = !yes;
  $('sync').className = yes ? '' : 'p';
}

async function show() {
  const where = await whichBrowser();
  // Just added from a store (1.8): Syft Notes on this computer may be waiting for it, with nothing to paste.
  conn = await loadConnection() || await adoptLibrary();
  if (!conn) {
    only(connect);
    say('Connect this extension to Syft Notes: Syft Notes shows the code when you add the extension to ' + (where || 'your browser') + '.', 'ask');
    return;
  }
  if (!(await hasAccess(conn))) {
    only(allowRow);
    say((where || 'This browser') + ' needs your OK before this extension can read Canvas and reach Syft Notes.', 'ask');
    return;
  }
  if (!conn.canvas) {
    // No Canvas found yet (1.8): a school with Canvas at its own address is found here, on its page.
    const here = await canvasHere();
    only(here ? useRow : null);
    say(here ? 'Read Canvas at ' + libraryHost(here) + '?'
      : 'Sign in to Canvas in ' + (where || 'this browser') + '. Syft Notes finds it by itself; if it doesn\'t, open Canvas and click this button there.', 'ask');
    use.onclick = () => chrome.permissions.request({origins: [originPattern(here)]}).then(granted => {
      if (!granted) return show();
      return tellCanvas(conn, here).then(() => { wake(); return show(); }, () => say(statusWords({state: 'library_unreachable', library: conn.app}), 'problem'));
    });
    return;
  }
  only(main);
  signedOut(false);
  $('recode').hidden = !conn.stored; // a store copy takes a new code when the library moves or its key changes
  let r;
  try {
    r = await fetch(conn.app + '/api/v2/canvas/status', {headers: {'X-Study-Stash-Key': conn.key}});
  } catch (e) {
    say(statusWords({state: 'library_unreachable', library: conn.app}), 'problem');
    return;
  }
  if (r.status === 401 || r.status === 403) {
    say(statusWords({state: 'library_refused'}), 'problem');
    return;
  }
  let j = {};
  try { j = await r.json(); } catch (e) { /* not the library's answer */ }
  let told = null;
  try { told = (await chrome.storage.local.get('status')).status; } catch (e) { /* storage unavailable */ }
  if (j.busy) say(j.busy, 'busy');
  else if (told && told.state === 'signed_out') { say(statusWords(told), 'problem'); signedOut(true); }
  else if (j.error) say(j.error, 'problem');
  else say('Canvas synced ' + ago(j.synced) + '.', 'ok');
}
show();

// Connect and Allow ask the browser straight away, inside the click: it only shows its permission prompt for one.
go.onclick = () => {
  go.disabled = true;
  return connectWithCode(code.value).then(granted => {
    if (granted) wake();
    return show();
  }, e => {
    say(e.message, 'problem');
  }).finally(() => { go.disabled = false; });
};
$('allow').onclick = () => allowAccess(conn).then(granted => {
  if (granted) wake();
  return show();
}, () => show());
$('recode').onclick = () => {
  only(connect);
  say('Paste the new code from Syft Notes.', 'ask');
  code.focus();
};
$('sync').onclick = () => {
  wake();
  say('Syncing… you can close this.', 'busy');
};
$('canvas').onclick = () => {
  if (conn && conn.canvas) chrome.tabs.create({url: conn.canvas + '/'});
  window.close();
};
$('open').onclick = () => {
  if (conn) chrome.tabs.create({url: conn.app + '/'});
  window.close();
};
