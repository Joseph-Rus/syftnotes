// Study Stash · Canvas: a read-only fetcher for your Study Stash library.
// The library decides what to read (assignments, submissions, modules, pages, files, or whatever a course's
// scout asks for); this extension only fetches those Canvas URLs with the session you're already
// signed into and hands the answers back. It never writes to Canvas, and it refuses any URL outside Canvas
// and its file store. It waits for work with the library (which answers as soon as there is some), and says on its
// toolbar button when it can't reach the library or Canvas.
importScripts('connection.js');
try { importScripts('config.js'); } catch (e) { /* only a folder from before 1.4 needs it: config.json replaced it */ }

const MAX_BYTES = 40 * 1024 * 1024;
// What this copy of the extension does, told to the library on every visit (docs/canvas.md, "The extension"):
// 2 = says when Chrome is signed out, passes on Canvas's rate limit, never hands over an error page or an
// oversized file as a file, reloads itself before taking work when its folder is newer, posts files one at a time.
// 3 = waits for work (wait=), says which library address it uses (a=), reloads itself when its host permissions change.
const PROTOCOL = 3;
// How long the library may hold a request for work, in seconds: under the 30 s Chrome allows a fetch without an answer.
const WAIT = 20;
// Canvas sent us to its sign-in page, or answered as if nobody were signed in. Canvas's other 401,
// {"status":"unauthorized"}, only means this student can't see that part of the course.
const SIGN_IN = /\/login(\/|\?|$)/;
const UNAUTHENTICATED = /unauthenticated|user authorization required/i;

// Where the library is, the extension's key and the Canvas address (connection.js): read again on every round.
let conn = typeof STUDY_STASH !== 'undefined' ? STUDY_STASH : null;

// Canvas itself, and the hosts Canvas keeps file bodies on (a download redirects there).
function allowed(url) {
  if (!conn || !conn.canvas) return false;
  if (url.startsWith(conn.canvas + '/')) return true;
  if (!Array.isArray(conn.files)) return /^https:\/\/[a-z0-9.-]+\.inscloudgate\.net\//.test(url); // a config.js from before 1.3
  let u;
  try { u = new URL(url); } catch (e) { return false; }
  if (u.protocol !== 'https:' || u.username || u.password || u.port) return false;
  return conn.files.some(h => h.startsWith('*.') ? u.hostname.endsWith(h.slice(1)) : u.hostname === h);
}

// The library's answer, or an error saying whether it refused this extension's key (refused) or didn't answer.
async function app(path, body, signal) {
  const r = await fetch(conn.app + path, {
    method: body ? 'POST' : 'GET',
    headers: {'X-Study-Stash-Key': conn.key, 'Content-Type': 'application/json'},
    body: body ? JSON.stringify(body) : undefined,
    signal,
  });
  if (!r.ok) {
    const e = new Error(`${path}: ${r.status}`);
    e.refused = r.status === 401 || r.status === 403;
    throw e;
  }
  return r.json();
}

function b64(buf) {
  const bytes = new Uint8Array(buf);
  let s = '';
  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
  return btoa(s);
}

// Some files (often your own submissions) don't download through Canvas's /files/<id>/download redirect from a
// service worker. Canvas's API hands out a signed link to the same file, which does.
async function viaPublicUrl(url) {
  const m = url.match(/\/files\/(\d+)\/download/);
  if (!m) throw new Error('not a Canvas file');
  const t = await (await fetch(`${conn.canvas}/api/v1/files/${m[1]}/public_url`, {credentials: 'include'})).text();
  const signed = JSON.parse(t.replace(/^while\(1\);/, '')).public_url;
  if (!signed || !allowed(signed)) throw new Error('no public link');
  return fetch(signed);
}

