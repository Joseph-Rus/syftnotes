using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace StudyStash.Core;

/// <summary>Whether an address is out on the public internet, and not this computer, the home network, the tailnet or
/// any other address the library shouldn't be talked into reaching for someone else.</summary>
public static class PublicAddress
{
    public static bool IsGlobal(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        byte[] b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224
                || (b[0] == 100 && (b[1] & 0xC0) == 64)      // 100.64/10: carrier NAT, and Tailscale
                || (b[0] == 169 && b[1] == 254)              // link-local
                || (b[0] == 172 && (b[1] & 0xF0) == 16)      // 172.16/12
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                || (b[0] == 198 && (b[1] & 0xFE) == 18)      // 198.18/15: benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
        // Only 2000::/3 is global unicast; that leaves out loopback, link-local, unique-local (Tailscale's fd7a:) and
        // multicast. Inside it, documentation and Teredo aren't real hosts, and 6to4 carries an IPv4 address to check.
        if ((b[0] & 0xE0) != 0x20) return false;
        if (b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0x0d && b[3] == 0xb8) || (b[2] == 0 && b[3] == 0))) return false;
        if (b[0] == 0x20 && b[1] == 0x02) return IsGlobal(new IPAddress(b[2..6]));
        return true;
    }
}

/// <summary>
/// Reads an app's client metadata document (OAuth CIMD): the page at an https client_id that says what the app is and
/// where its sign-ins go back to. It is on the internet and the address comes from whoever is signing in, so the fetch
/// is fenced in: https on 443 only, public addresses only (checked, then connected to directly, so a second DNS
/// answer can't point it somewhere else), no redirects, 5 seconds and 16 KB at most. A document is kept for as long
/// as it asks, between 5 minutes and a day.
/// </summary>
public sealed class ClientDocuments
{
    public const int MaxBytes = 16 * 1024;
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(5), Shortest = TimeSpan.FromMinutes(5), Longest = TimeSpan.FromHours(24);

    /// <summary>The one the library uses, with the real DNS and network.</summary>
    public static ClientDocuments Shared { get; } = new();

    public Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; init; } = (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    /// <summary>What sends the request, given the address it was checked to be at: every connection goes there.</summary>
    public Func<IPAddress, HttpMessageHandler> Handler { get; init; } = PinnedTo;

    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    readonly ConcurrentDictionary<string, (string Body, DateTimeOffset Until)> kept = new();

    /// <summary>The document's text, or null when it can't be read safely.</summary>
    public async Task<string?> FetchAsync(Uri url, CancellationToken ct)
    {
        if (url.Scheme != Uri.UriSchemeHttps || url.Port != 443 || url.UserInfo.Length > 0) return null;
        string key = url.AbsoluteUri;
        if (kept.TryGetValue(key, out var hit) && hit.Until > Clock()) return hit.Body;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Wait);
        try
        {
            var addresses = await Resolve(url.IdnHost, cts.Token);
            if (addresses.Length == 0 || !addresses.All(PublicAddress.IsGlobal)) return null;
            using var handler = Handler(addresses[0]);
            using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Wait };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaxBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[MaxBytes + 1];
            int read = 0, n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), cts.Token)) > 0) read += n;
            if (read > MaxBytes) return null;
            string body = Encoding.UTF8.GetString(buffer, 0, read);
            var age = response.Headers.CacheControl?.MaxAge ?? Shortest;
            kept[key] = (body, Clock() + (age < Shortest ? Shortest : age > Longest ? Longest : age));
            return body;
        }
        catch (Exception e) when (e is HttpRequestException or SocketException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    static SocketsHttpHandler PinnedTo(IPAddress ip) => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, ConnectTimeout = Wait,
        ConnectCallback = async (_, ct) =>
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, 443), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
