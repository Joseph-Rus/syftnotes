# Study Stash and AI apps (MCP)

Study Stash's library speaks MCP, so AI apps can search and read your lectures, notes and Canvas work. Every tool
only reads. There are two ways in, and which you need depends on where the AI app runs:

| The AI app | Where it runs | How it reaches your library | Needs Tailscale? |
|---|---|---|---|
| Claude Desktop, Claude Code, ChatGPT (the desktop app, and Codex in the terminal or your editor), Gemini CLI | On your computer | It starts Study Stash's MCP server itself (stdio) | No |
| claude.ai in a browser, the Claude phone app | Anthropic's servers | A custom connector at an internet address | Yes (Funnel) |
| chatgpt.com in a browser (a custom MCP server, added as a plugin) | OpenAI's servers | The same internet address | Yes (Funnel). Not yet tried with a real ChatGPT account |

## On this computer: no Tailscale

This is all a computer on its own needs ("Use just this computer", or a library and a laptop in one).

1. Open Study Stash → Settings → **AI tool access**. **AI apps on this computer** is the first thing on the page,
   with a row for each AI app Study Stash found here. The ones it didn't find are named in one line underneath; if it
   found none, the page offers **Get ChatGPT** and **Get Claude** instead.
2. Choose **Connect** next to the app. Study Stash adds itself to that app's own settings file. There's nothing to
   type or paste. (**Show what changed** shows what it wrote, and where.)
3. The row now says the one step left, since an app only reads its settings when it starts:
   - **On a Mac**, choose **Reopen Claude** or **Reopen ChatGPT**. Study Stash quits the app the way its own Quit menu
     does and opens it again. (Closing an app's window on a Mac doesn't quit it, which is why this is a button.) If
     the app won't quit, because it's asking about unsaved work, say, nothing is forced and the page says so.
   - **On Windows**, quit the app completely and open it again.
   - **ChatGPT** starts Study Stash when a chat in Codex begins, so its row adds: then start a chat in Codex.
   - Claude Code, Codex in a terminal and Gemini CLI: start a new session.
4. The row turns to **Connected** by itself: Settings keeps looking for five minutes after Connect or Reopen. The
   page then gives a first question to ask ("What did my last lecture cover?"), with **Copy**.

**Disconnect** takes Study Stash out again. If the app has another copy of Study Stash set up (an old download, say),
the row says so and offers **Fix**. If you pasted Study Stash into the app's settings by hand, it works, but the row
can't tell when it's connected: it says so and offers **Update**, which writes it the way Connect does.

### ChatGPT

The **ChatGPT** row is for the ChatGPT desktop app that has Codex built in (the one OpenAI released in July 2026, for
Mac and Windows), and for Codex in the terminal or your editor. They share one settings file, Codex's, so one
**Connect** covers all of them. After ChatGPT is reopened, Study Stash is under ChatGPT's own Settings → MCP servers,
and ChatGPT can use it where it works on this computer: its **Codex** mode. Ask it something like "search my Study
Stash lectures for recursion". ChatGPT's plain Chat mode runs on OpenAI's servers, which can't start an app on your
computer: that's what the internet address below is for.

**ChatGPT Classic**, the app from before, has no MCP servers on this computer, so the row doesn't count it. Get the
new ChatGPT app, or use the internet address below.

### What it writes, and where

