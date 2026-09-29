using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using StudyStash.Core;
using StudyStash.Core.Ai;
using StudyStash.Core.Setup;

namespace StudyStash.Library;

/// <summary>
/// The door guided setup's AI reaches its setup tools through, open only while the setup window is: an MCP server on
/// this computer alone (127.0.0.1, a port the system picks), stateless, answering only a request that carries this
/// window's own token (32 random bytes, compared in constant time). A request from a web page (it has an Origin) or
/// addressed to any other host name is turned away, so no page in a browser can reach it.
/// </summary>
public sealed class SetupMcpHost : IAsyncDisposable
{
    readonly WebApplication app;
    readonly byte[] token;

    SetupMcpHost(WebApplication app, byte[] token, string token64)
    {
        this.app = app;
        this.token = token;
        Token = token64;
    }

    /// <summary>The tools' address: http://127.0.0.1:PORT/setup-mcp.</summary>
    public string Url { get; private set; } = "";
    public int Port { get; private set; }
    /// <summary>This window's token: given to the CLI in its environment only.</summary>
    public string Token { get; }

    /// <summary>Opens the door for <paramref name="tools"/>.</summary>
    public static async Task<SetupMcpHost> StartAsync(SetupTools tools, CancellationToken ct = default)
    {
        byte[] raw = RandomNumberGenerator.GetBytes(32);
        string token64 = Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Services.AddMcpServer(o =>
        {
            o.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = SetupChat.Server, Title = "Study Stash setup", Version = Engine.Version };
            o.ServerInstructions = "Study Stash's setup tools. get_setup_status is the truth; offer_… tools show the student a card and change nothing themselves.";
        })
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools(tools.Tools());
        var web = builder.Build();
        var host = new SetupMcpHost(web, Encoding.ASCII.GetBytes(token64), token64);
        web.Use(async (ctx, next) =>
        {
            if (host.Refused(ctx.Request) is { } status)
            {
                ctx.Response.StatusCode = status;
                return;
            }
            await next();
        });
        web.MapMcp(SetupChat.McpPath);
        await web.StartAsync(ct);
        string bound = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        host.Port = new Uri(bound).Port;
        host.Url = $"http://127.0.0.1:{host.Port}{SetupChat.McpPath}";
        return host;
    }

    /// <summary>Why a request is turned away (its status), or null to let it in: any Origin (a web page), a Host
    /// that isn't exactly 127.0.0.1 and this port, or no or the wrong token.</summary>
    int? Refused(HttpRequest req)
    {
        if (req.Headers.Origin.Count > 0) return StatusCodes.Status403Forbidden;
        if (!string.Equals(req.Host.Value, $"127.0.0.1:{Port}", StringComparison.Ordinal)) return StatusCodes.Status403Forbidden;
        string auth = req.Headers.Authorization.ToString();
        const string Bearer = "Bearer ";
        byte[] given = auth.StartsWith(Bearer, StringComparison.Ordinal) ? Encoding.ASCII.GetBytes(auth[Bearer.Length..].Trim()) : [];
        return CryptographicOperations.FixedTimeEquals(given, token) ? null : StatusCodes.Status401Unauthorized;
    }

    public async ValueTask DisposeAsync()
    {
        using var soon = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await app.StopAsync(soon.Token);
        }
        catch (OperationCanceledException)
        {
        }
        await app.DisposeAsync();
    }
}
