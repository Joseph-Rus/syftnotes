# Study Stash for Canvas on the Chrome Web Store

The Canvas extension works today as an unpacked folder that Study Stash writes and keeps up to date ("Load unpacked"
in `chrome://extensions`, see [canvas.md](canvas.md#the-extension)). That needs Developer mode, and Chrome turns an
unpacked extension off if Developer mode is switched off later. A copy from the Chrome Web Store has neither problem:
the student clicks **Add to Chrome**, and Chrome keeps it up to date.

Nothing here has been published. This page is what it takes, and what is already built for it.

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

**What still needs doing in the app.** Nothing in the app shows the code yet. The Canvas setup screen should offer
"Add to Chrome" (the store link), then show the code with a Copy button (the laptop app from
`ExtensionKeeper.ConnectionCode`, the library page from `connection_code`). Keep "Load unpacked" as the fallback.

**Tested.** `ExtensionStoreTests` checks the zip and its manifest against the store's rules, that packing twice gives
the same bytes, the code going both ways and the `extension-zip` command. `ExtensionScriptTests` runs the real
`connection.js` and `background.js` in Jint: decoding a code, the sites it asks for (the same as a folder's manifest
for that connection), `no_access` without Chrome's permission and the library reached once it's given. In a real
Chrome (`engine/tests/extension-e2e.sh`, story `S9b`) the unzipped store build registers, says `no_config`, has no
sites, its popup asks for the code, and after the code is pasted it waits (`no_access`) without asking Canvas or the
library anything. Headless Chrome can't click Chrome's own permission prompt, so the step after **Allow** is covered
only in Jint.

## Publishing it

### 1. A developer account

Register at the [Chrome Web Store developer dashboard](https://chrome.google.com/webstore/devconsole) with a Google
account. It costs a **one-time US$5** fee. Use an account that will keep working after graduation, not a school one:
the item belongs to it. Verify the contact email the dashboard asks for.

### 2. The first upload and the extension's ID

```sh
StudyStash extension-zip ~/Desktop/study-stash-for-canvas-1.4.zip
```

In the dashboard choose **New item** and upload the zip. The item gets its ID now (32 letters, a to p), and it never
changes. The student's install link is `https://chromewebstore.google.com/detail/<id>`, which is the one link
"Add to Chrome" in Study Stash should open.

**A stable ID for the unpacked copy too (optional).** An unpacked extension's ID comes from its folder's path, so it
differs between computers. To give it the store's ID, copy the item's public key (dashboard, **Package**,
**View public key**) into the unpacked manifest's `"key"` field. `Extension.Ensure` would add it, and
`PackForStore` must leave it out, because the store rejects a manifest with `key`. Two copies with the same ID can't be
installed side by side, so a student switching to the store copy removes the unpacked one first. This isn't built:
add it only if the app needs to recognise the extension by ID.

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
- **Icon**: 128 × 128 (`extension/icon-128.png`).
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

1. Change the files in `extension/` and raise `"version"` in `extension/manifest.json` (1.4 → 1.5; `1.10` comes after
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
Some managed Chromes block Developer mode instead, and then only the store copy works. Offer both: "Add to Chrome"
first, and "Load unpacked" (Study Stash's folder) when the store is blocked or while the item is in review.
