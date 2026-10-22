using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyStash.Core;
using StudyStash.Core.Ai;

namespace StudyStash.App.ViewModels;

/// <summary>One kept conversation in an Ask bar's Chats list: its name (its first question, until it's renamed), when
/// it was last added to, and on a folder's page or Home which lecture or folder it's about. Pressed, it opens in the
/// card to carry on; it can be renamed or deleted from the list.</summary>
public sealed partial class ChatRow : ObservableObject
{
    public string Id { get; }
    [ObservableProperty] public partial string Title { get; set; }
    public string When { get; }
    public string Where { get; }
    public string Sub => Where.Length > 0 ? $"{When} · {Where}" : When;
    /// <summary>The one open in the card now.</summary>
    [ObservableProperty] public partial bool Current { get; set; }
    [ObservableProperty] public partial bool Renaming { get; set; }
    [ObservableProperty] public partial string NewTitle { get; set; } = "";
    public IAsyncRelayCommand Open { get; }
    public IRelayCommand Rename { get; }
    public IAsyncRelayCommand CommitRename { get; }
    public IRelayCommand CancelRename { get; }
    public IAsyncRelayCommand Delete { get; }
    public string RenameSaid => $"Rename {Title}";
    public string DeleteSaid => $"Delete {Title}";

    public ChatRow(AskChat chat, string where, DateTimeOffset now, AiAskModel owner)
    {
        Id = chat.Id;
        Title = chat.Title.Length > 0 ? chat.Title : "Chat";
        When = WhenWords(chat.Updated, now);
        Where = where;
        Open = new AsyncRelayCommand(() => owner.OpenChatAsync(Id));
        Rename = new RelayCommand(() =>
        {
            NewTitle = Title;
            Renaming = true;
        });
        CommitRename = new AsyncRelayCommand(() => owner.RenameChatAsync(this, NewTitle));
        CancelRename = new RelayCommand(() => Renaming = false);
        Delete = new AsyncRelayCommand(() => owner.DeleteChatAsync(this));
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(RenameSaid));
        OnPropertyChanged(nameof(DeleteSaid));
    }

    /// <summary>"Today 4:30 PM", "Yesterday 9:05 AM", "Mon 4:30 PM" this week, else "Mon 12 Oct".</summary>
    public static string WhenWords(string updated, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) return "";
        var local = at.ToOffset(now.Offset);
        var c = CultureInfo.InvariantCulture;
        int days = (now.Date - local.Date).Days;
        return days switch
        {
            0 => "Today " + local.ToString("h:mm tt", c),
            1 => "Yesterday " + local.ToString("h:mm tt", c),
            > 1 and < 7 => local.ToString("ddd h:mm tt", c),
            _ => local.ToString("ddd d MMM", c),
        };
    }
}

public sealed partial class AiAskModel
{
    /// <summary>Where this bar's conversations are kept and listed: a lecture's page (or the recording's chat, under
    /// the recording's own id, so it's on the lecture after Stop), a folder's, or Home. Null: nothing kept (a stand-in).</summary>
    public AskPlace? Place { get; set; }

    /// <summary>The kept conversation open in the card, and so the one the next question goes on; null for a new one
    /// (the library starts it with the first answer).</summary>
    public string? ChatId { get; private set; }

    /// <summary>This place's kept conversations, newest first: the card's and the bar's Chats list.</summary>
    public ObservableCollection<ChatRow> Chats { get; } = [];
    public bool HasChats => Chats.Count > 0;

    /// <summary>A chat was picked from the list (or deleted there): the views close the list.</summary>
    public event Action? ChatsDone;

    /// <summary>A chat was deleted: the host says so with Undo (the title, and what Undo does).</summary>
    public Action<string, Func<Task>>? ToldDeleted { get; set; }

