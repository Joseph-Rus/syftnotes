# Study Stash's website

The public site: a home page with one download per platform, the phone app guide, the privacy policy, the terms and
a support page. It's plain HTML and CSS with one small script that nothing depends on, and it loads nothing from any
other site (the privacy policy says so, and the check enforces it).

```
site/
  index.html          home: what it does, one computer or two, the privacy promise, downloads
  phone/index.html    Get the phone app: Tailscale, Add a phone, the QR code, the home screen, troubleshooting
  privacy/index.html  privacy policy, including Google's Limited Use disclosure
  terms/index.html    terms of use (MIT, as is)
  support/index.html  support: GitHub issues, private security reports
  404.html            the page for a missing address
  assets/             site.css, site.js, the icon, favicon, social picture, screenshots (WebP) and the demo video
  check.mjs           the check CI runs before publishing (not published)
```

The colours are the app's: Lagoon, its default theme (`oklch(0.58 0.12 195)`, from
`engine/src/StudyStash.App/ColourThemes.cs`), on the warm paper of the icon, with the icon's navy for ink and the
quick panel's highlighter. Headings use the serif the app writes notes in (New York on a Mac, then Charter or
Georgia); there are no web fonts. Light and dark follow the system.

## Preview

```sh
python3 -m http.server 8000 --directory site     # then open http://localhost:8000
node site/check.mjs                              # the same check CI runs
```

Every link is relative, so the site works at `https://study-stash-app.web.app/`, on its own domain, and under a
folder such as `<user>.github.io/study-stash/` alike.
`check.mjs` fails if one starts with `/`. Pages are folders (`privacy/index.html`), so addresses are
`/privacy/`, `/phone/` and so on.

**The check** serves `site/` the way a static host does and fails when a page doesn't load or is missing `lang`, a
viewport, a `<title>`, a description or an `<h1>`; when an image has no `alt`; when any link, image, script or
stylesheet on the site doesn't resolve, or a `#fragment` names no id on its page; when a page loads anything from
another site; or when a download link isn't `Study-Stash.dmg` or `Study-Stash-Setup.exe` from
`releases/latest/download/` (and the home page must offer both). It doesn't fetch outside links, so it passes
before a release with those assets exists.

## Publishing

The site is hosted by **Firebase Hosting** in the `study-stash-app` Google Cloud project (the same project as the
Google Calendar sign-in), set up by `firebase.json` and `.firebaserc` at the repository's root: `site/` as it is,
without `check.mjs` and this README, with clean addresses (`/privacy/`). It's free at
`https://study-stash-app.web.app/` (and `study-stash-app.firebaseapp.com`).

By hand, from the repository's root, with the Firebase CLI signed in to an owner of the project:

```sh
node site/check.mjs && firebase deploy --only hosting
```

`.github/workflows/site.yml` runs the check on every pull request that changes `site/**`, and on a push to `main`
also publishes, when the repository has a `FIREBASE_SERVICE_ACCOUNT` secret (a service account key for the project
with the **Firebase Hosting Admin** role; `firebase init hosting:github` makes one and adds the secret). Without the
secret, it only checks.

## The domain

The site is built for its own domain, which isn't bought yet. When it is:

1. Firebase console → **Hosting → Add custom domain**, type it (the bare domain, and `www` redirecting to it).
2. At the domain's DNS, add the `TXT` and `A` records Firebase shows; it issues the HTTPS certificate itself (up to a
   day).
3. Make `og:image` in `index.html` absolute (`https://<domain>/assets/social.png`): link previews need a full
   address.

Nothing else changes: every link is relative.

## What Google and Microsoft need from this site

The calendar integration signs in to Google and Microsoft with OAuth. Both want a public homepage and privacy
policy on a domain the publisher controls.

| | Address (with the domain) |
|---|---|
| Homepage | `https://<domain>/` |
| Privacy policy | `https://<domain>/privacy/` |
| Terms of service | `https://<domain>/terms/` |
| Support | `https://<domain>/support/` |

### Google (OAuth consent screen, then verification)

In Google Cloud console, **Google Auth Platform → Branding**:

- **App name** Study Stash, the **logo** (the repository's `assets/icon.png`, resized to 120 × 120), a **support email**.
- **Application home page**, **privacy policy link** and **terms of service link**: the three addresses above.
- **Authorised domains**: the domain. It must be verified as yours in
  [Google Search Console](https://search.google.com/search-console): add it as a **Domain** property, and Search
  Console gives a `TXT` record (`google-site-verification=…`) to add at the domain's DNS. Use the same Google
  account as the Cloud project (or add that account as an owner in Search Console).
- **Data access**: only the read-only calendar scope(s) the calendar code actually asks for (e.g.
  `https://www.googleapis.com/auth/calendar.readonly`, or the narrower `calendar.calendarlist.readonly` and
  `calendar.events.readonly`), plus `openid` and `email`. Calendar scopes are *sensitive*, so verification also asks
  for a justification and a short **demo video** showing the consent screen and where the app uses the data.

What reviewers check on the site, and where it is:

- The homepage says what the app does and links to the privacy policy (the footer, and the privacy section).
- The homepage isn't just a sign-in page, and the app's name matches the consent screen: **Study Stash**.
- The privacy policy says what Google data is used and why, where it's stored, that it's not shared, how to revoke
  it, and includes the **Limited Use** statement in the exact words Google asks for (`privacy/#google`).

### Microsoft (verified publisher)

- In Microsoft Entra admin center, **App registrations → the app → Branding & properties**: **Home page URL**,
  **Terms of service URL** and **Privacy statement URL**, the addresses above. The home page's domain should be the
  publisher domain.
- **Publisher domain**: verify the domain (**Branding & properties → Publisher domain → Verify**). Entra gives a
  JSON file to publish at `https://<domain>/.well-known/microsoft-identity-association.json`. Put it at
  `site/.well-known/microsoft-identity-association.json`; Firebase Hosting publishes dot-folders (`firebase.json`
  ignores only the check and this README) and serves the file as it is. (Adding a DNS `TXT` record instead also works for a verified domain in
  the tenant.)
- **Verified publisher** then needs a Microsoft AI Cloud Partner Program (formerly MPN) account whose publisher
  domain matches, linked in **Branding & properties → Publisher verification**.

## Keeping it true

The privacy policy describes the code. When the app starts reading something new, contacting a new service, or
the calendar code changes its scopes or what it sends to the library, change `privacy/index.html` (and its "Last
updated" date) in the same pull request.