async function run(job) {
  if (!conn || !conn.canvas) return {id: job.id, error: 'refused: Study Stash has no Canvas address yet'};
  if (!allowed(job.url)) return {id: job.id, error: 'refused: not a Canvas URL'};
  try {
    const headers = job.kind === 'json' ? {Accept: 'application/json'} : {};
    let r;
    try {
      r = await fetch(job.url, {credentials: 'include', headers});
    } catch (e) {
      if (job.kind !== 'bytes') throw e;
      r = await viaPublicUrl(job.url);
    }
    const h = name => r.headers.get(name) || '';
    const out = {id: job.id, status: r.status, link: h('link'), type: h('content-type'), final: r.url};
    // How much of Canvas's allowance is left, and how long it asked us to wait: the library slows down.
    if (h('x-rate-limit-remaining')) out.rate = h('x-rate-limit-remaining');
    if (h('retry-after')) out.retry_after = h('retry-after');
    // Status 401 as well, for a library from before signed_out.
    if (r.url && SIGN_IN.test(new URL(r.url).pathname)) return {...out, status: 401, signed_out: true};
    if (job.kind === 'bytes' && r.ok) {
      // Canvas says how big a file is before sending it: a file too big to hand over is never read.
      if (Number(h('content-length')) > MAX_BYTES) return {...out, error: 'too big'};
      const buf = await r.arrayBuffer();
      if (buf.byteLength > MAX_BYTES) return {...out, error: 'too big'};
      return {...out, b64: b64(buf)};
    }
    const text = await r.text();
    if (job.kind === 'json' && r.ok && text.trimStart().startsWith('<')) return {...out, status: 401, signed_out: true}; // a sign-in page, not data
    if (r.status === 401 && UNAUTHENTICATED.test(text)) out.signed_out = true;
    // A file Canvas refused: its words, so the library can tell why, never saved as the file.
    if (job.kind === 'bytes') return {...out, text: text.slice(0, 4000)};
    return {...out, text: job.kind === 'json' ? text.replace(/^while\(1\);/, '') : text.slice(0, 2000000)};
  } catch (e) {
    return {id: job.id, error: String(e)};
  }
}

// Study Stash rewrites this folder when it updates, and when the Canvas or library address changes: when the
// manifest there isn't the one running (another version, or other hosts it may reach), reload to pick up the new
// files and permissions. Checked before asking for work, so a reload never drops work already taken. Reloading for
// the same folder again (Chrome came back still running something else) waits a minute, so a folder Chrome reads
// differently can't reload it over and over; a folder that changed again reloads at once.
const RELOAD_EVERY = 60 * 1000;
async function needsReload() {
  let onDisk;
  try {
    onDisk = await (await fetch(chrome.runtime.getURL('manifest.json'), {cache: 'no-store'})).json();
  } catch (e) {
    return false;
  }
  const running = chrome.runtime.getManifest();
  const hosts = m => JSON.stringify((m.host_permissions || []).slice().sort());
  if (!onDisk || !onDisk.version || onDisk.version === running.version && hosts(onDisk) === hosts(running)) return false;
  const want = onDisk.version + ' ' + hosts(onDisk);
  try {
    // Kept in local storage: it outlives the reload it guards.
    const last = (await chrome.storage.local.get('lastReload')).lastReload;
    if (last && last.want === want && Date.now() - last.at < RELOAD_EVERY) return false;
    await chrome.storage.local.set({lastReload: {at: Date.now(), want}});
  } catch (e) { /* storage unavailable: reload anyway */ }
  return true;
}

// How this copy is doing, for the popup and the toolbar button: ok, no_config, no_access, library_refused,
// library_unreachable or signed_out. Written only when it changes.
let told = null;
let signedOut = false;
async function setStatus(state) {
  if (state === 'ok' && signedOut) state = 'signed_out';
  const library = conn ? conn.app : '';
  if (told && told.state === state && told.library === library) return;
  told = {state, library, at: new Date().toISOString()};
  try {
    await chrome.storage.local.set({status: told});
    await chrome.action.setBadgeText({text: state === 'ok' ? '' : '!'});
    await chrome.action.setTitle({title: state === 'ok' ? 'Study Stash' : 'Study Stash: ' + statusWords(told)});
  } catch (e) { /* no toolbar button to update */ }
}

// Canvas's answers say whether Chrome is signed in: an answer that bounced to sign-in says no, a good one says yes.
function noteCanvas(results) {
  if (results.some(r => r.signed_out)) signedOut = true;
  else if (results.some(r => !r.error && r.status >= 200 && r.status < 300)) signedOut = false;
}

