using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Audio;

namespace StudyStash.App.Tests;

/// <summary>A pretend model mirror: serves files by name, can fail the first requests (no internet), and can hold a
/// file's request open until it's cancelled (a big download under way).</summary>
sealed class Mirror : HttpMessageHandler
{
    public Dictionary<string, byte[]> Files { get; } = [];
    /// <summary>Requests for these never answer until they're cancelled.</summary>
    public HashSet<string> Hold { get; } = [];
    public int FailFirst { get; set; }
    public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int asked;

    public int Asked => Volatile.Read(ref asked);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref asked);
        string name = request.RequestUri!.Segments[^1];
        if (FailFirst > 0)
        {
            FailFirst--;
            throw new HttpRequestException("No route to host");
        }
        if (Hold.Contains(name))
        {
            Held.TrySetResult();
            try
            {
                await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Released.TrySetResult();
            }
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[name]) };
    }
}
