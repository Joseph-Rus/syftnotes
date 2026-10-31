# Security

## Reporting a problem

Please report security problems privately: on GitHub, open this repository's **Security** tab and
choose **Report a vulnerability**. Please don't open a public issue for them. You'll get a reply
within a week.

## What Syft Notes protects, and how

- **The library** is protected by one password, sent over your own network or Tailscale. It's
  plain http, so keep it on Tailscale or a network you trust; don't expose its port to the
  internet. Wrong passwords are counted by the address they come from: after ten different ones
  in ten minutes, nothing from that address is looked at until the oldest is ten minutes old, at
  every door the password opens. Each browser sign-in has its own secret (only a hash is kept):
  Log out ends it on the library, and a new password ends them all.
- **Claude's own door** (for claude.ai, over Tailscale Funnel) is a separate OAuth 2.1
  authorization server: authorization code with PKCE, short-lived access tokens with rotating
  refresh tokens, and dynamic client registration. Only token hashes are kept, and wrong passwords
  on its sign-in page are counted and refused the same way, by address. A token made by hand in Settings, for another MCP client, can
  be revoked on its own.
- **Secrets stay on your own computer.** The library's config and its Claude access file are
  written readable by you only.
- **Updates** come only from this repository's GitHub releases, which CI builds from `main` after
  the tests pass. Anyone who controls the repository controls the updates, so the maintainer's
  GitHub account uses two-factor authentication. To turn off automatic updates, set
  `auto_update = false` in the config.

## Supported versions

Only the newest release gets fixes. `studystash update` installs it.
