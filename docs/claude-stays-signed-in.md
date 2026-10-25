# Keeping Claude signed in for Study Stash

Study Stash writes notes and answers Ask by running the student's own Claude Code (`claude -p`), signed in with
their own Claude plan. When that sign-in stops working, notes and Ask stop until the student signs in again. The
question was whether `claude setup-token` could keep Claude signed in for Study Stash "no matter what".

**Short answer.** On the technical side it would work. Anthropic's rules forbid it, though. `setup-token` prints a
one-year token, and Claude Code runs on it through `CLAUDE_CODE_OAUTH_TOKEN`. But Anthropic's legal page says an app
like Study Stash "may not collect, store, or intermediate Claude.ai credentials or session tokens". A button that
runs `setup-token` and keeps the token does exactly that. So it isn't built. What was built instead is smaller: a
false "signed out" that Study Stash could cause itself is now tried again instead (see
[What changed](#what-changed-on-this-branch)). The other ways to keep Claude working are listed below.

Checked on 2026-10-24 against Anthropic's docs and Claude Code 2.1.310's own `--help`. Nothing here ran
`setup-token`, `login` or `logout`, or read a credential.

## What Study Stash does today

- The student signs in through Claude Code's own `claude auth login`, on Anthropic's own sign-in page. Claude Code
  keeps the sign-in itself: in the Keychain on a Mac, in `.credentials.json` elsewhere
  ([Credential management](https://code.claude.com/docs/en/authentication#credential-management)). Study Stash
  never sees it.
- When a run fails because Claude needs signing in again (`Engines.LooksSignedOut`):
  - the lecture says so in plain words;
  - its notification and Settings → Your AI offer **Sign in**, then **Check**;
  - once Claude answers again, the library writes the notes it missed by itself. That's the log line "Claude is
    signed in again: writing the notes of 1 lecture(s) it couldn't before".
- With Fallback on, Ollama answers while Claude is known to be signed out.

## What `claude setup-token` is

| Question | What Anthropic's docs say | Source |
|---|---|---|
| What it does | "opens the same browser authorization flow as `/login`, and the token prints to the terminal after you approve access in the browser. It does not save the token anywhere." `--help`: "Set up a long-lived authentication token (requires Claude subscription)", and it has no options. | [Generate a long-lived token](https://code.claude.com/docs/en/authentication#generate-a-long-lived-token), [CLI reference](https://code.claude.com/docs/en/cli-reference) |
| How long it lasts | "a one-year OAuth token" | same |
| Which plans | "requires a Pro, Max, Team, or Enterprise plan" | same |
| What it can do | "It can only make model requests, so it can't establish Remote Control sessions or fetch claude.ai connectors. MCP servers you configure locally still work." That's enough for Study Stash: plain answers turn claude.ai's connectors off anyway, and the Canvas tools are a local MCP server. | same |
| How it's used | Put it in the `CLAUDE_CODE_OAUTH_TOKEN` environment variable. It ranks 5th of 7: above the saved `/login` (7th), but below `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY` and `apiKeyHelper`. `--bare` never reads it. Study Stash uses `--restricted`, which the docs don't say ignores it. | [Authentication precedence](https://code.claude.com/docs/en/authentication#authentication-precedence), [env vars](https://code.claude.com/docs/en/env-vars) |
| Revoking it | The docs name "revoked" as a state ("you signed out everywhere or an admin removed access"), but don't say where one token is revoked. Users report that claude.ai's settings list each token with a Revoke button, the setup-token one showing only `user:inference`. There's no command to revoke one: [#57400](https://github.com/anthropics/claude-code/issues/57400) asked for one and was closed for inactivity. | [Errors](https://code.claude.com/docs/en/errors#oauth-token-revoked-or-expired), [#103209](https://github.com/anthropics/claude-code/issues/103209) |
| When it expires or is revoked | In `-p`: `Failed to authenticate: OAuth token revoked. Please log in again or contact your administrator.` or `Failed to authenticate. API Error: 401 OAuth token has expired ...` (error code `authentication_failed`). Claude Code goes on sending the variable's token after a 401 and never falls back to the saved login: you "generate a fresh token … and restart with it". | [Errors](https://code.claude.com/docs/en/errors#oauth-token-revoked-or-expired) |
| Does it sign the student out of their own Claude Code? | No. It "does not save the token anywhere", so the student's own `/login` stays as it is. The variable only affects the processes that are given it. | [Generate a long-lived token](https://code.claude.com/docs/en/authentication#generate-a-long-lived-token) |
| Reading its output without a terminal | Not documented: the docs only say it "prints to the terminal", and it has no flag for a file or JSON. Tools that do automate it record it through a pseudo-terminal (`script(1)`) and then have to join "a wrapped, styled token" back together. Not tried here, since running it was off limits. | [n2-agents #95](https://github.com/noisyneighborstudio/n2-agents/pull/95) |
| How long an ordinary `/login` lasts | It has an end. "When the login you created with `/login` is within three days of expiring, Claude Code shows a warning at startup". Once it can't be renewed, `-p` says `Failed to authenticate: OAuth session expired and could not be refreshed`. `claude auth status` has no expiry field. | [Renew an expiring login](https://code.claude.com/docs/en/authentication#renew-an-expiring-login), [Login expired](https://code.claude.com/docs/en/errors#login-expired) |

## Why it isn't built

[Legal and compliance → Authentication and credential use](https://code.claude.com/docs/en/legal-and-compliance#authentication-and-credential-use):

> Anthropic does not permit third-party developers to offer Claude.ai login into their own applications, or to route
> requests through Free, Pro, or Max plan credentials on behalf of their users. Moreover, developers may not collect,
> store, or intermediate Claude.ai credentials or session tokens — sign-in to a Claude account must complete through
> Anthropic's own flow.

> Anthropic reserves the right to take measures to enforce these restrictions and may do so without prior notice.

The button in the brief would capture a Claude.ai token from `setup-token`'s output and keep it. The sign-in would
still happen on Anthropic's own page, but keeping the token is the part this rule names. Keeping it in the Keychain
or an owner-only file doesn't change that. If Anthropic enforces the rule, it's the student's own Claude account that
pays.

What Study Stash does today fits inside what the same page allows: "Nor does it prevent an end user from signing in
to the unmodified Claude Code binary with their own Claude subscription". The student signs in with `claude auth
login`, Claude Code keeps the credential, and Study Stash never handles it.

Two more lines point the same way:

- The Agent SDK page: "Unless previously approved, Anthropic does not allow third party developers to offer
  claude.ai login or rate limits for their products"
  ([Agent SDK overview](https://code.claude.com/docs/en/agent-sdk/overview)).
- The Consumer Terms: "You may not share your Account login information, Anthropic API key, or Account credentials
  with anyone else" ([Consumer Terms](https://www.anthropic.com/legal/consumer-terms), section 2).

Anthropic's own error pages do recommend `setup-token`, for "automation that can't sign in interactively"
([Errors](https://code.claude.com/docs/en/errors#oauth-token-revoked-or-expired)). That means a person's own scripts,
with the person keeping their own token. It's not an app collecting one.

## What else keeps Claude working

1. **Ask Anthropic.** The legal page ends: "For questions about permitted authentication methods for your use case,
   please contact sales". The question to ask: may a desktop app that runs the student's own unmodified Claude Code,
   on the student's own computer, run `setup-token` for them and keep the token on that computer, giving it only to
   that Claude Code? If they say yes in writing, the design at the end of this page is ready to build.
2. **The student's own API key (already in Study Stash).** Settings → Your AI takes an Anthropic Console key and
   keeps it in `ai_keys.json`, owner-only. The same page allows this: "This does not restrict how customers provision
   and manage their own API keys". With a key, notes and Ask go to the API and never depend on the sign-in. It's
   billed per use by the Console, apart from the Pro or Max plan. Agent work with tools still runs through Claude
   Code.
3. **Fewer false or avoidable sign-outs**, from the causes Anthropic's docs name:
   - *Another Claude Code renewing the sign-in.* Claude Code's renewal is shared by every Claude Code on the
     computer. When one holds it, or stopped halfway and left it locked, the others say `Failed to refresh OAuth
     token: another Claude Code process is refreshing it or exited mid-refresh. This is usually transient; retry in a
     minute…` ([Errors](https://code.claude.com/docs/en/errors#could-not-refresh-your-login-because-another-claude-code-process-is-refreshing-it)).
     Study Stash runs several at once (notes, diagrams, Ask, beside the student's own). It also stops one halfway when
     a question is cancelled or a run times out. Until this branch, those words read as signed out. The lecture said
     "Claude needs you to sign in again", and the notes were written once the next job worked, with no sign-in. That
     matches Joey's log line. **Fixed on this branch.**
   - *An old Claude Code.* "Before v2.1.211, waking the machine from sleep could cause two sessions to renew with the
     same token, which revoked the saved login"
     ([Troubleshooting](https://code.claude.com/docs/en/troubleshoot-install#not-logged-in-or-token-expired)). The
     library's LaunchAgent puts `/usr/local/bin` and `/opt/homebrew/bin` before `~/.local/bin` (`Autostart.cs`). So a
     student with an old npm or Homebrew copy beside the native install gets the old one in the library, and
     Homebrew's copy doesn't update itself. Not changed here. A small follow-up could prefer the newest copy found, or
     say in Settings when the one in use is older than 2.1.211. Joey's Mac has only the native 2.1.310 (and Claude
     Desktop's 2.1.308), so this isn't his cause.
   - *A locked Keychain.* "On macOS this can happen when the login keychain locks, for example on sleep or idle"
     ([Couldn't save your login](https://code.claude.com/docs/en/errors#couldnt-save-your-login)). A renewal that
     can't be saved loses the sign-in. `claude doctor` reports `macOS Keychain is not writable` when this is the cause.
   - *The clock.* "check that your system clock is accurate, as token validation depends on correct timestamps"
     (same page).
4. **What's already there stays.** A `/login` has an end anyway, so a student will still sign in now and then. The
   plain words, **Sign in**, the notes written afterwards and the Ollama stand-in cover that. The newer wording of
   these errors (Claude Code 2.1.287 on) is covered too; tests now check it.

## What changed on this branch

- `ClaudeProvider.RunAsync` (`engine/src/StudyStash.Core/Ai/Providers.cs`): a run that stops before saying anything
  because another Claude Code is renewing the sign-in is tried again after 30 seconds, three tries in all, which is
  about the minute the docs give. If it says so every time, its words stand and read as signed out, as before. The
  docs say that's when to sign in again.
- `Engines.LooksLikeRefreshBusy` (`engine/src/StudyStash.Core/Ai/Engines.cs`) reads those words.
- `engine/tests/StudyStash.Core.Tests/SignInAgainTests.cs`:
  - busy then working is tried again and answers;
  - busy every time stops after three tries;
  - really signed out is said at once;
  - the 2.1.287 wording of revoked, expired and can't-be-renewed counts as signed out.

## If Anthropic says yes: the design

- **Settings → Your AI, Claude's row:** "Keep Claude signed in for Study Stash". It runs `claude setup-token` in a
  pseudo-terminal (a Mac: `forkpty` or `script`; Windows: ConPTY), and `setup-token` opens the browser itself. Study
  Stash reads the token from the output: colour codes stripped, wrapped lines joined, never logged.
- **Where it's kept:** the Keychain on a Mac. On Windows, an owner-only file beside `ai_keys.json` (`ApiKeys`), or
  DPAPI.
- **Who gets it:** every `claude` Study Stash starts gets `CLAUDE_CODE_OAUTH_TOKEN` (the `env` that `Spawn` takes).
  The student's own terminal never does.
- **What could override it:** `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY` or `apiKeyHelper` in the app's own
  environment outrank the token. Never use `--bare` with it. `claude auth status` then says `authMethod:
  oauth_token`.
- **When it stops working:** a revoked or expired token gives `authentication_failed` while it's set. Study Stash
  says "Study Stash's Claude sign-in stopped working" with **Get a new one**, and runs once more without the token,
  so the student's own sign-in still writes the notes. The handling above stays as the fallback.
- **Stop keeping it:** Study Stash forgets the token, and says where on claude.ai it can be revoked, since there's no
  command or API for that.
- **What Joey would try by hand:** the one real sign-in in a browser, on a Pro or Max account; then a lecture's notes
  written with the student's own sign-in signed out.
