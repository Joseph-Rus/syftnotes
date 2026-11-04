using System.Net;
using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Everything that talks to the library gets its client from LibraryClients: the same one until the library's
/// address changes, a new one after, and a password changed meanwhile (Add a laptop, Password → Change) used at once.</summary>
public class LibraryClientsTests
{
    [Fact]
    public void Clients_stay_until_the_address_changes_and_none_is_made_before_a_library_exists()
    {
        var cc = new ClientConfig("unused");
        var clients = new LibraryClients(() => cc);
        Assert.Null(clients.Library);
        Assert.Null(clients.Canvas);

        cc.ServerUrl = "http://library:8787";
        var first = clients.Library;
        Assert.Same(first, clients.Library);
        Assert.Same(clients.Canvas, clients.Canvas);

        cc.ServerUrl = "http://other:8787";
        Assert.NotSame(first, clients.Library);
        Assert.Equal("http://other:8787", clients.Library!.ServerUrl);
    }

    [Fact]
    public async Task A_password_changed_while_a_client_is_kept_is_sent_on_the_next_request()
    {
        string password = "old";
        var seen = new List<string?>();
        var http = new HttpClient(new Handler(r => seen.Add(r.Headers.Authorization?.Parameter)));
        var library = new RemoteLibrary("http://library:8787", () => password, http);
        await library.LectureAsync("a");
        password = "new";
        await library.LectureAsync("a");
        Assert.Equal(["old", "new"], seen);
    }

    sealed class Handler(Action<HttpRequestMessage> saw) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            saw(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
