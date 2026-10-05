# Signing Study Stash

Today the Mac app is signed "ad hoc" and the Windows installer isn't signed, so a Mac asks you to go to System
Settings → Privacy & Security → Open Anyway, and Windows says "Windows protected your PC". The CI pipeline can
fix both. It is **already built and waiting**: it stays off until the GitHub secrets below exist, and without them
every build is exactly what it was before. This page is what you buy, what you set up, which secrets to add, and
what students will and won't see.

Nothing here costs anything until you do the setup, and the code needs no change when you do.

## What changes for students (the honest version)

| | Today | Signed and notarized (Mac), signed (Windows) |
|---|---|---|
| **Mac, opening the DMG's app** | "Apple could not verify…" then a trip to Privacy & Security → Open Anyway | One ordinary question the first time ("Study Stash is an app downloaded from the Internet. Open?") with an **Open** button. No trip to Settings. |
| **Mac, the microphone** | macOS asks once | macOS asks once, the same. Signing doesn't skip it: macOS asks every app. |
| **Mac, "record what this computer plays"** | macOS asks once | The same: once. |
| **Mac, permissions after an update** | Probably asked again after each update (below) | Kept across updates (below), but asked once more on the first update that switches from ad hoc to Developer ID |
| **Mac, self-updates** | No question (the app downloads and swaps itself) | The same |
| **Windows, running Setup.exe** | "Windows protected your PC", then More info → Run anyway, with "Unknown publisher" | The publisher's name instead of "Unknown publisher". **SmartScreen can still ask** for a while (below). |
| **Windows, the uninstaller and the app** | Unsigned | Signed, so the app's own files carry the publisher too |

### SmartScreen, honestly

Signing does not switch SmartScreen off on day one. Microsoft says so in its own table: Azure Artifact Signing
"does **not** provide instant SmartScreen trust", and since 2024 even an EV certificate "no longer" bypasses it.
SmartScreen builds a reputation for the signer as people download and run consistently signed releases, and
Microsoft publishes no number for how long that takes. Expect "Windows protected your PC" to keep appearing for
the first weeks or months, now naming the publisher, and then to fade. That is not a sign something is wrong.
(Source: [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).)

### Mac permissions across updates: what I could and couldn't confirm

macOS stores each privacy permission (the microphone, system audio) against the app's **code requirement**, a
rule the app's signature must satisfy.

- **Confirmed here:** an ad hoc signature's designated requirement is the build's own hash. On this Mac,
  `codesign -d -r-` on an ad hoc binary printed `designated => cdhash H"…"`, and that hash is different for every
  build, so a permission granted to one build matches no later build.
