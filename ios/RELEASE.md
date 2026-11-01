# Releasing the iPhone and iPad app

## Once

The app's name in App Store Connect (App Information → Name) is set by hand there, not by this repo; it must be "Syft Notes" (the home-screen name comes from `project.yml`).

1. **Xcode → Settings → Accounts**: sign in with the Apple ID on the developer team in `project.yml`
   (`DEVELOPMENT_TEAM`, joey russell G67978NLXW), as Account Holder or Admin, so Xcode can make the distribution
   certificate and profiles itself. Done on Joey's Mac by 2026-10-23: `app.studystash.ios` and
   `app.studystash.ios.widgets` are registered on that team, with App Store profiles.
2. **App Store Connect → Apps → +, New App** (Apple's API can't make this; only the website or Xcode's Distribute
   window can): iOS, name *Syft Notes* (it was made as Nerd Notes, then Pylorum; rename it under App Information → Name), primary language English (U.S.), bundle ID `app.studystash.ios`, SKU
   `nerdnotes-ios` (a SKU can never change, and nobody sees it), Full Access. Until it exists an upload stops at "Error Downloading App Information" (the
   distribution log says `missingApp`).
3. **TestFlight → Internal Testing → +**: a group (say *Team*) with **automatic distribution** on, and yourself in it.
   Every build uploaded after that shows in the TestFlight app on your phone by itself.
4. **App Privacy**: the privacy policy URL is `https://syftnotes.com/privacy/` (the site also still answers at `https://study-stash-app.web.app/privacy/`). Data: "Data Not Collected" (the App Store listing, kept outside this public repository by the maintainer, says why).
   The app sends what it records and asks only to the student's own library, on their own computer, never to us;
   `PrivacyInfo.xcprivacy` says the same. The AI that writes notes and answers questions runs from that library, under
   the student's own account (or on the computer with Ollama), and the privacy policy says so.
5. **TestFlight → Test Information** (needed before external testers, not for internal ones):
   - **Beta App Description**: kept outside this public repository by the maintainer.
   - **Feedback Email**: an address you read.
   - **Privacy Policy URL**: as above. **Marketing URL**: `https://syftnotes.com/`.
   - **Beta App Review Information**: contact name, phone (international format, +1…) and email; **Sign-in
     required: off**; and the App Review notes, kept outside this public repository by the maintainer.

## Each build

```sh
ios/release.sh
```

It archives, signs and uploads, numbering the build itself. Then in App Store Connect → TestFlight:

- **Internal testing** (no review): add yourself and up to 100 people on the App Store Connect team; the build
  installs from the TestFlight app as soon as it has processed (5–15 minutes).
- **External testing** (up to 10,000 people): make a group, add the build, and turn on the **public link** if you
  want one. The first build of each version goes to Beta App Review (usually about a day); later builds of the same
  version often don't. A build can be tested for 90 days.

## External testing: what to paste

App Store Connect → the app → **TestFlight** → **Test Information** (once), then a group under **External Testing**
with the build, then **Submit for Review**.

The Beta App Description, the App Store listing and the notes for App Review are kept outside this public repository by the maintainer.

**Before the first external build:** Google's sign-in must be out of Testing (Google Auth Platform → Audience →
**Publish app**). In Testing only listed test users can press Allow; everyone else sees "Access blocked".

## The App Store

The App Store listing and the notes for App Review are kept outside this public repository by the maintainer. Submit
a TestFlight build for review with those.

- **Screenshots**: `tools/app-store-shots.sh` (the screenshot slots are in the listing, kept outside this public repository), from headless simulators, never the Mac's
  screen. Apple wants at least one iPhone size with a Dynamic Island (medium display: iPhone 17 Pro, 1206 × 2622) and
  the iPad 13-inch (2064 × 2752); the iPhone 17 Pro Max's 1320 × 2868 fills the large slot.