// Asking the library again at once keeps Chrome from putting this worker to sleep: any extension call resets its timer.
async function stayAwake() {
  try { await chrome.storage.session.set({lastPoll: Date.now()}); } catch (e) { /* no session storage */ }
}

let running = false;
let again = false;      // asked to sync while a request for work was waiting: ask again, with force
let waiting = null;     // the waiting request's AbortController
async function pump(force) {
  if (running) {
    if (force) {
      again = true;
      if (waiting) waiting.abort();
    }
    return;
  }
  running = true;
  try {
    let idle = 0;
    for (let round = 0; round < 5000; round++) {
      if (await needsReload()) { chrome.runtime.reload(); return; }
      conn = await loadConnection();
      if (!conn) { await setStatus('no_config'); return; }
      if (!(await hasAccess(conn))) { await setStatus('no_access'); return; } // a store copy the student hasn't allowed yet
      const forced = force && round === 0 || again;
      again = false;
      let work;
      const asked = Date.now();
      try {
        waiting = typeof AbortController === 'function' ? new AbortController() : null;
        work = await app('/api/v2/canvas/work?v=' + chrome.runtime.getManifest().version + '&p=' + PROTOCOL + '&wait=' + WAIT
                         + '&a=' + encodeURIComponent(conn.app) + (forced ? '&force=1' : ''), undefined, waiting && waiting.signal);
      } catch (e) {
        if (e && e.name === 'AbortError') continue; // asked to sync: ask again at once, with force
        await setStatus(e && e.refused ? 'library_refused' : 'library_unreachable');
        return; // the 30-second alarm tries again
      } finally {
        waiting = null;
      }
      await stayAwake();
      if (work.jobs.length) {
        idle = 0;
        // The folder may have changed while the library held the request (a new Canvas address): work taken now
        // is done by the new copy, which the library hands it to again when it starts.
        if (await needsReload()) { chrome.runtime.reload(); return; }
        conn = await loadConnection() || conn;
        const results = await Promise.all(work.jobs.map(run));
        noteCanvas(results);
        // Answers together, but each file on its own: one big file per request stays under the library's limit.
        const files = results.filter((_, i) => work.jobs[i].kind === 'bytes');
        const rest = results.filter((_, i) => work.jobs[i].kind !== 'bytes');
        try {
          if (rest.length) await app('/api/v2/canvas/results', {results: rest});
          for (const f of files) await app('/api/v2/canvas/results', {results: [f]});
        } catch (e) {
          await setStatus(e && e.refused ? 'library_refused' : 'library_unreachable');
          return; // the library didn't take them: it hands those jobs out again
        }
        await setStatus('ok');
        continue;
      }
      await setStatus('ok');
      // A library that waits for work (protocol 3) is asked again at once, unless it answered straight away with
      // nothing: then a breath first, so a library that can't wait is never asked in a tight loop.
      if (work.p >= 3) {
        if (Date.now() - asked < 1000) await new Promise(r => setTimeout(r, 1500));
        continue;
      }
      // An older library answers at once: check again shortly only while an agent is exploring.
      if (!work.hot || ++idle > 60) return;           // nothing queued and no agent exploring: sleep until the alarm
      await new Promise(r => setTimeout(r, 1500));
    }
  } finally {
    running = false;
  }
}

// Every 30 seconds (Chrome 120 and later): starts the pump again when the worker slept or the library went away.
function schedule() { chrome.alarms.create('sync', {periodInMinutes: 0.5}); }
chrome.runtime.onInstalled.addListener(() => { schedule(); pump(true); });
chrome.runtime.onStartup.addListener(() => { schedule(); pump(false); });
chrome.alarms.onAlarm.addListener(a => { if (a.name === 'sync') pump(false); });
// a store copy was just allowed to reach Canvas and the library: start at once rather than at the next alarm
if (chrome.permissions && chrome.permissions.onAdded) chrome.permissions.onAdded.addListener(() => pump(true));
// the toolbar popup asks for a sync
chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (msg && msg.sync) { pump(true); reply({ok: true}); }
});