- **Confirmed by Apple:** an Apple engineer on the Developer Forums says what TCC (the privacy system) does with an
  app depends on its designated requirement and that "when dealing with TCC it's best to sign your code with a stable
  signing identity, typically Apple Developer for day-to-day work and Developer ID for final distribution", which
  "will radically cut down on the amount of TCC thrash"
  ([forum thread 730043](https://developer.apple.com/forums/thread/730043), pointing to
  [TN3127, Inside Code Signing: Requirements](https://developer.apple.com/documentation/technotes/tn3127-inside-code-signing-requirements)).
- **Not confirmed:** I found no Apple page that says in one sentence "permissions survive updates", and I could not
  test it: that needs a real Developer ID and the real prompts. Developers' reports (many GitHub issues about
  ad hoc builds asking again after every auto-update) agree with it. The expectation, then: with a Developer ID the
  requirement names your Team ID instead of a hash, so it matches every release you sign, and the permissions stay.
  The first update from an ad hoc build to a Developer ID one is a different signature, so macOS asks once more.
  Check it yourself on the first two signed releases: grant the microphone, update, record.

## The accounts and the cost

| What | Cost | For |
|---|---|---|
| Apple Developer Program | **$99 a year** | The Developer ID certificate, and notarization (which is free once you're in) |
| Azure subscription, and Azure Artifact Signing (Basic) | **$9.99 a month** (about $120 a year), plus the Azure account | Windows signing, with no hardware token |
| Together | about **$220 a year** | |

Check the current prices when you sign up. Alternatives for Windows are below.

## Part 1: the Mac

You need a Mac (you have one), and you do this once.

### 1. Join the Apple Developer Program

1. Go to <https://developer.apple.com/programs/enroll/> and enroll with your Apple ID (two-factor authentication on).
2. Enroll as an **individual**. Apple checks your identity, which can take minutes or a couple of days, and charges
   $99 a year. The certificate then carries **your legal name** (visible to anyone who runs `codesign -dvv` on the
   app, not in the Gatekeeper dialog). To have "Study Stash" there instead you would enroll as an organization,
   which needs a registered legal entity and a D-U-N-S number.
3. Only the **Account Holder** (you) can create the certificate below.

### 2. The Developer ID Application certificate

1. On your Mac, open **Keychain Access → Certificate Assistant → Request a Certificate From a Certificate
   Authority…**. Enter your email and name, choose **Saved to disk**, and save the `.certSigningRequest`.
2. At <https://developer.apple.com/account/resources/certificates/add> choose **Developer ID Application** (the
   **G2 Sub-CA** one), upload the request, and download the `.cer`. Double-click it to add it to your login keychain.
3. In Keychain Access, **My Certificates**, find "Developer ID Application: Your Name (TEAMID)", right-click →
   **Export…**, format **.p12**, and give it a password you'll remember.
4. Put it where the CI can read it: `base64 -i DeveloperID.p12 | pbcopy`. That's the value of the secret below.
   Keep the `.p12` and its password somewhere safe (a password manager); delete the copy on the Desktop.
5. Your **Team ID** is the ten characters in the brackets after your name, and on the Membership page of your account.

### 3. The notarization key

Notarization is Apple scanning the app and handing back a ticket. CI logs in with an App Store Connect API key:

1. At <https://appstoreconnect.apple.com/access/integrations/api> choose **Team Keys → Generate API Key**
   (you may be asked to request access to the API first).
2. Name it "Study Stash CI". Access: **Developer**. (Apple's docs don't say which role notarization needs. Reports
   agree that Developer is enough; one older guide says App Manager. If the first notarization is refused with a 401
   or 403, make a new key with App Manager.)
3. Copy the **Issuer ID** (a UUID, above the list of keys) and the **Key ID** (ten characters).
4. **Download the `.p8` once**: Apple lets you download it only the one time.
5. `base64 -i AuthKey_XXXXXXXXXX.p8 | pbcopy`

### 4. The GitHub secrets for the Mac

Repository → Settings → Secrets and variables → Actions → **New repository secret**. All of them, or none:

| Secret | Value |
|---|---|
| `MACOS_CERT_P12_BASE64` | the `.p12`, as base64 (step 2.4) |
| `MACOS_CERT_PASSWORD` | the password you gave the `.p12` |
| `APPLE_TEAM_ID` | the ten-character Team ID |
| `APPLE_NOTARY_KEY_P8_BASE64` | the `.p8`, as base64 (step 3.5) |
| `APPLE_NOTARY_KEY_ID` | the Key ID |
| `APPLE_NOTARY_ISSUER_ID` | the Issuer ID |

With the first three but not the notary ones, CI signs but doesn't notarize (and warns), which doesn't get rid of
the Open Anyway trip; it is only useful for a first try. The notary secrets without the certificate stop the run.

## Part 2: Windows

Pick **A** unless you can't.

### A. Azure Artifact Signing (what CI is built for)

Microsoft's managed signing, called **Trusted Signing** until early 2026: the certificate lives in Microsoft's
hardware and CI asks for each file to be signed, so there is no token and no certificate file to leak. Basic is
**$9.99 a month** for 5,000 signatures. Microsoft's current terms (checked in October 2026, and they change):

- **Individuals** can sign up only in the **USA and Canada**; **organizations** in the USA, Canada, the EU and the UK.
  Elsewhere Microsoft points you to an OV certificate (B below). If you're not in one of those places, skip to B.
- You need a paid Azure subscription and to pass Microsoft's **identity validation** (a photo ID and a check, taking
  a few business days). It is validated as you, so your legal name is the publisher.
- Its certificates last three days and are renewed for you; the timestamp CI adds is what keeps a signature valid
  afterwards.

Setup, in the Azure portal (<https://portal.azure.com>):

1. Create an Azure account and subscription if you have none (it needs a credit card).
2. Create a **resource group**, then an **Artifact Signing account** in it (SKU **Basic**). Remember the **region**
   you pick and the account name.
3. In the account: **Identity validation → New identity → Individual**, and finish the check.
4. Once it's approved: **Certificate profiles → Create → Public Trust**, using that identity. Remember the profile name.
5. **Microsoft Entra ID → App registrations → New registration** (call it "Study Stash CI"). On its page, copy the
   **Directory (tenant) ID** and **Application (client) ID**, then **Certificates & secrets → New client secret**
   and copy the secret's **Value** now (it's shown once, and expires: put the date in your calendar).
6. In the signing account: **Access control (IAM) → Add role assignment →
   Artifact Signing Certificate Profile Signer**, assigned to that app registration. (It was "Trusted Signing
   Certificate Profile Signer" before the rename.)

The GitHub secrets for Windows (all six, or none):

| Secret | Value |
|---|---|
| `AZURE_TENANT_ID` | Directory (tenant) ID |
| `AZURE_CLIENT_ID` | Application (client) ID |
| `AZURE_CLIENT_SECRET` | the client secret's Value |
| `WINDOWS_SIGNING_ENDPOINT` | the endpoint of **your account's region**, e.g. `https://eus.codesigning.azure.net` for East US (the full list is on [Microsoft's page](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations); a region that doesn't match gives a 403) |
| `WINDOWS_SIGNING_ACCOUNT` | the Artifact Signing account's name |
| `WINDOWS_SIGNING_PROFILE` | the certificate profile's name |

What CI does with them (`windows/sign.ps1`): signtool from the Windows SDK plus Microsoft's `Microsoft.ArtifactSigning.Client`
plugin, exactly as Microsoft's [signtool instructions](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations)
say, with Microsoft's timestamp server.

### B. An OV certificate from a certificate authority

For anyone outside Artifact Signing's countries. $150 to $300 a year from DigiCert, Sectigo, GlobalSign and others,
with a validation of your identity. **The catch:** since June 2023 the certificate authorities must keep the key on
hardware (a USB token) or in their own cloud, so most no longer give you a `.pfx` file to put in GitHub.

- If yours still gives a `.pfx`, add two secrets and nothing else: `WINDOWS_CERT_PFX_BASE64`
  (`[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`) and `WINDOWS_CERT_PASSWORD`. `windows/sign.ps1`
  already signs with them (CI's rehearsal uses exactly this path, with a throwaway certificate), and signs with
  Azure instead if both sets are present.
- If it's a token or the CA's cloud signing (DigiCert KeyLocker, SSL.com eSigner and the like), `windows/sign.ps1`
  needs a third branch that calls their tool. I haven't written it, since each one differs; the one place to add it
  is the `$Identity` section of that script.

EV certificates cost more ($400 and up) and since 2024 don't get past SmartScreen any faster; not worth it.

### C. A free one for open source

[SignPath Foundation](https://signpath.io) signs open source projects for free, with its own signing service. It
isn't wired in here, and I haven't checked whether Study Stash qualifies (they set conditions on the project and its
history), so it's one to look at if the $10 a month matters.

## What CI does

A run signs only if it builds installers (a release, or a rehearsal by hand with "installers") **and** all the
secrets of a group are there. The first job (`version`) decides, with `.github/scripts/signing-plan.sh`, and says
so on the run's summary. Some of a group's secrets but not all of them fails the run at once, before anything is built.

| | Without secrets, on a pull request, or on a fork | With the secrets |
|---|---|---|
| Merges (not a release) | tested, nothing signed | tested, nothing signed |
| Mac installer | ad hoc signed, as now | **Developer ID**, hardened runtime, secure timestamp, notarized, stapled (the app and the DMG) |
| Windows installer | unsigned, as now | the app's unsigned exe and dlls, **Setup.exe and the uninstaller** signed |

- Forks and pull requests get no secrets from GitHub, and CI doesn't run on pull requests at all.
- Signing is a **step of its own after the build** (`macos/sign-release.sh`, `windows/sign-release.ps1`), so nothing
  the build runs (npm's packages, the .NET restore) ever sees a key, which is where a stolen secret would otherwise
  come from. Each secret reaches only the step that uses it, nothing prints one, and the Mac's keychain, the notary
  key and the temporary files are removed at the end of the job.
- **Mac, in detail:** every file in the bundle is signed (not only the ones that look like Mach-O: the build's old
  hard-won note about `--deep` skipping some stays true, and a new native library from any lane is picked up
  automatically), in parallel and with a retry, since the timestamp server is the slow part; then the launcher; then
  the bundle with its entitlements. A check then insists that every Mach-O carries your Team ID. The app is
  zipped and notarized, **stapled**, put in a new DMG, which is signed, notarized and stapled too. `spctl --assess` and
  `codesign --verify --deep --strict` are run on the Apple silicon runner and again on the Intel one, with the
  Intel Mac running the self-test natively.
- **`disable-library-validation` is gone** from a Developer ID build (`macos/StudyStash.developer-id.entitlements`):
  every library is the app's own or Apple's, and carries your Team ID, so macOS's rule that the app load only those
  can stay on. The microphone and JIT entitlements stay. The self-test runs the signed app, so a library that
  wouldn't load fails the release before it's published. If one ever does, set the repository *variable*
  (Settings → Secrets and variables → Actions → Variables) `STUDYSTASH_LIBRARY_VALIDATION` to `off` and the build
  keeps the exemption.
- **The first notarizations of a new account can take a long time** (Apple's answers sometimes take hours on the
  first ones). The Mac job waits up to two hours, then fails without publishing; run it again.
- **Windows, in detail:** `windows/build.ps1 -NoInstaller` publishes as before; then `windows/sign-release.ps1` signs
  every exe and dll that isn't already signed (Microsoft's own runtime files keep Microsoft's), and runs Inno Setup
  with its `SignTool` and `SignedUninstaller` settings, so Setup.exe and `unins000.exe` are signed too. The installer
  is then installed, updated in place and uninstalled by the same tests as before, and its installed files are checked
  for signatures.
- **The publish step won't ship the wrong thing.** Installers a by-hand run signed with throwaway identities are
  never published, and are never reused by a release (`reuse_run`); installers built before you added the secrets
  (so unsigned) can't be reused by a release that is now supposed to be signed. It says which, and you run the
  rehearsal again.

## Testing it

**Before you've bought anything**: a rehearsal that signs with throwaway self-signed identities made on the
runners (a self-signed certificate on the Mac, in a temporary keychain; one in the Windows certificate store):

    gh workflow run ci --ref <branch> -f installers=true -f test_signing=true

It proves every signing step: files signed, entitlements, the Mac's signed DMG, Setup.exe and the uninstaller
signed, the signed app self-tested, the signed installer installing, updating and uninstalling. It does **not** prove
notarization or Azure, which need the real accounts, and Gatekeeper and SmartScreen rightly reject what it makes.
It publishes nothing and its installers can never be published.

**With the secrets**: the same without `test_signing`:

    gh workflow run ci --ref <branch> -f installers=true

signs for real, notarizes, publishes nothing, and leaves the installers as run artifacts (`mac-installers`,
`windows-installers`). Download them and check them on your own computers:

    sh macos/verify-signing.sh "/Volumes/Study Stash/Study Stash.app"    # after mounting the DMG
    powershell -File windows\verify-signing.ps1 Study-Stash-Setup.exe

and open them the way a student would (download from a browser, so they're quarantined). If it passed, a later
release can reuse exactly those installers (`reuse_run`), as before. Do the real rehearsal **before** the release
you want signed: the first notarization may be slow, and it's the likeliest thing to need a second try.

## What has been tested, and what hasn't

The pipeline was written before any certificate existed, so this is what two rehearsals by hand (`test_signing`) on
GitHub's runners showed, in October 2026:

- **Worked, on the Mac:** a throwaway self-signed identity in a temporary keychain; every file in the bundle signed
  with the hardened runtime and a real Apple timestamp (a minute for the lot); the bundle and the DMG signed;
  `codesign --verify --deep --strict` on the Apple silicon runner and again on the Intel one; the signed app's
  self-test on both (windows, microphone, Whisper with Metal); the checks and the clean-up. The runner then
  discards the identity.
- **Worked, on Windows:** the self-signed certificate, and signing the 74 and 69 unsigned exe and dll files of the
  two trees (the rest are Microsoft's, already signed).
- **Not proved yet, on Windows:** Inno Setup signing Setup.exe and the uninstaller, and everything after it (the
  signed installer installing, updating and uninstalling). The first rehearsal stopped there, because the signing
  script asked PowerShell to read a signature from Inno's temporary files, which it can't; that is fixed, and the
  scripts now leave a log of why a signature failed. The second rehearsal then stopped on a typo that the new
  parse step caught, also fixed. **Run the rehearsal again first (`gh workflow run ci --ref signing -f
  installers=true -f test_signing=true`); if the Windows job fails, its log says why.**
- **Can't be proved without your accounts:** notarization and stapling (the code is Apple's documented flow, with the
  log printed on a refusal), Azure Artifact Signing (the commands are Microsoft's own, from the page above), and
  `Valid` status on Windows.
- **Can't be proved with a self-signed identity:** that the app runs with `disable-library-validation` gone. A
  self-signed identity has no Team ID, so the rehearsal keeps the exemption (it says so). With a Developer ID the build
  drops it, and the self-test of the signed app is the proof: if it fails, the release isn't published, and the
  `STUDYSTASH_LIBRARY_VALIDATION` variable above puts it back.

## When it's live

Once a signed release is out and you've opened it on a Mac and a Windows PC the way a student would, change what
we tell people (these are the only places that say it):

- `README.md`, the **Mac** and **Windows** bullets under "Install".
- `site/index.html`, the two download paragraphs; `site/support/index.html`, the two "Worth trying first" items.
- `docs/DESIGN.md`, the line about the launcher being "signed ad hoc".

Replace the Open Anyway sentence with nothing (the DMG now opens with a single Open question) and keep the
Windows sentence, softened: "If Windows says 'Windows protected your PC', click More info, then Run anyway. It
names the publisher now, and goes away as more people install it." Don't claim signing before a release really is.

## If something fails

- **"no usable Developer ID Application certificate"**: the `.p12` has an "Apple Development" certificate, or the
  wrong password, or `APPLE_TEAM_ID` doesn't match it.
- **Notarization "Invalid"**: the log is printed in the job. It names the file Apple objected to (usually one
  that isn't signed, or isn't signed with the hardened runtime and a timestamp).
- **Notarization 401 or 403**: the API key's Issuer ID or Key ID is wrong, or it isn't a Team key, or its role is
  too small (use App Manager).
- **A 403 from Azure**: the endpoint doesn't match the account's region, or the app registration lacks the
  Certificate Profile Signer role.
- **The signed Mac app won't start**: see `STUDYSTASH_LIBRARY_VALIDATION` above.
- **To change or revoke**: revoke the certificate at developer.apple.com (Certificates, Identifiers & Profiles) or delete
  the Azure client secret, and replace the GitHub secret. A leaked `.p8` or client secret is revoked in the same places.
