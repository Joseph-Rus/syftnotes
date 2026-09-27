// The toolbar popup: when Canvas last synced, or what's wrong (the library refused this extension's key, can't be
// reached, Chrome isn't signed in to Canvas); sync now, or open the library.
const s = document.getElementById('s');
let conn = null;

function ago(iso) {
  if (!iso) return 'never';
  const m = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  return m < 1 ? 'just now' : m < 60 ? m + ' min ago' : Math.round(m / 60) + ' h ago';
}

async function show() {
  conn = await loadConnection();
  if (!conn) {
    s.textContent = statusWords({state: 'no_config'});
    return;
  }
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

document.getElementById('sync').onclick = () => {
  chrome.runtime.sendMessage({sync: true});
  s.textContent = 'Syncing… you can close this.';
};
document.getElementById('open').onclick = () => {
  if (conn) chrome.tabs.create({url: conn.app + '/'});
  window.close();
};