    /// <summary>What "now" is, for the list's "Today 4:30 PM" (a test's clock).</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Reads this place's chats into the list; with <paramref name="openLatest"/>, and nothing asked here yet,
    /// the latest is loaded into the card (closed: the next question, or opening it, carries it on).</summary>
    public async Task LoadChatsAsync(bool openLatest = false)
    {
        if (Place is null) return;
        List<AskChat> chats;
        try
        {
            chats = await library.ChatsAsync(Place.Scope == "lecture" ? Place.Lecture : null, Place.Scope == "class" ? Place.Class : null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or System.Text.Json.JsonException)
        {
            return; // the list waits for the library; asking says what's wrong
        }
        ShowChats(chats);
        if (openLatest && Turns.Count == 0 && ChatId is null && chats.Count > 0) await OpenChatAsync(chats[0].Id, show: false);
    }

    void ShowChats(IEnumerable<AskChat> chats)
    {
        var now = Now();
        Chats.Clear();
        foreach (var c in chats) Chats.Add(new ChatRow(c, WhereWords(c), now, this) { Current = c.Id == ChatId });
        OnPropertyChanged(nameof(HasChats));
    }

    /// <summary>On a folder's page or Home, which lecture or folder a chat is about ("" where it was started here).</summary>
    string WhereWords(AskChat c) => Place?.Scope switch
    {
        "lecture" => "",
        "class" => c.Scope == "lecture" ? c.LectureTitle : "",
        _ => c.Scope == "lecture" ? c.LectureTitle : c.Scope == "class" ? ClassNames.Display(c.Class) : "",
    };

    /// <summary>A kept conversation opened in the card: its turns as they were, and the next question carries it on
    /// (its turns go to the AI with it). <paramref name="show"/>: the card opens on it.</summary>
    public async Task OpenChatAsync(string id, bool show = true)
    {
        AskChat? chat;
        try
        {
            chat = await library.ChatAsync(id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException or System.Text.Json.JsonException)
        {
            return;
        }
        ChatsDone?.Invoke();
        if (chat?.Messages is not { } messages)
        {
            await LoadChatsAsync(); // gone meanwhile (deleted on another device)
            return;
        }
        Stop();
        Turns.Clear();
        var earlier = new List<AiTurn>();
        for (int i = 0; i + 1 < messages.Count; i += 2)
        {
            if (messages[i].Role != "user" || messages[i + 1].Role != "assistant") continue;
            var a = messages[i + 1];
            var turn = new AiTurn(messages[i].Text, a.EngineName) { Answer = a.Text, Sources = a.Sources, Engine = a.Engine };
            if (a.Failed) turn.Failed = a.Text;
            else if (a.Stopped) turn.Byline = AiWords.Stopped;
            else
            {
                bool readChat = SentElsewhere(a.Engine, earlier.TakeLast(AskMemory.Turns));
                turn.Byline = AiWords.AskByline(a.EngineName, a.Sources, readChat);
                turn.BylineParts = AiWords.AskBylineParts(a.EngineName, a.Sources, OnSource, readChat);
            }
            Turns.Add(turn);
            earlier.Add(turn);
        }
        ChatId = id;
        foreach (var row in Chats) row.Current = row.Id == id;
        AnswerClosed = !show;
        OnPropertyChanged(nameof(Latest));
        ThreadShown();
        if (show) Asked?.Invoke(); // shown from its end, the latest answer in view
    }

    /// <summary>Whether the chat above went to an AI that hadn't had it: Claude or ChatGPT, after the free AI (or
    /// another) answered some of it.</summary>
    static bool SentElsewhere(string engine, IEnumerable<AiTurn> remembered) =>
        engine is { Length: > 0 } and not "ollama" && remembered.Any(t => t.Engine.Length > 0 && t.Engine != engine);

    /// <summary>A turn kept in a chat: the list shows the chat at its top, named for its first question.</summary>
    void Kept(string id, string firstQuestion)
    {
        ChatId = id;
        var at = Chats.FirstOrDefault(r => r.Id == id);
        var row = new ChatRow(new AskChat(id, at?.Title ?? firstQuestion, Now().ToString("o", CultureInfo.InvariantCulture)), at?.Where ?? "", Now(), this) { Current = true };
        if (at is not null) Chats.Remove(at);
        foreach (var r in Chats) r.Current = false;
        Chats.Insert(0, row);
        OnPropertyChanged(nameof(HasChats));
    }

    public async Task RenameChatAsync(ChatRow row, string title)
    {
        title = title.Trim();
        row.Renaming = false;
        if (title.Length == 0 || title == row.Title) return;
        try
        {
            if (await library.RenameChatAsync(row.Id, title) is { } renamed) row.Title = renamed.Title;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            // the name stays as it was
        }
    }

    /// <summary>A chat deleted: out of the list (and out of the card, if it's the one open), and the host offers Undo,
    /// which brings it back to the list.</summary>
    public async Task DeleteChatAsync(ChatRow row)
    {
        // An answer still being written into it stops first: nothing more is kept in a chat being deleted.
        if (row.Id == ChatId) Stop();
        try
        {
            if (!await library.DeleteChatAsync(row.Id)) return;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LibraryRefusedException)
        {
            return;
        }
        Chats.Remove(row);
        OnPropertyChanged(nameof(HasChats));
        if (row.Id == ChatId) StartOver(show: false);
        ChatsDone?.Invoke();
        ToldDeleted?.Invoke(row.Title, async () =>
        {
            if (await library.RestoreChatAsync(row.Id)) await LoadChatsAsync();
        });
    }
}
