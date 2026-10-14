using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace StudyStash.Library;

/// <summary>
/// Wrong tries at the library's password, counted by the address they come from, at every door the password opens:
/// the sign-in page, the key laptops and tools send, the extension's door (it takes the password too), and the
/// sign-in Claude and ChatGPT use. An address's wrong answers come slower after <see cref="Quick"/> of them, and
/// after <see cref="Most"/> in <see cref="Window"/> nothing it sends is looked at, right or wrong, until the oldest
/// is that old; the answer says how long that is.
///
/// An address only ever uses up its own tries, so someone guessing can't keep the student out from anywhere else.
/// This computer isn't counted at all (its pages need no password). A right password is never counted, however
/// often it's sent. And the same wrong password sent again counts once: it isn't a new guess, and a laptop that
/// still has the old password after a change asks with it every few seconds without ever being shut out for it.
/// Kept in memory: a library that starts again has forgotten, which nobody outside can make it do.
/// </summary>
public sealed class PasswordTries
{
    /// <summary>Different wrong passwords one address gets in <see cref="Window"/>.</summary>
    public const int Most = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    /// <summary>Wrong passwords answered at once; each one after waits a second longer than the last, up to
    /// <see cref="LongestHold"/>.</summary>
    public const int Quick = 3;
    public static readonly TimeSpan LongestHold = TimeSpan.FromSeconds(5);
    /// <summary>Addresses remembered at once. Past it the one that tried longest ago is forgotten.</summary>
    const int MaxAddresses = 5000;

    /// <summary>What became of one try: it was right; or it wasn't looked at, with how long until one will be
    /// (<paramref name="Wait"/>); or it was wrong, with how long to hold the answer back (<paramref name="Hold"/>).</summary>
    public readonly record struct Answer(bool Right, TimeSpan? Wait, TimeSpan Hold);

    readonly Func<DateTimeOffset> clock;
    readonly Func<TimeSpan, CancellationToken, Task> pause;
    readonly Lock gate = new();
    readonly Dictionary<string, List<(DateTimeOffset At, ulong Guess)>> wrong = [];
    /// <summary>Wrong passwords are remembered as a keyed hash that lasts as long as this library runs, enough to
    /// tell one sent again from a new guess.</summary>
    readonly byte[] salt = RandomNumberGenerator.GetBytes(32);

    /// <param name="clock">The time (tests move it).</param>
    /// <param name="pause">Holding an answer back (tests don't wait).</param>
    public PasswordTries(Func<DateTimeOffset>? clock = null, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.pause = pause ?? Task.Delay;
    }

    /// <summary>An address's wrong tries still inside the window, oldest first, or null when it has none. Called
    /// under the lock.</summary>
    List<(DateTimeOffset At, ulong Guess)>? Recent(string address, DateTimeOffset now)
    {
        if (!wrong.TryGetValue(address, out var tries)) return null;
        tries.RemoveAll(t => now - t.At >= Window);
        if (tries.Count > 0) return tries;
        wrong.Remove(address);
        return null;
    }

    /// <summary>How long until a try from <paramref name="address"/> is looked at again, or null when one would be
    /// now (always, for this computer: a null address).</summary>
    public TimeSpan? Wait(string? address)
    {
        if (address is null) return null;
        lock (gate)
        {
            var now = clock();
            return Recent(address, now) is { Count: >= Most } tries ? tries[0].At + Window - now : null;
        }
    }

    /// <summary>One try at the password from <paramref name="address"/> (null: this computer, never counted).
    /// <paramref name="right"/> says whether what was typed is the password; it isn't asked while the address is
    /// waiting, so a guess sent then tells nothing.</summary>
    public Answer Try(string? address, string typed, Func<string, bool> right)
    {
        if (address is null) return new(right(typed), null, TimeSpan.Zero);
        lock (gate)
        {
            var now = clock();
            var tries = Recent(address, now);
            if (tries is { Count: >= Most }) return new(false, tries[0].At + Window - now, TimeSpan.Zero);
            if (right(typed)) return new(true, null, TimeSpan.Zero);
            ulong guess = BinaryPrimitives.ReadUInt64LittleEndian(HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(typed)));
            if (tries is not null && tries.Any(t => t.Guess == guess)) return new(false, null, TimeSpan.Zero);
            if (tries is null)
            {
                MakeRoom(now);
                wrong[address] = tries = [];
            }
            tries.Add((now, guess));
            // In the library's log, so its owner can see that someone has been guessing, and from where.
            if (tries.Count == Most) Console.WriteLine($"[sign-in] {Most} wrong passwords from {address}: nothing from there is looked at for {Window.TotalMinutes:0} minutes");
            int slow = tries.Count - Quick;
            return new(false, null, slow <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(slow, LongestHold.TotalSeconds)));
        }
    }

    /// <summary>Room for one more address: those whose tries have all aged out go, then, if it's still full, the one
    /// that tried longest ago. Called under the lock.</summary>
    void MakeRoom(DateTimeOffset now)
    {
        if (wrong.Count < MaxAddresses) return;
        foreach (string address in wrong.Keys.ToList()) Recent(address, now);
        while (wrong.Count >= MaxAddresses) wrong.Remove(wrong.MinBy(kv => kv.Value[^1].At).Key);
    }

    /// <summary>Holds a wrong answer back for <see cref="Answer.Hold"/>. A sender that hangs up isn't waited for.</summary>
    public async Task HoldAsync(TimeSpan hold, CancellationToken gone)
    {
        if (hold <= TimeSpan.Zero) return;
        try
        {
            await pause(hold, gone);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>What every door says while an address waits.</summary>
    public static string Words(TimeSpan wait)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
        return $"Too many wrong passwords. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
    }

    /// <summary>The wait in whole seconds, for a Retry-After header.</summary>
    public static string Seconds(TimeSpan wait) => Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The address a request's try counts against, or null for this computer itself (the same test the library's
    /// pages use to let its owner in without a password). A connection from anywhere else counts as the address it
    /// comes from, whatever headers it sends: a made-up X-Forwarded-For gets nobody a fresh count, and can't make a
    /// stranger look like this computer. From this computer with a proxy's headers, it's Tailscale Serve or Funnel
    /// (or a proxy the student runs here) passing someone on: they are who the last X-Forwarded-For names, the one
    /// the proxy wrote itself, not what the sender put before it. One that names nobody shares a count with the
    /// others like it.
    /// </summary>
    public static string? AddressOf(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (!IPAddress.IsLoopback(ip)) return Name(ip);
        if (!LibraryWeb.ProxyHeaders.Any(h => ctx.Request.Headers.ContainsKey(h))) return null;
        string last = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',')[^1].Trim();
        return IPAddress.TryParse(last, out var from) ? Name(from) : "proxy";
    }

    /// <summary>An address as it's counted: an IPv4 address by itself; an IPv6 one by its network (the first 64
    /// bits), since whoever has one address there has the rest of it to send from.</summary>
    static string Name(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        byte[] network = ip.GetAddressBytes();
        Array.Clear(network, 8, 8);
        return new IPAddress(network) + "/64";
    }
}
