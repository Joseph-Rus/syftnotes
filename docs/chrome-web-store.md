# Study Stash for Canvas on the Chrome Web Store

The Canvas extension also works as an unpacked folder that Study Stash writes and keeps up to date ("Load unpacked"
in `chrome://extensions`, see [canvas.md](canvas.md#the-extension)). That needs Developer mode, the hardest step for a
student who isn't technical, and Chrome turns an unpacked extension off if Developer mode is switched off later. A copy
from the Chrome Web Store has neither problem: the student clicks **Add to Chrome**, and Chrome keeps it up to date.

Version 1.6 is published, Unlisted (anyone with the link can install it, nobody finds it by searching):
[Study Stash for Canvas](https://chromewebstore.google.com/detail/study-stash-for-canvas/dfhoenhdjjbnopbepgkgjdgdnfmecjjd),
item ID `dfhoenhdjjbnopbepgkgjdgdnfmecjjd`. `Extension.ChromeStorePublishedAt` names that page, so from the Study Stash
after 0.16.13 the Canvas step offers the store first in Chrome, Edge, Brave, Arc and Vivaldi, and keeps the folder for
the rest. Firefox has its own copy: [firefox-add-on.md](firefox-add-on.md).

## Which browsers get the store

| Browser | Gets | Why |
|---|---|---|
| Chrome | The store | It's Chrome's own store. |
| Edge | The store | Edge installs from the Chrome Web Store after a question of its own the first time (allow extensions from other stores). One more click, and still no Developer mode. |
| Brave | The store | It installs from the Chrome Web Store as it is; the button says Add to Brave. |
| Arc, Vivaldi | The store | They install from the Chrome Web Store as it is; the button says Add to Chrome. |
| Opera | The folder | Opera installs from the Chrome Web Store only after an add-on of its own ("Install Chrome Extensions" from Opera's add-ons site), with Opera's own questions after it: two stores and more pages than the folder's three pictures, in words nobody here has seen. |
| Chromium | The folder | Some builds (ungoogled-chromium, for one) can't install from the Chrome Web Store at all, and anyone using Chromium knows Load unpacked. |
| Firefox, Zen, LibreWolf, Waterfox | Mozilla's add-on | [firefox-add-on.md](firefox-add-on.md). |

`Browser.StoreButton` holds what each browser's store button says, and `Browsers.StorePage` where each one gets the
extension ("" for the folder). Emptying `Extension.ChromeStorePublishedAt` sends every browser of Chrome's family back to
the folder, as it was through 0.16.13.

## What a student sees

**Add to Chrome** in the Canvas step opens the store's page in their browser and shows the code. The step's three
lines:

1. **The store's page.** "On the page that opens in Chrome, click the blue Add to Chrome button, then Add extension
   when Chrome asks." Brave's button says Add to Brave; Arc's and Vivaldi's say Add to Chrome. Chrome then asks
   whether to add Study Stash for Canvas: **Add extension** or Cancel. Edge's page says **Get extension** instead, and
   the first time Edge asks whether to allow extensions from other stores (**Allow**), then **Add extension**: "On the
   page that opens in Edge, click Get extension. If Edge asks to allow extensions from other stores, click Allow. Then
   click Add extension."
2. **The extension's button.** "Click the Study Stash button (it may be under the puzzle-piece Extensions button),
   paste this code and click Connect." The code, with **Copy**, sits under this line.
3. **The browser asks for the sites.** "Click Allow when Chrome asks." Chrome lists the school's Canvas,
   `inscloudgate.net` and the library's address, with **Allow** or Deny.

The extension checks in within a second or so of Allow, and the step moves on to the courses. Under the step, once Add
to Chrome has been pressed: **Open the Chrome Web Store again** (and **Use another browser** when there's one), and on a
line of its own **Can't add it from the store? Load it from a folder instead**, which turns the step into the folder's
three pictures and its own Add to Chrome; from there **Add it from the Chrome Web Store instead** goes back. Guided
setup's Canvas card has the same three lines, code and link.

The store's buttons and questions in Edge, Brave, Arc and Vivaldi come from Microsoft's, Brave's and Vivaldi's own help
pages and guides (read 2026-10-14), not from those browsers on this computer. Older guides show Add to Chrome in Edge
too; Microsoft's page now says Get extension.

## The extension's ID

- **The store copy's ID is `dfhoenhdjjbnopbepgkgjdgdnfmecjjd`** (`Extension.ChromeStoreId`). The package the store
  serves says so in its signed header (`crx_id`), and its own public key hashes to it.
- **An unpacked copy's ID is different**, made from the folder's path: the repo's `manifest.json` has no `key`, and
  `Extension.Ensure` adds none. So a folder's copy and the store's can both be installed in one browser; both then check
  in with the same key, and the library hands each of them work. A student switching to the store copy can remove the
  folder's from the browser's extensions page, but nothing breaks if they don't.
- **Nothing goes by either ID.** The library knows the extension by its key (`X-Study-Stash-Key`, in the code) or the
  library's password, never by its origin: `/api/v2/canvas/*` has no `Origin` check and no list of allowed origins, and
  an extension reaching a site it has permission for isn't held to CORS. The extension's own scripts only look at their
  address's scheme (`moz-extension:` means Firefox). The code carries the library's address, the key and the Canvas
  address, no ID. Chrome sends `Origin: chrome-extension://<id>` with the extension's POSTs (its answers) and none
  with its GETs (its requests for work), so the library couldn't tell the store's copy by its ID reliably anyway.
- **Never called outdated.** A store copy updates through the store, often days after a new Study Stash has a newer
  folder. The library calls an extension outdated only when it can't bring itself up to date (from before protocol 2),
  so a store copy of any version is never told to reload, and every version gets work.

## The published copy is the repo's 1.6

Checked 2026-10-14. The package from
`https://clients2.google.com/service/update2/crx?response=redirect&prodversion=130.0&acceptformat=crx2,crx3&x=id%3Ddfhoenhdjjbnopbepgkgjdgdnfmecjjd%26uc`
(`DFHOENHDJJBNOPBEPGKGJDGDNFMECJJD_1_6_0_0.crx`, 33,908 bytes, CRX3) holds `background.js`, `connection.js`,
`popup.html`, `popup.js` and the four icons byte for byte as `extension/` has them. Its `manifest.json` is
`Extension.StoreManifest()` (no `host_permissions`, `optional_host_permissions` for any site) with one line the store
adds, `"update_url": "https://clients2.google.com/service/update2/crx"`. The store also adds
`_metadata/verified_contents.json`, its own signed list of the files' hashes. There's no `config.json`, no `config.js`
and no `key`.

## What's built

- **The zip to upload.** `StudyStash extension-zip OUT.zip` (it needs no `--home` and writes nothing else) packs the
  extension as `Extension.PackForStore` makes it: the files at the top of the zip, in name order, with fixed times, so
  the same engine always makes the same bytes. It holds `background.js`, `connection.js`, `popup.html`, `popup.js`,
  the four icons and `manifest.json`, and never a `config.json` or `config.js`.
- **The store manifest** (`Extension.StoreManifest`) is the repo's `extension/manifest.json` with **no**
  `host_permissions` and `"optional_host_permissions": ["https://*/*", "http://*/*"]`. When the extension is
  published nobody knows the student's school Canvas or where their library is, so the extension asks for exactly
  those sites later, when it is connected. Nothing is granted at install. Everything else is shared with the unpacked
  folder: name **Study Stash for Canvas**, short name **Study Stash**, the description (132 characters at most),
  `minimum_chrome_version` 120, `permissions` `["alarms", "storage"]` and icons at 16, 32, 48 and 128 px.
- **Connecting by code.** A store copy has no folder for Study Stash to write, so the student pastes a code instead:
  - The code is `config.json`'s text (`{app, key, canvas, files, protocol}`) in base64url (`Extension.ConnectionCode`).
    The library gives it for Chrome on the library computer as `connection_code` in `GET /api/v2/canvas/extension`
    (library address `http://127.0.0.1:<port>`). The laptop app makes its own, with the library address the laptop
    uses (`ExtensionKeeper.ConnectionCode`). There's no code until the library has a Canvas address.
  - With no connection, the popup shows **Paste the code from Study Stash** and a **Connect** button. The click decodes
    the code (`connection.js decodeCode`; spaces, line breaks and quotes from copying are fine), asks Chrome for the
    code's Canvas, Canvas's file store and the library (`chrome.permissions.request`, which must run inside the click)
    and saves the connection in `chrome.storage.local.connection`. When Chrome says yes, the service worker starts
    straight away (`chrome.permissions.onAdded`).
  - If the student said no, the extension reaches nothing: its status is `no_access`, the toolbar button shows `!`,
    and the popup offers **Allow** to ask again. A connected store copy also shows **New code**, for when the library
    moves or its key changes.
  - An unpacked folder's copy never shows any of this: its `config.json` comes first.

**Where the code is shown.** The app's Canvas connect step (in setup, guided setup and Settings), for every browser
that gets the store; and the library's Settings page, under Canvas, **Set up the extension on this computer**, which
links the store's page and Mozilla's and shows the code for a browser on the library's computer, with the folder below
for Opera.

**Tested.** `ExtensionStoreTests` checks the zip and its manifest against the store's rules, that packing twice gives
the same bytes, the code going both ways and the `extension-zip` command. `ExtensionScriptTests` runs the real
`connection.js` and `background.js` in Jint: decoding a code, the sites it asks for (the same as a folder's manifest
for that connection), `no_access` without Chrome's permission and the library reached once it's given. In a real
Chrome (`engine/tests/extension-e2e.sh`): story `S9b`, the unzipped store build registers, says `no_config`, has no
sites, its popup asks for the code, and after the code is pasted it waits (`no_access`) without asking Canvas or the
library anything; story `S9c`, the same build under the store's own ID (the store item's public key in its manifest)
takes the library's code, checks in as connected, answers Find my courses and isn't called outdated. Headless Chrome
can't click Chrome's own permission prompt, so in `S9c` the three sites Allow would grant are in the manifest instead.
`CanvasConnectTests` checks the step: Chrome and Edge get the store's words, page and code, Opera the folder, and the
folder is one link away. `CanvasExtensionTests` checks an older store copy still gets work and isn't called outdated.

Not tried: a real install from the store (it needs a real browser profile and a person's clicks), and the store's
page in Edge, Brave, Arc or Vivaldi themselves. The step's words for them come from those browsers' own help pages.

## Publishing it

### 1. A developer account

Register at the [Chrome Web Store developer dashboard](https://chrome.google.com/webstore/devconsole) with a Google
account. It costs a **one-time US$5** fee. Use an account that will keep working after graduation, not a school one:
the item belongs to it. Verify the contact email the dashboard asks for.

### 2. The first upload and the extension's ID

```sh
StudyStash extension-zip ~/Desktop/study-stash-for-canvas-1.6.zip
```

In the dashboard choose **New item** and upload the zip. The item gets its ID now (32 letters, a to p), and it never
changes. The student's install link is `https://chromewebstore.google.com/detail/<id>`, which is the one link
"Add to Chrome" in Study Stash should open.

**A stable ID for the unpacked copy too (not done).** An unpacked extension's ID comes from its folder's path, so it
differs between computers. To give it the store's ID, copy the item's public key (dashboard, **Package**,
**View public key**) into the unpacked manifest's `"key"` field. `Extension.Ensure` would add it, and
`PackForStore` must leave it out, because the store rejects a manifest with `key`. Two copies with the same ID can't be
installed side by side, so a student switching to the store copy would have to remove the unpacked one first. Nothing
needs the extension's ID ([above](#the-extensions-id)), so the folder keeps its own.

### 3. The listing

- **Visibility: Unlisted.** Only people with the link can find it. That is enough for Study Stash, whose app hands out
  the link, and it keeps the extension out of search results where people without a library would install it.
  (Private is only for named testers or a Google Workspace domain.)
- **Name and summary** come from the manifest: "Study Stash for Canvas" and its description.
- **Detailed description**, plain text: what it does (copies your Canvas courses, meaning assignments, your
  submissions and feedback, modules, files and announcements, into your own Study Stash library), that it only reads
  and uses your own sign-in, that it does nothing until you paste a code from Study Stash, and that nothing goes
  anywhere except your library.
- **Category**: Education. **Language**: English.
- **Icon**: 128 × 128 (`extension/icon-128.png`: the "S." tile at 96 px inside Chrome's 16-px margin; redraw every
  size with `swift macos/make_icon.swift out.chrome` and copy them into `extension/`).
- **Screenshots**: at least one, 1280 × 800 (or 640 × 400). For example: the popup connected ("Canvas synced just
  now") over a Canvas course page, and the popup asking for the code. Use the design's made-up classes (CS 101,
  BIO 110), never a real school or real names.
- **Small promo tile**: 440 × 280, if the dashboard asks for one.

### 4. Privacy practices (the dashboard's Privacy tab)

- **Single purpose**: "Copies the student's own Canvas course content into their own Study Stash library, which they
  run themselves."
- **Permission justifications**:
  - `alarms`: checks in with the student's library every 30 seconds, so a sync or a question about Canvas starts
    promptly.
  - `storage`: keeps the connection the student pasted, and the extension's own status.
  - **Host permissions** (the optional `https://*/*` and `http://*/*`): the school's Canvas address and the student's
    library address are different for every student and aren't known when it's published. Nothing is granted at
    install. When the student pastes their code, the extension asks Chrome for exactly three sites (their Canvas,
    Canvas's file store `*.inscloudgate.net`, their library) and Chrome shows them which ones.
- **Remote code**: No. All the code is in the package. It fetches only data (Canvas's JSON and files) and runs none.
- **Data usage**: tick what it handles: *Website content* (course pages, assignments, files) and, because
  announcements and instructor feedback are messages, *Personal communications*. It collects no passwords: Chrome
  sends the student's own Canvas session cookie, and the extension never reads it.
- **Certify**: the data is not sold, not used or transferred for purposes unrelated to the single purpose, and not
  used to decide creditworthiness or lending.
- **Privacy policy URL**: required once user data is declared. A page in the repo is enough (not written yet, e.g.
  `docs/privacy.md` on GitHub): what's read, that it goes only to the student's own library on a computer they
  control, that there's no analytics and no third party, and how to remove it (remove the extension; delete the
  class's Canvas folder in the library).

### 5. Review

Submit for review. Google says most items are reviewed within a few days. Broad host permissions (even optional
ones) get a closer look and can take longer. Clear justifications above help. A rejection email names the policy.
Fix it, bump the version and resubmit.

## Updates

1. Change the files in `extension/` and raise `"version"` in `extension/manifest.json` (1.6 → 1.7; `1.10` comes after
   `1.9`). **Every change to a shipped file needs a new version**: the store refuses an upload that isn't higher than
   the published one, and unpacked copies only reload themselves when the version (or their sites) changes
   ([canvas.md](canvas.md#versions-updates-and-reload)). The extension's version is its own and never follows the
   app's.
2. `StudyStash extension-zip study-stash-for-canvas-<version>.zip`.
3. Dashboard, the item, **Package**, **Upload new package**, then **Submit for review**.
4. Once it's approved, Chrome updates installed copies by itself, usually within hours. A store copy keeps its pasted
   connection and its granted sites across updates. A new version that needs a new kind of site has to ask for it
   again, and the library must keep answering older protocols, since not every copy updates at once.

Unpacked folders keep updating the way they do now: the library and the app rewrite their folders, and the extension
reloads itself from them.

## Keep "Load unpacked"

The store copy doesn't replace the folder. A school-managed Chrome (a Chromebook, or Chrome with an
`ExtensionInstallAllowlist`/`ExtensionInstallBlocklist` policy) may block store installs of anything not approved.
Some managed Chromes block Developer mode instead, and then only the store copy works. So the step offers both: Add to
Chrome opens the store first, and **Can't add it from the store? Load it from a folder instead** turns it into the
folder's steps. Opera and Chromium get the folder straight away.
