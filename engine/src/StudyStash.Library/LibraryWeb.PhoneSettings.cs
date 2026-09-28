using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using StudyStash.Core;

namespace StudyStash.Library;

/// <summary>
/// "Add a phone" on the library's own Settings page, for when the app isn't to hand: a QR code of the phone app's
/// address and the 6-digit code, how long it lasts, then the phones already paired, each with Remove.
/// </summary>
public sealed partial class LibraryWeb
{
    /// <summary>The code the page made last, or why it couldn't (said once, on the page after).</summary>
    JsonObject? pageCode;
    ReachProblem? pageCodeProblem;

    void MapPhoneSettings(WebApplication app)
    {
        app.MapPost("/settings/phone", Http.Handle(ctx => WithMemberAsync(ctx, async _ =>
        {
            (pageCode, pageCodeProblem) = await NewPhoneCodeAsync();
            return Http.SeeOther("/settings?phone=new#phone");
        })));
        app.MapPost("/settings/phone/{id}/remove", (HttpContext ctx, string id) => WithMember(ctx, _ =>
        {
            Phones.Remove(id);
            return Http.SeeOther("/settings#phone");
        }));
    }

    string PhoneSettingsGroup(HttpContext ctx, string nonce)
    {
        string shown = "";
        if (ctx.Request.Query["phone"] == "new")
        {
            if (pageCodeProblem is { } p)
                shown = $"<div class=\"notice\"><div>{Ui.Esc(p.Words)}"
                    + (p.FixUrl is { } fix ? $" <a href=\"{Ui.Esc(fix)}\">Open the page that fixes it</a>." : "") + "</div></div>";
            else if (pageCode is { } c && DateTimeOffset.Parse(c["expires"]!.GetValue<string>(), CultureInfo.InvariantCulture) is var until
                     && until > DateTimeOffset.UtcNow)
            {
                string url = c["url"]!.GetValue<string>(), code = c["code"]!.GetValue<string>();
                shown = "<div class=\"group phone-code\"><div class=\"row\" style=\"gap:1.2rem;align-items:center;flex-wrap:wrap\">"
                    + $"<div style=\"flex:none;line-height:0;border-radius:12px;overflow:hidden\">{Qr.Svg(url, 176, "QR code of the phone app's address")}</div>"
                    + "<div class=\"grow\"><div class=\"subtitle\">1. Open your phone's camera and point it at the code.</div>"
                    + $"<div class=\"subtitle\">2. Type this code in the page that opens:</div>"
                    + $"<div style=\"font:600 2rem/1.2 var(--mono);letter-spacing:.18em;margin:.4rem 0\">{code[..3]} {code[3..]}</div>"
                    + $"<div class=\"subtitle\" id=\"phone-left\" data-until=\"{until.ToUnixTimeMilliseconds()}\">Works for 10 minutes, once.</div>"
                    + $"<div class=\"subtitle\">3. Add it to your Home Screen from the Share menu.</div>"
                    + $"<div class=\"subtitle\" style=\"overflow-wrap:anywhere\">{Ui.Esc(url)}</div></div></div></div>"
                    + $"<script nonce=\"{nonce}\">(function(){{var el=document.getElementById('phone-left');if(!el)return;var until=+el.dataset.until;"
                    + "function tick(){var s=Math.max(0,Math.round((until-Date.now())/1000));el.textContent=s>0?'Works for '+Math.floor(s/60)+':'+String(s%60).padStart(2,'0')+' more, once.':'This code has run out. Make a new one.';"
                    + "if(s>0)setTimeout(tick,1000);}tick();})();</script>";
            }
        }
        var phones = Phones.List();
        string rows = string.Concat(phones.Select(d =>
            $"<form class=\"row\" method=\"post\" action=\"/settings/phone/{Ui.Quote(d.Id, "")}/remove\" data-confirm=\"Remove {Ui.Esc(d.Name)}? It can't read your library until it's added again.\">"
            + $"<div class=\"grow\"><div class=\"title\">{Ui.Esc(d.Name)}</div><div class=\"subtitle\">Added {Ui.Esc(Ui.ShortDate(d.Added.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}"
            + $" · last used {Ui.Esc(Ui.ShortDate(d.LastSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}</div></div>"
            + "<button class=\"danger\">Remove</button></form>"));
        return "<div class=\"group-head\" id=\"phone\">Your phone</div>" + shown
            + $"<div class=\"group\">{rows}<form class=\"row\" method=\"post\" action=\"/settings/phone\"><span class=\"grow\">"
            + (phones.Count == 0 ? "Read your notes and lectures on your phone" : "Add another phone") + "</span>"
            + "<button class=\"primary\">Add a phone</button></form></div>"
            + "<p class=\"group-foot\">Your phone reaches the library over Tailscale, so install Tailscale on it and sign in to the same "
            + "account. Removing a phone locks it out straight away.</p>";
    }
}