Each app gets one entry, `study-stash`, that starts the Study Stash app as an MCP server over stdio:
`StudyStash mcp --client <app>` (with `--home <folder>` if your settings aren't in the usual place). No password,
token or address goes into the app's settings: the server reads your library through this computer with the
password Study Stash already keeps in its own settings (`~/.study-stash`), and `--client` only lets Settings show
which app started it.

| App | File | Where Study Stash goes |
|---|---|---|
| Claude Desktop | Mac: `~/Library/Application Support/Claude/claude_desktop_config.json`. Windows: `%APPDATA%\Claude\claude_desktop_config.json`, or, for the Microsoft Store build, `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude\claude_desktop_config.json` | `mcpServers.study-stash` |
| Claude Code | `~/.claude.json` (or `$CLAUDE_CONFIG_DIR/.claude.json`), the servers for every project, as `claude mcp add --scope user` keeps them | `mcpServers.study-stash` |
| ChatGPT | `~/.codex/config.toml` (or `$CODEX_HOME/config.toml`). The ChatGPT app, the Codex CLI and its IDE extension all read it | a `[mcp_servers.study-stash]` table |
| Gemini CLI | `~/.gemini/settings.json` | `mcpServers.study-stash` |

On Windows, `~` is your user folder (`C:\Users\<you>`).

How each change is made:

- **A merge, never a replace.** Everything else in the file (the app's other MCP servers, its settings) stays. In
  `config.toml` every other line stays exactly as it was, comments included.
- **The file as it was is kept** next to it, as `<file>.study-stash-backup`, before each change.
- **A file Study Stash can't rewrite safely is left alone**: JSON that isn't valid, JSON with comments (a rewrite
  would drop them), TOML that isn't valid, or Study Stash set up by hand in a form it won't rewrite. Settings says
  why and shows the setup to add by hand.
- A settings file that's a link (a dotfiles setup) is followed: the file it points to changes, and the link stays.
- **Copy setup** (for any other MCP app) copies the same `mcpServers` JSON, to paste into that app's settings.

Doing it by hand: Claude Code's own command is
`claude mcp add --scope user study-stash -- "/Applications/Study Stash.app/Contents/MacOS/StudyStash" mcp`
(on Windows, the path to `StudyStash.exe`). The others take the JSON from **Copy setup**, or for Codex:

```toml
[mcp_servers.study-stash]
command = "/Applications/Study Stash.app/Contents/MacOS/StudyStash"
args = ["mcp"]
```

### Over HTTP on this computer

The library also answers MCP over Streamable HTTP at `http://127.0.0.1:<library port + 1>/mcp` (8788 with the usual
port), on this computer only: it never listens on the network. It always asks for a sign-in. An app that supports
MCP sign-in (OAuth) finds it by itself, and you allow it with your library password; the sign-in page warns you when
it returns to a program on this computer. Most apps don't need this: the stdio setup above is simpler.

### What the AI apps on this computer can't do

ChatGPT Classic (the desktop app from before July 2026) and chatgpt.com have no MCP servers on this computer: their
plugins run on OpenAI's servers, like claude.ai. Use the new ChatGPT app, or the internet address below. The Gemini
app on the web has no custom MCP servers.

## On the web: claude.ai, the Claude phone app and chatgpt.com

claude.ai, the Claude phone app and chatgpt.com reach your library from Anthropic's and OpenAI's servers, so the
library needs an address on the internet. Study Stash makes one with Tailscale Funnel. A computer with no Tailscale doesn't have one, so the card
says what it needs (Tailscale, free, installed and signed in) and links to it, instead of showing an address that
goes nowhere. Nothing goes on the internet until you turn the switch on, and turning it on needs a library password.

Study Stash doesn't set up other tunnels. Funnel is the one it can turn on and off for you, check from the internet,
and keep to the one port alone.

## For students

### What the switch does

The **Claude and ChatGPT on the web and phone** card is in Study Stash → Settings → AI tool access. Its switch is
**Let Claude and ChatGPT reach your library from the internet**. When you turn it on:

- Study Stash asks Tailscale to turn on **Funnel** for the library's Claude port. That gives it one HTTPS address on
  the internet, `https://<your computer>.<your tailnet>.ts.net`. Nothing else on the computer is exposed: the
  library's own pages stay on your tailnet.
- The card shows the address to paste, ending in `/mcp`, with a **Copy** button.
- The library checks the address from the internet the way Claude or ChatGPT would, and the status dot says whether
  they can reach it.

It's off until you turn it on. Turning it off stops Funnel, and nothing can reach the library from the internet again
until you turn it back on. Only someone who knows your library password can let a Claude or ChatGPT account in.

### The three steps

1. In Claude, open **Settings → Connectors** and choose **Add custom connector**.
2. Paste the name (`Study Stash`) and the URL from the card (`https://mini.tail1234.ts.net/mcp`, say), then choose
   **Continue**. Leave the advanced settings as they are.
3. A Study Stash page opens: "Claude wants to read your lectures". It says where it will send you back to
   (claude.ai). Type your library password and choose **Allow**.

Claude lists Study Stash's tools, and you can use them in any chat. You can also ask for them by name: "search my
Study Stash lectures for recursion".

### The same, in ChatGPT

1. In a browser, open [chatgpt.com/plugins](https://chatgpt.com/plugins), choose **+**, then **Add custom MCP
   server**. If it isn't there, turn on **Developer mode** in ChatGPT's settings first (it needs a paid ChatGPT plan,
   and on a school or work account an admin has to allow custom MCP servers).
2. Give it the name (`Study Stash`) and the URL from the card, ending in `/mcp`, then choose **Create as a plugin**.
3. The same Study Stash page opens: "ChatGPT wants to read your lectures", identified by chatgpt.com. Type your
   library password and choose **Allow**.

In a chat, type `@` and pick Study Stash. ChatGPT's own menus change often: if these names have moved, OpenAI's
[Connect from ChatGPT](https://developers.openai.com/apps-sdk/deploy/connect-chatgpt) page has today's.

### What Claude can read

What it can read follows the toggles under **What they can read** on the same page:

- **Lectures and transcripts**: the list of lectures, each lecture's transcript, and passages that match a search.
- **Study notes**: the notes written from each transcript.
- **Canvas assignments and files**: what the Chrome extension mirrored (assignments with their instructions,
  rubrics, your submissions and feedback, modules, files, pages and announcements) and the folders you let Study
  Stash read.

Every tool only reads. None of them can change, move or delete anything, and each one is marked read-only for Claude.
The one tool that saves Canvas files into the library, `canvas_download`, isn't offered on the internet at all.
With **AI tool access** off, Claude stays connected, but every tool answers "AI tool access is off in Study Stash"
until you turn it back on. You don't need to reconnect.

### Disconnecting

Any of these ends the connection:

- **In Claude:** Settings → Connectors → Study Stash → Disconnect (or Remove). Claude tells the library to forget its
  sign-in.
- **In Study Stash:** on the Claude and ChatGPT on the web and phone card, choose **Remove** next to Claude (or ChatGPT). Its token stops working at once, and Claude asks you to
  sign in again next time.
- **Everything at once:** turn the Claude switch off. The address stops answering on the internet.

A sign-in also ends on its own after 30 days without use.

### Troubleshooting

| What you see | What to do |
|---|---|
| "Your tailnet doesn't allow Funnel yet", with a Tailscale link | Funnel has to be allowed once for this computer by whoever runs your tailnet (you, usually). Choose **Copy link**, open it in a browser, allow Funnel, then turn the switch on again. |
| "Your tailnet doesn't have HTTPS certificates turned on yet" | Funnel needs HTTPS certificates. Open the card's link (it goes to the Tailscale admin console), turn them on, then turn the switch on again. |
| "Tailscale hasn't given this computer a name yet" | Turn on **MagicDNS** in the Tailscale admin console (DNS page). The address comes from it. |
| "The internet can't find … yet" right after turning it on | A new Funnel name takes a minute or two to reach public DNS. Wait, then choose **Check again**. If Claude already tried and failed, wait about 5 minutes before choosing Connect in Claude again, because Claude remembers what it found for a few minutes. |
| "… answers, but not with Study Stash" | Something else is using port 443 on this computer through Tailscale. Turn the Claude switch off (this also clears a Tailscale Serve setup on port 443), then on again. |
| Claude says it couldn't connect, but the card says it's reachable | Check that the URL in Claude ends in `/mcp` and starts with `https://`. Remove the connector in Claude and add it again. |
| "That isn't your library password" | This is the library password from Settings → Library, not your Tailscale or Claude password. After 8 wrong tries, the sign-in waits 15 minutes. |
| "Your library has no password yet" | Set one in Study Stash → Settings → Library. The switch can't be turned on without it. |

Things you don't need to change:

- **Ports.** Funnel only serves ports 443, 8443 and 10000. Study Stash uses 443, so the address has no port number.
- **Firewall and router.** Funnel connects out from your computer to Tailscale, and Tailscale brings the requests
  in. You don't open a port, and there's no router setup. Claude's requests come from Anthropic's servers
  (160.79.104.0/21), not from your phone or laptop. You only need to know that if you run your own proxy in front
  of the library.

## Conformance checklist

These are the requirements for Claude's custom connector and where Study Stash meets each one.

**Sources:**
- **MCP**: the Model Context Protocol specification, revision 2026-07-28 (sections: authorization,
  client registration, security considerations, Streamable HTTP transport).
- **Claude**: Anthropic's "Authentication for connectors" and "Troubleshoot connectors" pages.

All the tests are in `engine/tests/StudyStash.Core.Tests`. `ConnectorTests.` is shortened to `C.`, and
`ClaudeTests.` to `CT.`

| Requirement | Source | How Study Stash meets it | Proved by |
|---|---|---|---|
| Protected resource metadata (RFC 9728), and a 401 that points to it and names the scope | MCP authorization §2.3; Claude "Authentication" | `POST /mcp` without a valid token gets `401` with `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource/mcp", scope="library:read"`. A bad token also gets `error="invalid_token"` and `error_description`. | `C.Discovery_advertises_every_field_on_the_ts_net_https_address`, `C.Claude_adds_Study_Stash_as_a_custom_connector` (step 1) |
| Metadata at the path-suffixed and the root well-known address | MCP authorization §2.3.2 | Both `/.well-known/oauth-protected-resource/mcp` and `/.well-known/oauth-protected-resource` answer. `resource` is the pasted URL. | `C.Discovery_…`, `C.Claude_adds_…` (step 2) |
| Authorization server metadata (RFC 8414), with the issuer equal to the address it was read from | MCP authorization §2.3.3 | `/.well-known/oauth-authorization-server` (plus `/mcp` and `openid-configuration`) gives `issuer`, `authorization_endpoint`, `token_endpoint`, `registration_endpoint`, `revocation_endpoint`, `response_types_supported: [code]`, `grant_types_supported: [authorization_code, refresh_token]`, `code_challenge_methods_supported: [S256]`, `token_endpoint_auth_methods_supported: [none]`, `scopes_supported`. | `C.Discovery_…`, `C.Claude_adds_…` (step 3) |
| `iss` in the authorization response (RFC 9207), and advertised | MCP authorization (2026-07-28); RFC 9207 | Every redirect back, including errors, carries `iss`. The metadata says `authorization_response_iss_parameter_supported: true`. | `C.Every_answer_back_names_the_server`, `C.Claude_adds_…` (step 7) |
| Client ID Metadata Documents (CIMD) | MCP client registration §CIMD; Claude "Authentication" (Claude uses CIMD when `client_id_metadata_document_supported: true` and `none` is supported) | Both values are advertised. An https `client_id` with a path is fetched as the client's document. The document must name itself as `client_id`, list its redirect URIs, and be able to sign in as a public client: it says `none`, says nothing, or (ChatGPT's document, which prefers `private_key_jwt`) lists `none` in `token_endpoint_auth_methods_supported`. A client that signs who it is anyway is read by its assertion's `sub`; the code and its PKCE verifier are the proof either way. SSRF guards: https on port 443 only, no user info; the name must resolve only to public addresses; the connection is pinned to the checked address; no redirects, no proxy; 16 KB at most; 5 s timeout; kept for between 5 minutes and 24 hours per `Cache-Control`. | `C.Claude_signs_in_with_its_published_identity`, `C.ChatGPT_signs_in_with_its_published_identity`, `C.A_document_that_doesnt_match_gets_a_problem_page_and_no_redirect`, `C.The_fetch_refuses_private_addresses_redirects_and_big_documents_and_keeps_what_it_read`, `C.Only_public_addresses_are_global` |
| Dynamic Client Registration (RFC 7591) as the fallback, and `401 invalid_client` for a client the library doesn't know | MCP client registration §DCR; Claude "Troubleshoot" (Claude registers again only after `invalid_client`) | `/register` takes what clients send and answers as a public client (`none`, no secret), with at most 10 redirect URIs of up to 2000 characters. `/token` with an unknown `client_id` answers `401 invalid_client`. | `C.Registration_takes_what_clients_send_and_answers_as_a_public_client`, `C.An_unknown_client_gets_invalid_client_so_claude_registers_again`, `C.Claude_adds_…` (DCR run), `CT.An_mcp_client_finds_the_sign_in_by_itself` |
| Claude's callback `https://claude.ai/api/mcp/auth_callback`, exactly; loopback callbacks on any port | Claude "Authentication"; RFC 8252 §7.3; MCP security considerations | Redirects match exactly. A registered `http://localhost/…` or `http://127.0.0.1/…` also matches the same host and path on any port. Look-alikes fail with no redirect, and the sign-in page warns before sending you back to a program on this computer. | `C.Redirects_match_exactly_or_on_loopback_on_any_port`, `C.Claude_codes_loopback_callback_passes_on_any_port_and_look_alikes_fail`, `C.Sign_in_page_warns_before_a_loopback_redirect`, `C.Claude_adds_…` (Claude Code run) |
| PKCE with S256, required | MCP authorization §2.6 (OAuth 2.1) | `/authorize` without an S256 `code_challenge` (a missing one, `plain`, or one that's too short) is refused back to the client with `invalid_request`. `/token` checks the verifier. | `C.Every_answer_back_names_the_server`, `C.An_unknown_client_gets_invalid_client_so_claude_registers_again`, `C.Claude_adds_…` (step 8) |
| Resource indicators (RFC 8707) and token audience | MCP authorization §2.5 | `resource` is checked at `/authorize` and `/token`, and on refresh (`invalid_target` otherwise). A token is bound to that audience, and `/mcp` accepts it only at that address. Tokens made in Settings read at any of the library's addresses. | `C.A_token_is_for_one_address`, `C.A_wrong_resource_gets_invalid_target` |
| Refresh rotation, a grace period for a retry, 30-day idle expiry, `invalid_grant` | MCP authorization §2.6; OAuth 2.1 §4.3; Claude "Troubleshoot" | Each refresh returns a new pair and ends the old access token. The old refresh token still works for 60 s (in case the answer was lost), then gets `invalid_grant`. A sign-in unused for 30 days is forgotten. A `claude.json` from 0.6.0 loads, and its sign-ins get an audience on their next refresh. | `C.Refresh_rotates_forgives_a_retry_for_a_minute_and_ends_after_30_idle_days`, `C.A_claude_json_from_before_still_loads_and_its_sign_ins_bind_on_refresh`, `C.Claude_adds_…` (step 10) |
| Access tokens for 1 hour, codes for 5 minutes, used once | OAuth 2.1 §4.1 | `expires_in: 3600`. A code lasts 300 s and works once. Token answers are `Cache-Control: no-store`. | `C.Claude_signs_in_with_its_published_identity`, `C.Refresh_rotates_…`, `C.Claude_adds_…` (step 8) |
| Revocation (RFC 7009) | MCP authorization (2026-07-28); Claude "Authentication" (disconnect) | `/revoke` takes an access or refresh token and ends the whole sign-in. It always answers 200, even for an unknown token, and a request that names a different client ends nothing. | `C.Revoking_ends_the_sign_in`, `C.Claude_adds_…` (step 11) |
| `/token` takes `application/x-www-form-urlencoded`, and the client id in the body or as Basic auth | OAuth 2.1 §3.2 | Form fields are read. For a public client, a Basic header's secret is ignored. | `C.Registration_takes_what_clients_send_…`, `C.Claude_adds_…` |
| Fast answers (Claude gives up after about 10 s) | Claude "Troubleshoot" | Nothing on the sign-in path waits on anything slow. The CIMD fetch has a 5 s timeout. | `C.Claude_adds_…` (the test fails if any answer through Funnel takes over 10 s) |
| CORS for browser clients, and an `Origin` check (DNS rebinding) | MCP Streamable HTTP §security (MUST validate `Origin`); MCP security considerations | Preflight on `/mcp` needs no sign-in. Allowed origins get `Access-Control-Allow-Origin` and see `WWW-Authenticate`. Discovery, `/register`, `/token` and `/revoke` allow any origin (no cookies). A page from any other site gets `403` with a JSON-RPC error. Allowed origins: claude.ai, claude.com, chatgpt.com, the library's own addresses, and localhost. | `C.A_browser_may_ask_first_without_signing_in`, `C.Only_claude_chatgpt_this_library_or_this_computer_may_call_from_a_page`, `C.A_page_on_claude_that_isnt_signed_in_can_read_where_to_sign_in` |
| Streamable HTTP, stateless | MCP Streamable HTTP | `POST /mcp` answers each request on its own, with no `Mcp-Session-Id`. A stale session id is ignored, not refused. `GET` and `DELETE` get `405`. The 2025-06-18, 2025-11-25 and 2026-07-28 revisions all work. A library restart or update loses Claude nothing. | `C.Every_request_stands_alone_so_a_restart_loses_claude_nothing`, `C.A_client_on_the_2026_07_28_revision_reads_with_its_per_request_headers` |
| Every advertised URL is the public https address behind Funnel | MCP authorization §2.3 (issuer and resource must match what the client used) | Behind Tailscale's proxy (plain http, `Host: <name>.ts.net`), every address is `https://<name>.ts.net`: the one recorded when Funnel was turned on, or `https` for any `*.ts.net` name. `X-Forwarded-*` headers only count from a proxy on this computer. | `C.Discovery_…`, `C.Forwarded_headers_count_only_from_a_proxy_on_this_computer`, `C.Claude_adds_…` (a real server behind a stand-in Funnel; the test fails on any request to another address) |
| Tools only read, and say so | MCP tools (annotations); Claude connector directory guidance | 15 tools on the web, each with a title and `readOnlyHint: true`, `destructiveHint: false`, `idempotentHint: true`. `openWorldHint` is set only on the two that reach Canvas live. `canvas_download` is left out, and no description tells Claude to use "file tools". | `C.Claude_on_the_web_sees_only_tools_that_read_each_labelled_so`, `C.The_stdio_server_keeps_canvas_download`, `C.Every_tool_needs_a_reading_toggle_on_purpose` |
| Off switch: AI tool access off, or a reading toggle off | Claude "Troubleshoot" (a bare 403 reads as a broken connector) | The connection stays up and the tools stay listed. Each call answers `isError` in words that name the setting. It's checked on every call, so turning it back on needs no reconnect. The sign-in page says so up front. | `C.Tool_access_off_still_connects_and_every_call_says_why_in_words`, `C.Canvas_off_refuses_every_tool_that_reads_canvas_naming_the_setting`, `C.Sign_in_page_names_the_library_the_redirect_and_what_it_reads`, `CT.A_reading_toggle_off_refuses_only_the_tools_that_need_it`, `AiToolAccessTests.*` |
| No redirects on the MCP URL | Claude "Troubleshoot" (redirects on the server URL break connecting) | `/mcp` answers directly, with no redirect to `/mcp/` and none from http to https inside the library. | `C.Claude_adds_…` (its client never follows a redirect, yet every MCP call succeeds) |
| The sign-in page names the client and where it returns | MCP security considerations (consent) | The page shows the library's name, "<client> wants to read your lectures", "will return to <host>", the published identity's host, and what the toggles let it read. It escapes everything the client chose. It is sent with `no-store`, `frame-ancestors 'none'`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`. | `C.Sign_in_page_names_the_library_the_redirect_and_what_it_reads`, `C.Sign_in_page_escapes_a_self_asserted_name`, `C.Sign_in_page_words_for_a_wrong_password_a_lockout_and_no_password`, `CT.Guessing_the_password_gets_locked_out` |
| Funnel on and off, with Tailscale's problems in plain words, and a check from the internet | (Study Stash) | See the card above. The check looks the name up with public DNS (not MagicDNS), then reads the PRM and AS metadata at the public address. | `ReachTests.*` |

## What was verified, and how

**Automated** (in `dotnet test engine/StudyStash.slnx`):

- `ConnectorTests.Claude_adds_Study_Stash_as_a_custom_connector` plays Claude's side from start to finish. It runs
  against a **real Kestrel server** on `127.0.0.1` with two made-up lectures, and the public address
  `https://mini.tail1234.ts.net`. A stand-in for Funnel (`FakeFunnel`) sends that address to the port over plain
  http, keeping `Host` and adding `X-Forwarded-For`/`-Proto`, as Tailscale does. It refuses any other address and
  any answer slower than 10 s. The client never follows redirects. The test signs in three ways:
  - Claude's published identity (CIMD) with `https://claude.ai/api/mcp/auth_callback`. It runs the unauthenticated
    `POST /mcp`, PRM, AS metadata, the sign-in page and its icon, a wrong password, the right one, the code
    exchange with PKCE and `resource`, then `initialize` / `tools/list` / `tools/call search_notes` through the MCP
    SDK's client. Then it refreshes (the old token is refused after the grace period) and revokes (`/mcp` is 401
    again).
  - Registration (DCR) with the same callback.
  - Claude Code's document, with a loopback callback on port 51234.
- `ConnectorTests.The_sdks_own_oauth_client_signs_in_through_funnel_by_itself` connects the MCP C# SDK's own OAuth
  client (`ClientOAuthOptions`) to the same real server through Funnel. It finds the sign-in from the 401 alone,
  once with a client metadata document and once by registering.
- `ConnectorTests.ChatGPT_signs_in_with_its_published_identity` signs in as ChatGPT does, with the document
  `https://chatgpt.com/oauth/client.json` answered on 2026-10-06 (kept word for word in the test) and ChatGPT's
  callback, `https://chatgpt.com/connector_platform_oauth_redirect`: as a public client, and with a signed assertion.
- The other tests in `ConnectorTests`, `ClaudeTests`, `ReachTests` and `AiToolAccessTests`, listed in the table.

**The ChatGPT desktop app** (run by hand, 2026-10-06: ChatGPT 26.930 for Mac, bundle `com.openai.codex`, with its own
Codex 0.160.1, signed in to a real ChatGPT account). The library was a throwaway one on 127.0.0.1 with the two
made-up lectures.

- ChatGPT's Codex (`codex exec`, with Study Stash given as `mcp_servers.study-stash` exactly as Connect writes it)
  started `StudyStash mcp --client codex` over stdio and called `list_classes` and `search_notes`; both answered from
  the library, and the note that turns the row to **Connected** was written.
- **Connect** on a copy of that ChatGPT's real `config.toml` (it already holds ChatGPT's own `node_repl` server and
  its plugins) added only Study Stash's table, `codex mcp list` then showed both servers, and **Disconnect** put the
  file back byte for byte.
- Reopen (2026-10-06, on a Mac): tried on a real app, Calculator, both open and closed. It quit and a new copy
  opened, with no system permission asked for. Not tried on ChatGPT or Claude themselves.
- ChatGPT, left open and idle, wasn't running its own `node_repl` MCP server: it starts MCP servers when a chat in
  Codex begins, which is why ChatGPT's row asks for one.
- Not done by hand: pressing Connect in the app's own window, then Reopen, then starting a chat in ChatGPT.

**MCP Inspector** (`@modelcontextprotocol/inspector` 2.8.0, `--cli`, run by hand). The library ran on 127.0.0.1
with a throwaway `--home`, two ingested lectures (CS 101, BIO 110), and a fake `tailscale` on `PATH` (it was never
called).

- With a token from `POST /api/v2/claude/tokens` passed as `--header "Authorization: Bearer …"`:
  - `tools/list`: 15 tools, all with titles and `readOnlyHint: true`, none destructive.
  - `--strict` found 0 errors and 6 portability warnings. Four tools' optional string arguments have
    `"type": ["string", "null"]`. That's legal JSON Schema, and Claude accepts it.
  - `tools/call list_classes`: "Library: Sam's library / CS 101 (1 lecture) / BIO 110 (1 lecture)".
  - `tools/call search_notes query=recursion`: the CS 101 lecture, and the passage at 18:05.
- With no token (`--stored-auth-only`): `auth_required`, from the library's `401 invalid_token`.
- **The Inspector's own OAuth** (`MCP_AUTO_OPEN_ENABLED=true`, with a stand-in `open` that caught the sign-in
  address, and curl playing the student):
  - The Inspector found the sign-in from the 401 by itself and registered (DCR).
  - It asked `/authorize` with `client_id`, `code_challenge` (S256), `redirect_uri=http://127.0.0.1:6276/oauth/callback`,
    `resource`, `scope` and `state`.
  - The page showed "wants to read your lectures", "will return to 127.0.0.1" and the loopback warning.
  - After the password, the library sent the browser back with `code`, `iss` and `state`. The Inspector said
    "Authorization complete." and `list_classes` answered.
  - The Inspector's CIMD option (`--client-metadata-url`) wasn't tried. The library would have to fetch a document
    from the internet, and this run had none to fetch.

**Not verified here:**

- A real connection from claude.ai or the Claude desktop app, through a real Funnel. That needs a real Claude
  account and tailnet, so the owner tests it from their own account.
- A real connection from chatgpt.com through a real Funnel: the same, with a ChatGPT account that can add a custom
  MCP server. Unknown until then: whether ChatGPT keeps its sign-in going with the refresh token, and the menu names.
- ChatGPT for Windows. The row looks for the Microsoft Store's `OpenAI.Codex` package, Codex on the PATH, or the
  `.codex` folder.
- The client id Claude on the web really uses for CIMD. The tests use a made-up hosted document.
- The name of Claude's menu. The card says Settings → Connectors. Newer versions of Claude may call it Customize →
  Connectors.
