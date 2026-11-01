# The App Store listing

Everything App Store Connect asks for the iPhone and iPad app, ready to paste. Character counts are in brackets
(Apple's limits after the slash). The listing is for a paid app with an upfront price: the app has no in-app
purchases and no StoreKit code, so nothing here mentions a price. RELEASE.md has the builds, TestFlight and signing.

## App Information

- **Name** (10/30): `Syft Notes`
- **Subtitle** (25/30): `Your lectures, written up`
- **Primary category**: Education. **Secondary category**: Productivity.
- **Content rights** ("Does your app contain, show, or access third-party content?"): No. It shows the student's own
  notes, and their own Canvas work through their own laptop.
- **Age rating**: answer **None** or **No** to every question (see below). It comes out **4+**.
- **Privacy Policy URL**: `https://syftnotes.com/privacy/`

## Version information

- **Promotional text** (168/170):

  ```
  Record a class on your phone and your laptop writes the notes. Read them anywhere, ask them questions, make flashcards and see what's due. Try the sample classes first.
  ```

- **Keywords** (98/100; the name and subtitle are searched already, so "notes" and "lectures" aren't repeated, and no
  other company's names):

  ```
  recorder,transcribe,class,study,flashcards,quiz,college,student,exam,homework,notebook,handwriting
  ```

- **Support URL**: `https://syftnotes.com/support/`
- **Marketing URL**: `https://syftnotes.com/`
- **Copyright**: `2026 Joseph Russell` (or the company's name, if the company sells it)
- **Description**:

  ```
  Syft Notes for iPhone and iPad is the phone half of Syft Notes, the free app that turns your lectures into study notes on your own laptop.

  To use it with your own lectures, you need the free Syft Notes app on your Mac, Windows or Linux computer, from syftnotes.com. The sample classes let you look around first, with no computer and no sign-in: on the first screen, tap Other ways, then Try it with sample classes.

  Record a class
  Tap Record and your phone records the lecture and writes down what's said as it goes, right on the phone. It keeps going with the screen locked, and you can type your own notes while it records. When you stop, your laptop writes the study notes around what you typed.

  Read your notes
  Every lecture's notes, with formulas, tables and the moments in the recording they came from. Search all of them at once. Add photos of the whiteboard to a lecture.

  Ask your notes
  Ask about one lecture, a class or everything, and the answer comes from your own notes, with links to the moments it used. Answers are written by the AI you chose in Syft Notes on your laptop: Claude, ChatGPT or Gemini with your own account, or a model that runs on the laptop itself. Before your first question, the app shows where it goes and asks you first.

  Study
  Make flashcards and a quiz from any lecture, or from a whole class.

  See what's due
  Your assignments from Canvas and your class times from your calendar, with reminders to record when a class starts and before work is due. Widgets show what's next.

  Write by hand
  Notebooks with ruled, grid or Cornell paper, filed with the class they're for. Made for iPad and Apple Pencil, and they work on iPhone too.

  How it connects
  Continue with Google on your phone and on your laptop, with the same account. The two share a private Syft Notes folder in your own Google Drive that only Syft Notes can see. There's no Syft Notes account and no Syft Notes server: your recordings and notes stay on your own devices and in your own Drive, and nothing is sent to us.

  Record only where you're allowed to. Many schools need everyone's permission first.
  ```

- **What's New in This Version**: App Store Connect doesn't ask for it on an app's first version. For the first
  update after it, start from:

  ```
  Ask now shows where your question goes before the first one, and asks you first. You can change this in Settings, under Your AI. Help and the privacy policy are in Settings, under About.
  ```

## Age rating

Every answer is **None** (for how often) or **No**:

| Section | Answer and why |
| --- | --- |
| In-app controls: parental controls, age assurance | No. |
| Unrestricted web access | No. Links (Help, the privacy policy, a link in a note, Open in Canvas) open in Safari, outside the app. |
| User-generated content | No. Nothing a student makes is shown to anyone else. |
| Social media | No. |
| Messaging and chat | No. Ask is a student asking about their own notes; no one talks to anyone. |
| Advertising | No. |
| Mature themes, medical or wellness, sexuality or nudity, violence | None. |
| Chance-based activities (gambling, contests, loot boxes) | None. |

Result: 4+. Don't use "for kids" anywhere: the privacy policy says Syft Notes isn't directed at children under 13.

## App Privacy (the "nutrition label")

**Data Not Collected.** Answer "No, we do not collect data from this app".

Worked out from the code under `ios/` (checked 2026-11-01):

- **No analytics, ads, crash reporting or tracking.** There's no SDK at all (no Swift packages), no Firebase or
  Firestore, and nothing under `ios/` sends the daily count: that's only the computer app's, and the privacy policy
  says so. `PrivacyInfo.xcprivacy` declares no tracking and no collected data.
- **What leaves the phone, and where it goes:**
  - over Google (the main way): the student's own Google Drive, its hidden Syft Notes folder (`drive.appdata`), with
    the student's own sign-in. What's there: a copy of what the phone shows, and the phone's requests (a recording's
    transcript and typed notes, a question, a renamed lecture, a photo or file). Only the student's laptop reads it.
    Google's sign-in asks for `openid email`; the email stays on the phone (Keychain) to show which account it is.
  - over Tailscale (Other ways): the student's own computer, at its private address.
  - Google's sign-in and token endpoints, and Apple's own download of the speech model.
- **None of it reaches us.** Apple counts data as collected when it's sent off the device so that the developer or
  its partners can get at it beyond serving the request. We have no server, and we can't read the student's Drive or
  computer. The AI that answers runs from the student's laptop under the student's own account with that company;
  it isn't a partner of ours.

If App Review ever disagrees and asks for something, the closest honest answer is **User Content → Other User
Content, used for App Functionality, not linked to identity, not used for tracking**. Don't change the label
without that request.

## Export compliance

`ITSAppUsesNonExemptEncryption` is `NO` in the build (`ios/project.yml`), so App Store Connect doesn't ask about each
build. The only encryption is the system's own (https through `URLSession`) and a SHA-256 hash for Google's sign-in
(PKCE), both exempt. If it's ever asked: the app uses encryption, it qualifies for the exemption, and it uses no
encryption algorithm of its own. No French declaration is needed.

## App Review Information

- **Sign-in required**: No (leave the user name and password empty).
- **Contact**: name, phone (+1…) and an email that's read.
- **Notes** (paste as is, about 2,000 of the 4,000 characters):

  ```
  No sign-in or computer is needed to review the app. On the first screen tap Other ways, then Try it with sample classes. Everything runs on made-up classes kept on the device: Home, a lecture's notes and transcript, Due, Ask, Study (flashcards and a quiz), Search, notebooks, and recording.

  What it is: the phone app for Syft Notes, a free desktop app (Mac, Windows, Linux) that records lectures and writes study notes on the student's own computer. With their own lectures the phone reads that computer's library. We have no accounts and no servers.

  Continue with Google: this isn't an account with us. It only lets the app reach its own hidden folder in the student's own Google Drive (the drive.appdata scope, plus their email to show which account), which their computer also uses. The app is a client of the student's own Google Drive and has no account of its own, so we believe guideline 4.8 doesn't call for another login. Settings, Sign out of Google, revokes it.

  AI (5.1.2(i)): before the first question in Ask, the first flashcards, or the first "Ask Syft Notes" from Siri, the app shows a sheet: the question goes to the student's own laptop, and the AI they chose there (Claude, ChatGPT or Gemini, under their own account) answers it. Nothing is sent until they tap Agree. Settings, Your AI, turns it off again, and a link opens our privacy policy. To see it in the sample classes, open Ask and send a question. There the answers are made on the device.

  Recording (2.5.14): it starts only when the student taps Record, and the microphone is asked for then. While it records, the app shows a red recording bar and a Live Activity, and keeps recording in the background (the audio background mode) so a lecture isn't cut off when the screen locks. Speech is written down on the device.

  NSAllowsArbitraryLoads: only for Other ways, Connect over Tailscale, where the app reaches the student's own computer at its private Tailscale address with plain http. Tailscale encrypts that traffic end to end. The main way, Google Drive, is all https.

  Privacy policy: https://syftnotes.com/privacy/ (also in the app, Settings, About).
  ```

## Screenshots

`tools/app-store-shots.sh -u <simulator udid> -o <folder> iphone|ipad` takes them from a headless simulator in the
sample classes (never the Mac's screen, never a real library): status bar at 9:41 with a full battery, light, and the
app's clock at 9:32 so Up next's Calculus II is at 9:41, "in 9 minutes". The set of 2026-11-01 is in
`~/Downloads/syft-notes-app-store/screenshots/` (not in the repo):

| Folder | Simulator | Size | App Store Connect slot |
| --- | --- | --- | --- |
| `iphone-6.9/` | iPhone 17 Pro Max | 1320 × 2868 | iPhone, Dynamic Island (large display) |
| `iphone-6.3/` | iPhone 17 Pro | 1206 × 2622 | iPhone, Dynamic Island (medium display): the iPhone size Apple requires |
| `ipad-13/` | iPad Pro 13-inch (M5) | 2064 × 2752 | iPad 13-inch: required, since the app runs on iPad |

iPhone, in this order: Home, recording with typed notes, a lecture's notes, Ask with an answer, flashcards, Due,
Search, a handwritten notebook. iPad: Home, a lecture's notes, a notebook with its pages, Ask, Due's month, flashcards.
The first three show in search results.
