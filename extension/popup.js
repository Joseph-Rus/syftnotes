// The toolbar popup: when Canvas last synced, or what's wrong (the library refused this extension's key, can't be
// reached, the browser isn't signed in to Canvas); sync now, or open the library. A copy from a browser's store that
// isn't connected yet asks for the code from Study Stash, then for the browser's permission to reach Canvas and the
// library.
const $ = id => document.getElementById(id);
const s = $('s'), connect = $('connect'), allowRow = $('allow-row'), main = $('main'), code = $('code'), go = $('go');
let conn = null;

function ago(iso) {
  if (!iso) return 'never';
  const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  return m < 1 ? 'just now' : m < 60 ? m + ' min ago' : Math.round(m / 60) + ' h ago';
}

// Show one of the popup's three parts: the code field, the Allow button, or Sync / Open.
function only(part) {
  for (const p of [connect, allowRow, main]) p.hidden = p !== part;
}

// Tell the background script to start now rather than at its next alarm.
function wake() {
  try {
    const p = chrome.runtime.sendMessage({sync: true});
    if (p && p.catch) p.catch(() => { /* the worker is starting: its alarm picks it up */ });
  } catch (e) { /* the worker is starting: its alarm picks it up */ }
}

async function show() {
  const where = await whichBrowser();
  conn = await loadConnection();
  if (!conn) {
    only(connect);
    s.textContent = 'Connect this extension to your library: Study Stash shows the code when you add the extension to ' + (where || 'your browser') + '.';
    return;
  }
  if (!(await hasAccess(conn))) {
    only(allowRow);
    s.textContent = statusWords({state: 'no_access'});
    return;
  }
  only(main);
  $('recode').hidden = !conn.stored; // a store copy takes a new code when the library moves or its key changes
  let r;
  try {
    r = await fetch(conn.app + '/api/v2/canvas/status', {headers: {'X-Study-Stash-Key': conn.key}});
  } catch (e) {
    s.textContent = statusWords({state: 'library_unreachable', library: conn.app});
    return;
  }
  if (r.status === 401 || r.status === 403) {
    s.textContent = statusWords({state: 'library_refused'});
    return;
  }
  let j = {};
  try { j = await r.json(); } catch (e) { /* not the library's answer */ }
  let told = null;
  try { told = (await chrome.storage.local.get('status')).status; } catch (e) { /* storage unavailable */ }
  if (j.busy) s.textContent = j.busy;
  else if (told && told.state === 'signed_out') s.textContent = statusWords(told);
  else s.textContent = j.error ? j.error : 'Canvas synced ' + ago(j.synced) + '.';
}
show();

// Connect and Allow ask the browser straight away, inside the click: it only shows its permission prompt for one.
go.onclick = () => {
  go.disabled = true;
  return connectWithCode(code.value).then(granted => {
    if (granted) wake();
    return show();
  }, e => {
    s.textContent = e.message;
  }).finally(() => { go.disabled = false; });
};
$('allow').onclick = () => allowAccess(conn).then(granted => {
  if (granted) wake();
  return show();
}, () => show());
$('recode').onclick = () => {
  only(connect);
  s.textContent = 'Paste the new code from Study Stash.';
  code.focus();
};
$('sync').onclick = () => {
  wake();
  s.textContent = 'Syncing… you can close this.';
};
$('open').onclick = () => {
  if (conn) chrome.tabs.create({url: conn.app + '/'});
  window.close();
};
