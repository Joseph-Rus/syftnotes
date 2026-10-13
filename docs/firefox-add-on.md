# Study Stash for Canvas in Firefox

The Canvas extension is one set of files (`extension/`) that runs in two families of browser:

- **Chrome and the browsers built on it** (Edge, Brave, Arc, Opera, Vivaldi, Chromium). They load the folder Study
  Stash writes ("Load unpacked", see [canvas.md](canvas.md#the-extension)), or a copy from the Chrome Web Store
  ([chrome-web-store.md](chrome-web-store.md)).
- **Firefox and the browsers built on it** (Zen, LibreWolf, Waterfox). This page.

Version 1.6 is published on addons.mozilla.org (submitted 2026-10-06, public by the listing's own date on
2026-10-09): [Study Stash for Canvas](https://addons.mozilla.org/firefox/addon/study-stash-for-canvas/).
`Extension.FirefoxPublishedAt` names that page, so from Study Stash 0.16.3 the Canvas step offers Firefox and the
browsers built on it. This page is what's built, what a student sees, how it was tried, and what publishing took.

## Why Firefox can't use the folder

Firefox only keeps an add-on that Mozilla has signed. It can load a folder ("Load Temporary Add-on" in
`about:debugging`), but that copy is gone the next time Firefox starts, and Canvas would stop syncing without a word.
So the Firefox copy is a packed, signed add-on, and it's connected the way a Chrome Web Store copy is: the student
pastes a code from Study Stash.

## What's built

- **The zip Mozilla signs.** `StudyStash extension-zip --firefox OUT.zip` packs `Extension.PackForFirefox`: the same
  scripts, popup and icons as the Chrome Web Store zip, byte for byte, with Firefox's manifest. Like the store zip it
  never holds a `config.json`, and the same engine always makes the same bytes.
- **Firefox's manifest** (`Extension.FirefoxManifest`) is the store manifest (no site until it's connected,
  `optional_host_permissions` `https://*/*` and `http://*/*`) with four differences:
  - `"background": {"scripts": ["connection.js", "background.js"]}`. Firefox runs an extension's background as a page,
    not a service worker. `background.js` calls `importScripts` only where there is one.
  - `browser_specific_settings.gecko`: the add-on's ID `canvas@study-stash-app.web.app` (`Extension.FirefoxId`),
    `strict_min_version` 140.0 and `data_collection_permissions` (below).
  - `"content_security_policy": {"extension_pages": "script-src 'self'"}`. Firefox's default policy for Manifest V3
    turns every request into https. A library on the student's own network is reached over plain http, so the policy
    is stated without that.
  - No `minimum_chrome_version`.
- **Site patterns without a port.** Chrome grants `http://mini.local:8787/*`. Firefox grants `http://mini.local/*`
  (that host on every port) and never says a pattern with a port is allowed, so in Firefox `connection.js` asks for,
  and checks, the pattern without it. The library is still reached on its port.
- **It says which browser it is.** From 1.6 every request for work carries `b=<browser>` ("Chrome", "Edge", "Brave",
  "Firefox"; Zen says Firefox). The library keeps it with each copy (`extension.browser` in the state), and its words
  name that browser. The extension's own words do too ("Sign in to Canvas in Firefox.").
- **Connecting by code** is the store copy's flow unchanged ([chrome-web-store.md](chrome-web-store.md#whats-built)):
  paste the code, **Connect**, then **Allow** when Firefox asks for the code's three sites. Firefox may close the popup
  while it asks. The connection is saved before it asks, and the background page starts as soon as the sites are
  allowed.
- **Where the code is shown.** The library's Settings page, under Canvas, **Set up the extension on this computer**
  (the code for a browser on the library's computer). In the app, the Canvas connect step shows it when the student's
  browser is Firefox.
- **A Study Stash that doesn't know where the add-on is.** With `Extension.FirefoxPublishedAt` empty (as it was
  through 0.16.2, and would be again if the listing ever had to come down) the step says Firefox can't run the
  extension yet and uses another browser. A student whose usual browser is Firefox (or one built on it) also gets
  **I already have it in Firefox**: the step becomes that browser's, with the code to paste and nothing to add.

## What a student sees

**Add to Firefox** in the Canvas step opens Mozilla's page for the add-on in their Firefox and shows the code. From
there, as release Firefox 157 words it:

1. **Mozilla's page.** A blue **Add to Firefox** button. Above it sits Mozilla's notice for every add-on it hasn't
   picked as Recommended: "This add-on is not actively monitored for security by Mozilla. Make sure you trust it
   before installing."
2. **Firefox asks to add it.** "Add Study Stash for Canvas", with "The developer says this extension collects:
   website content, personal communications" ([what it says it sends](#4-what-it-says-it-sends)). **Add**, or Cancel.
3. **The add-on's button**, under the puzzle-piece Extensions button until it's pinned. Its popup says "Paste the
   code from Study Stash". Paste, **Connect**.
4. **Firefox asks for the sites.** "Study Stash for Canvas requests additional settings", listing the school's
   Canvas, `inscloudgate.net` and the library's address. **Allow**, or Deny.

The add-on checks in with the library within a second or two of Allow, and the step moves on to the courses. The
popup then says "Canvas synced just now." (or "never", before the first sync).

The step's own words follow those four: "On the page that opens in Firefox, click the blue Add to Firefox button,
then Add when Firefox asks", then the button and the code, then "Click Allow when Firefox asks".

## Tested

`ExtensionStoreTests` checks the Firefox zip and its manifest. `ExtensionScriptTests` runs the real scripts in Jint as
Firefox runs them (no `importScripts`, `moz-extension://`): patterns without a port, `b=Firefox`, and the words.

**The published copy, in release Firefox (2026-10-13, Firefox 157.0.1 on a Mac).** The file Mozilla serves holds the
repo's scripts, popup and icons byte for byte, plus Mozilla's signature. A Firefox with a fresh profile, which
installs only what Mozilla signed, went to the listing, pressed **Add to Firefox** and answered **Add**. The add-on's
real toolbar popup took the code the app makes (`Extension.ConnectionCode` from the library's key and Canvas address),
**Connect**, and Firefox's question was answered **Allow**. Against this engine's library and a pretend Canvas it
then checked in as Firefox (version 1.6, protocol 3), answered Find my courses at once, and synced a class: 27
requests to Canvas, every one with the session of the tab signed in to it, and the class's files on disk. It ran
twice: with the library at `127.0.0.1`, and at this Mac's address on its network over plain http, the way a laptop
reaches a library on another computer. Firefox's two questions were answered by pressing their buttons from a script,
and the code was put in the field by one; nothing else stood in for the student.

**Before it was published (Firefox Developer Edition 158, the packed zip, 2026-10-06):** the background page read a
pretend Canvas with the cookies of a tab signed in to it (including `SameSite=Strict` ones), handed back JSON and a
file, refused a URL that wasn't Canvas, and kept asking through 70 idle seconds. In Zen (Firefox 143) the same zip
installed, named itself and asked for the same sites.

Neither run is in the repo's tests: they need a Firefox on the machine. Not tried: anything on Windows, Mozilla's
page in Zen, LibreWolf or Waterfox (the step's words take its button to say Add to Firefox there too), and a
student's own hand on the two questions.

**The popup's page in a tab is not the popup.** Opened as `moz-extension://…/popup.html` in a tab, Firefox 157 holds
the page's request to a library on this computer behind a question of its own ("wants to access other apps and
services on this device"), and the page says "Can't reach your library" until it's answered. The toolbar popup and
the background page are not asked. Test with the real popup.

## Try a copy that isn't the published one

Firefox **Developer Edition** and **Nightly** install an unsigned add-on once told to. The release Firefox doesn't.

1. `StudyStash extension-zip --firefox ~/Desktop/study-stash-for-canvas.xpi` (an `.xpi` is the same zip).
2. In Firefox Developer Edition open `about:config` and set `xpinstall.signatures.required` to `false`.
3. Open `about:addons`, the gear, **Install Add-on From File…**, and pick the `.xpi`.
4. Click the **S.** button (under the puzzle-piece Extensions button until it's pinned), paste the code from Study
   Stash, **Connect**, then **Allow**.

For a quick look in any Firefox: `about:debugging`, **This Firefox**, **Load Temporary Add-on…**, and pick the `.xpi`.
It lasts until Firefox closes.

To point the app's Firefox step somewhere else than Mozilla's page, start the app with `STUDYSTASH_FIREFOX_ADDON`
set to a web address: that's what **Add to Firefox** opens. A file's path isn't opened (the app hands a browser web
addresses only).

## Publishing it

### 1. A developer account

Sign in at [addons.mozilla.org/developers](https://addons.mozilla.org/developers/) with a Mozilla account. It's free.
Use an account that will keep working after graduation: the add-on belongs to it.

### 2. The ID never changes

Mozilla signs the add-on under `canvas@study-stash-app.web.app`, and every update must carry the same ID. It doesn't
have to be a real address. The first upload was made under it, so `Extension.FirefoxId` stays as it is.

### 3. The first upload

```sh
StudyStash extension-zip --firefox ~/Desktop/study-stash-for-canvas-1.6.zip
```

**Submit a New Add-on**, **On this site** (listed: Firefox then updates installed copies by itself), and upload the
zip. The validator runs at once (no errors; one warning, that Firefox for Android 140 doesn't know
`data_collection_permissions`, which doesn't matter while Android stays unticked). Tick **Firefox** only. Answer **No**
to "do you need to submit source code": the scripts and the popup are hand-written and go in as they are. Only
`manifest.json` is written by the packing command, and it's plain JSON; the notes to the reviewer say so.

- **Name, summary** come from the manifest. **Categories**: there's none for education, so **My add-on doesn't fit
  into any of the categories**.
- **Add-on URL (slug)**: `study-stash-for-canvas`, so the page is
  `https://addons.mozilla.org/firefox/addon/study-stash-for-canvas/`.
- **Description**: what it does (copies your Canvas courses into your own Study Stash library), that it only reads and
  uses your own sign-in, that it does nothing until you paste a code from Study Stash, and that nothing goes anywhere
  except your library.
- **License**: the repo's.
- **Privacy policy**: required, because it sends data out of the browser. The site's privacy page says what's read
  and where it goes.
- **Notes to reviewer**: it needs a Study Stash library to do anything. Say how to get one (the app's download
  link), that the code is shown in Settings, Canvas, and that every site it asks for comes from that code.

### 4. What it says it sends

Firefox shows this before the student adds it. The manifest declares `websiteContent` (Canvas's pages, assignments and
files) and `personalCommunications` (announcements and instructor feedback are messages), as the Chrome Web Store
listing does ([chrome-web-store.md](chrome-web-store.md#4-privacy-practices-the-dashboards-privacy-tab)). They go only
to the student's own library. Mozilla counts anything that leaves the browser, so it's declared rather than left out.
It reads no passwords and no cookies: Firefox sends the student's own Canvas session with each request.

### 5. Review, then one line in the engine

Listed add-ons are usually reviewed within a few days; 1.6 was public three days after it went in. Optional access to every site gets a closer
look, like in Chrome's store, and the reviewer notes above answer the usual question.

`https://addons.mozilla.org/api/v5/addons/addon/study-stash-for-canvas/` answers 401 while it's awaiting review and
200 once it's public (its `status` is then `public`, and `current_version.file.url` is the signed file). Once the
page was live, `Extension.FirefoxPublishedAt` was set to its address
(`https://addons.mozilla.org/firefox/addon/study-stash-for-canvas/`). Emptied, the app stops offering Firefox, so
nothing a student sees depends on an add-on that isn't there.

### Signing it without listing it

`web-ext sign --channel=unlisted` (with an API key from the developer hub) returns a signed `.xpi` in a few minutes,
with no listing and no human review. Study Stash would then have to host that file and hand it out, and Firefox
updates such an add-on only if its manifest names an `update_url` that Study Stash keeps current. Neither is built.
Listing it is less to run.

## Updates

1. Change the files in `extension/` and raise `"version"` in `extension/manifest.json`, as for Chrome
   ([chrome-web-store.md](chrome-web-store.md#updates)). One version covers every browser.
2. `StudyStash extension-zip --firefox study-stash-for-canvas-<version>.zip`.
3. Developer hub, the add-on, **Upload New Version**.
4. Firefox updates installed copies within a day. A copy keeps its pasted connection and its allowed sites.

## Other browsers

- **Edge, Brave, Arc, Opera, Vivaldi** run the Chrome copy as it is: the folder ("Load unpacked" on their own
  extensions page, which `chrome://extensions` opens in each of them), or the Chrome Web Store copy, which they all
  install. Edge's and Opera's own stores take the same store zip.
- **Zen, LibreWolf, Waterfox** install the Firefox add-on from addons.mozilla.org.
- **Safari** isn't built. A Safari extension ships inside a Mac app as a signed app extension, made with Xcode
  (`xcrun safari-web-extension-converter` starts from these same files), and is turned on in Safari's settings. That's
  its own piece of work, with Apple's signing in it.
