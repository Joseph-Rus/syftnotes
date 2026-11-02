// The page this add-on opens when it's just been added (1.8): it finds Syft Notes on this computer, asks the browser
// for the student's Canvas and their library (one Allow, inside a click as the browser requires), then finds their
// Canvas from their own sign-in and tells Syft Notes, which carries on by itself. Nothing is typed or pasted.
const $ = id => document.getElementById(id);
let conn = null;
let where = '';
let looking = false;

function say(title, text) {
  $('t').textContent = title;
  $('s').textContent = text;
}

function show(...ids) {
  for (const id of ['allow', 'again', 'other']) $(id).hidden = ids.indexOf(id) < 0;
}

// The background script starts now rather than at its next alarm.
function wake() {
  try {
    const p = chrome.runtime.sendMessage({sync: true});
    if (p && p.catch) p.catch(() => { /* the worker is starting: its alarm picks it up */ });
  } catch (e) { /* the worker is starting */ }
}

function hostOf(url) {
  const m = /^[a-z]+:\/\/([^\/?#]+)/i.exec(url || '');
  return m ? m[1] : url;
}

async function start() {
  where = await whichBrowser() || 'your browser';
  conn = await loadConnection() || await adoptLibrary();
  if (!conn) {
    say('Open Syft Notes', 'Open Syft Notes on this computer and press Add Syft Notes to ' + where + ' there, then press Allow when Syft Notes asks. This page carries on by itself.');
    show();
    setTimeout(start, 3000);
    return;
  }
  if (!(await hasAccess(conn))) {
    say('Allow Syft Notes to read Canvas', where + ' asks you next. Syft Notes reads Canvas with your own sign-in and keeps it in your library on this computer. It never changes anything on Canvas.');
    show('allow');
    return;
  }
  if (conn.canvas) return done();
  look();
}

// The student's Canvas, from their own sign-in: found at once when they're signed in already, else as soon as they are.
async function look() {
  if (looking) return;
  looking = true;
  try {
    const found = await findCanvas();
    if (found) {
      conn.canvas = await tellCanvas(conn, found);
      wake();
      return done();
    }
  } catch (e) {
    say('Syft Notes didn\'t answer', 'Is Syft Notes still open on this computer? This page tries again by itself.');
  } finally {
    looking = false;
  }
  if (!conn.canvas) {
    say('Sign in to Canvas', 'Open Canvas in ' + where + ' and sign in, the way you usually do. Syft Notes finds it by itself.');
    show('other');
    setTimeout(look, 2000);
  }
}

function done() {
  say('All set', 'Syft Notes reads your Canvas at ' + hostOf(conn.canvas) + '. Go back to Syft Notes: your classes are on their way. You can close this tab.');
  show();
}

// Allow asks the browser straight away, inside the click: it shows its question only for one.
$('allow').onclick = () => chrome.permissions.request({origins: originsWanted(conn)}).then(granted => {
  if (!granted) {
    say('Syft Notes can\'t read Canvas yet', 'Click Allow, then Allow again when ' + where + ' asks. Syft Notes only reads; nothing on Canvas changes.');
    show('allow');
    return;
  }
  wake();
  return start();
});
$('again').onclick = () => start();
if (chrome.cookies && chrome.cookies.onChanged) chrome.cookies.onChanged.addListener(change => {
  if (conn && !conn.canvas && !change.removed && change.cookie && CANVAS_COOKIES.indexOf(change.cookie.name) >= 0) look();
});
start();
