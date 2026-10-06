# Study Stash for Canvas in Firefox

The Canvas extension is one set of files (`extension/`) that runs in two families of browser:

- **Chrome and the browsers built on it** (Edge, Brave, Arc, Opera, Vivaldi, Chromium). They load the folder Study
  Stash writes ("Load unpacked", see [canvas.md](canvas.md#the-extension)), or a copy from the Chrome Web Store
  ([chrome-web-store.md](chrome-web-store.md)).
- **Firefox and the browsers built on it** (Zen, LibreWolf, Waterfox). This page.

Version 1.6 was submitted to addons.mozilla.org on 2026-10-06 (listed, add-on URL `study-stash-for-canvas`) and is
awaiting review. Until it's approved and `Extension.FirefoxPublishedAt` is set, the app doesn't offer Firefox. This
page is what's built, how to try it today, and what publishing took.

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

**Tested.** `ExtensionStoreTests` checks the Firefox zip and its manifest. `ExtensionScriptTests` runs the real scripts
in Jint as Firefox runs them (no `importScripts`, `moz-extension://`): patterns without a port, `b=Firefox`, and the
words. In a real Firefox (Developer Edition 158, driven over WebDriver BiDi with the permission prompt answered by a
preference) the packed zip was installed, its popup took a pasted code, and the background page then asked a library
on this Mac's network address over plain http, read a pretend Canvas with the cookies of a tab signed in to it
(including `SameSite=Strict` ones), handed back JSON and a file, refused a URL that wasn't Canvas, and kept asking
through 70 idle seconds. In Zen (Firefox 143) the same zip installed, named itself and asked for the same sites.
That run isn't in the repo's tests: it needs a Firefox on the machine.

## Try it today

Firefox **Developer Edition** and **Nightly** install an unsigned add-on once told to. The release Firefox doesn't.

1. `StudyStash extension-zip --firefox ~/Desktop/study-stash-for-canvas.xpi` (an `.xpi` is the same zip).
2. In Firefox Developer Edition open `about:config` and set `xpinstall.signatures.required` to `false`.
3. Open `about:addons`, the gear, **Install Add-on From File…**, and pick the `.xpi`.
4. Click the **S.** button (under the puzzle-piece Extensions button until it's pinned), paste the code from Study
   Stash, **Connect**, then **Allow**.

For a quick look in any Firefox: `about:debugging`, **This Firefox**, **Load Temporary Add-on…**, and pick the `.xpi`.
It lasts until Firefox closes.

To try the app's own Firefox step before the add-on is published, start the app with `STUDYSTASH_FIREFOX_ADDON` set
to the `.xpi`'s path (or a web address): that's what **Add to Firefox** opens.

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

Listed add-ons are usually reviewed within a few days. Optional access to every site gets a closer look, like in
Chrome's store, and the reviewer notes above answer the usual question.

`https://addons.mozilla.org/api/v5/addons/addon/study-stash-for-canvas/` answers 401 while it's awaiting review and
200 once it's public. When the page is live, set `Extension.FirefoxPublishedAt` to its address
(`https://addons.mozilla.org/firefox/addon/study-stash-for-canvas/`). Until then it's empty and the app doesn't
offer Firefox, so nothing a student sees depends on an add-on that isn't there.

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
