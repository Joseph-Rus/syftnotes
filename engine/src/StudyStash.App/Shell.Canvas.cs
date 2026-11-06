using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.App.Platform;
using StudyStash.App.Windows;
using StudyStash.Core;

namespace StudyStash.App;

/// <summary>The shell's Canvas side: one context and one poll for the whole app, and what they feed — the sidebar's
/// Due and its count, the Due page, a linked class's own page, the dropdown's next due, the quick panel's Canvas rows,
/// Canvas's notifications and the window that connects Canvas.</summary>
public static partial class Shell
{
    /// <summary>The one Canvas context and watch, for the library the app is connected to now (a new library makes
    /// both again: <see cref="OnCanvasMade"/>).</summary>
    static CanvasLink canvasLink = null!;
    /// <summary>The library (address and key) the Canvas context was made for.</summary>
    static string canvasFor => canvasLink.MadeFor;
    static CanvasWatch? canvasWatch => canvasLink?.Current;
    static CanvasNotifier? canvasNotifier;
    static DispatcherTimer? canvasNotifyTimer;
    static bool canvasWatching;
    /// <summary>The library's last answers about Canvas (what's due, and every class with its course), kept current
    /// by the watch.</summary>
    static CanvasFeed? canvasFeed;
    static CanvasApi.DueResponse? canvasDue => canvasFeed?.Due;
    static IReadOnlyList<CanvasApi.ClassRow> canvasClasses => canvasFeed?.Classes ?? [];
    static Window? canvasConnectWindow;

    /// <summary>The Canvas context every Canvas screen shares, for the library the app is connected to now.</summary>
    static CanvasContext Canvas() => canvasLink.Context;

    static CanvasWatch CanvasPoll() => canvasLink.Watch;

    /// <summary>Marks a Due item done in Syft Notes alone, through the one copy of the list (<see cref="CanvasFeed"/>).</summary>
    static DueMarker? CanvasMarker() => canvasFeed is { } feed ? (cls, kind, id, mark, stop) => feed.MarkAsync(cls, kind, id, mark, stop) : null;

    /// <summary>A new library, so a new Canvas context and watch: the old watch (already stopped) is let go, and the
    /// status line, what's due and Canvas's notifications follow the new one.</summary>
    static void OnCanvasMade(CanvasWatch? old, CanvasContext context, CanvasWatch watch)
    {
        if (old is not null) old.Changed -= OnCanvasChanged;
        canvasWatching = false;
        watch.Changed += OnCanvasChanged;
        canvasFeed?.Dispose();
        canvasFeed = new CanvasFeed(context, watch);
        canvasFeed.Updated += touched => Dispatcher.UIThread.Post(() => Run(() => CanvasUpdatedAsync(touched), "CanvasUpdated"));
        canvasNotifier = new CanvasNotifier(context) { OnOpen = OpenCanvasNotification };
#if TEST_HELPERS // a student's copy leaves it out (an if on the const would be unreachable code there)
        FollowTidy(context, watch);
#endif
    }

    /// <summary>Canvas is watched while the app has a library that answers, and not otherwise.</summary>
    static void KeepCanvasWatched()
    {
        bool want = !quitting && host.Library == LibraryState.Connected && !host.OlderLibrary;
        var watch = CanvasPoll();
        if (want && !canvasWatching)
        {
            canvasWatching = true;
            watch.Start();
        }
        else if (!want && canvasWatching)
        {
            canvasWatching = false;
            watch.Stop();
        }
        if (want && canvasNotifyTimer is null)
        {
            canvasNotifyTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => UiTasks.RunQuietly(() => PollCanvasNotificationsAsync(), "PollCanvasNotifications"));
            canvasNotifyTimer.Start();
            Run(() => PollCanvasNotificationsAsync(), "PollCanvasNotifications");
        }
    }

    static void StopCanvas()
    {
        canvasWatch?.Stop();
        canvasWatching = false;
        canvasNotifyTimer?.Stop();
        canvasNotifyTimer = null;
    }

    /// <summary>The poll heard from the library: the status line follows it. What's due follows through
    /// <see cref="CanvasFeed"/>, which reads again whenever the library says Canvas changed.</summary>
    static void OnCanvasChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (quitting || canvasWatch?.State is not { } state) return;
        // Which browser the extension checked in from: the one "Open in the browser" opens, anywhere in the app.
        Browsers.Heard = Browsers.HeardFrom(state.Extension);
        // The extension is in two browsers: ask which one to use, once. The question gone (answered somewhere else,
        // or one of the browsers closed) takes its window with it; one the student opened from Settings stays for as
        // long as there are two to choose between.
        if (state.Extension?.ChooseBrowser is { Browsers.Count: > 1 } ask) AskWhichBrowser(ask);
        else if (!browserChoiceByHand || state.Extension?.Browsers is not { Browsers.Count: > 1 }) browserChoiceWindow?.Close();
        library.Status = LibraryStatus();
        UpdateDueStatus();
    });

    // --- Submit & Organize's Ready to tidy (test builds only) ---------------------------------------------------------

    /// <summary>The hand-ins waiting to be tidied, for the library the app is connected to now.</summary>
    static TidyList? tidyList;

    /// <summary>A new Canvas context: Ready to tidy follows its watch. Home's list and the assignment page follow the
    /// list; a hand-in new to it is said once in a notification whose Tidy up opens its card.</summary>
    static void FollowTidy(CanvasContext context, CanvasWatch watch)
    {
        tidyList?.Dispose();
        var list = tidyList = new TidyList(context, watch);
        list.Changed += () => Dispatcher.UIThread.Post(ShowTidy);
        list.Arrived += item => Dispatcher.UIThread.Post(() => Toast(FilingWords.HandedIn(item.Waiting.Assignment), FilingWords.ToTidy(item.Files),
            FilingWords.TidyUp, () => Dispatcher.UIThread.Post(() => OpenTidyCard(item))));
    }

    /// <summary>Home's Ready to tidy, newest first.</summary>
    static IEnumerable<TidyRow> TidyRows()
    {
        if (tidyList is not { } list) yield break;
        var ctx = Canvas();
        foreach (var item in list.Items.OrderByDescending(i => i.Waiting.At ?? DateTimeOffset.MinValue))
            yield return new TidyRow(item.Id, ctx.DotOf(item.Waiting.Class), item.Waiting.Assignment,
                FilingWords.RowLine(item.Files, item.Waiting.At is { } at ? CanvasWords.When(at, ctx.Clock.Zone, ctx.Clock.Now()) : ""),
                () => OpenTidyCard(item),
                () => Run(async () =>
                {
                    if ((await list.DismissAsync(item)).Failure is { } failed) Toast(FilingWords.NotThisOne, failed.Words, null, null);
                }, "DismissTidy"));
    }

    /// <summary>Tidy up on the assignment's page while its hand-in waits; null when it doesn't.</summary>
    static Action? TidyFor(string cls, string id) => tidyList?.For(cls, id) is { } item ? () => OpenTidyCard(item) : null;

    /// <summary>The list changed: Home's part and the open assignment's button follow.</summary>
    static void ShowTidy()
    {
        if (library.Overview is { IsHome: true } home) home.ShowTidy(TidyRows());
        if (library.Assignment is { } page && pageState.Assignment is { } open) page.OnTidy = TidyFor(open.Class, open.Id);
    }

    /// <summary>The tidy card over the library window (brought forward, as the notification's Tidy up asks).</summary>
    static void OpenTidyCard(TidyItem item)
    {
        ShowLibrary();
        if (mainWindow is not { } w || ConfirmLayer.For(w) is not { } layer || tidyList is not { } list) return;
        var model = canvasModels.TidyCard(item, Canvas().DotOf(item.Waiting.Class),
            new TidyServices((folder, title) => TidyCard.PickFileAsync(layer.Host, folder, title), list.Tidied));
        Run(() => TidyCard.ShowAsync(layer, model), "OpenTidyCard");
    }

    static Window? browserChoiceWindow;
    /// <summary>The question already asked since the app started: Not now isn't asked again until the next start.</summary>
    static string browserAsked = "";
    /// <summary>The open one was asked for from Settings, not come up by itself.</summary>
    static bool browserChoiceByHand;

    /// <summary>Settings' "Change browser": the same question, whether or not it's been answered.</summary>
    public static void ChooseBrowser(CanvasApi.BrowserQuestion ask) => AskWhichBrowser(ask, byHand: true);

    /// <summary>"Which browser do you use for Canvas?", in a small window of its own. By itself, once for each pair of
    /// browsers: not while setup or Connect Canvas is busy with the same thing, and not before setup is done.</summary>
    static void AskWhichBrowser(CanvasApi.BrowserQuestion ask, bool byHand = false)
    {
        if (browserChoiceWindow is { } open)
        {
            if (byHand) open.Activate();
            return;
        }
        string asked = ask.Where + "\n" + string.Join("\n", ask.Browsers);
        if (!byHand)
        {
            if (asked == browserAsked) return;
            if (!host.Settings.SetupDone || setupWindow?.IsVisible == true || canvasConnectWindow?.IsVisible == true) return;
        }
        browserAsked = asked;
        browserChoiceByHand = byHand;
        string where = ask.Where;
        var model = new BrowserChoiceModel(ask.Browsers, browser => ChooseBrowserAsync(where, browser));
        var w = BrowserChoiceWindow.Make(model);
        browserChoiceWindow = w;
        // Asked for, it takes the keyboard like any window the student opens. Come up by itself, it doesn't.
        AppWindows.Dress(w, "browser-choice", activate: byHand, appForward: false);
        w.Closed += (_, _) =>
        {
            if (browserChoiceWindow == w) browserChoiceWindow = null;
        };
        // In the middle of the pointer's display. It sets its own height (as tall as what it says).
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Position = Placement.Centred(AppWindows.ScreenFor(w), BrowserChoiceWindow.Wanted).Position;
        AppWindows.Show(w);
    }

    /// <summary>The student's pick: the library is told, the app opens Canvas in that browser from now on, and the
    /// status follows at once. Null when the library took it; else why not, for the window.</summary>
    static async Task<string?> ChooseBrowserAsync(string where, string browser)
    {
        if (Canvas().Client is not { } client) return "Your library isn't reachable right now. Try again in a moment.";
        await client.ChooseBrowserAsync(where, browser);
        // The app opens Canvas there too, at once: in the browser on this computer that goes by that name (Arc calls
        // itself Chrome; the two browsers may be on the library's computer, not this one). That's also the browser
        // Connect Canvas starts from next time.
        Browsers.Heard = browser;
        var mine = await Task.Run(() => Browsers.ForCanvas());
        if (mine.Says.Equals(browser, StringComparison.OrdinalIgnoreCase) && host.Settings.CanvasBrowser != mine.Name)
        {
            Browsers.Picked = mine.Name;
            host.Save(s => s.CanvasBrowser = mine.Name);
        }
        if (canvasWatch is { } watch) await watch.RefreshAsync();
        return null;
    }

    /// <summary>Reads Canvas again now (Canvas's connect window finished), then shows it.</summary>
    static async Task CanvasSyncedAsync()
    {
        if (!await LoadCanvasAsync()) return;
        await CanvasUpdatedAsync();
    }

    /// <summary>Newer Canvas data is in (a sync finished a class, a course was linked or chosen): the sidebar's Due
    /// and its count, the dropdown's next due, Canvas's notifications, and the page the window shows if it shows
    /// Canvas's data. Only what <paramref name="touched"/> names is read again (null: any class may have changed):
    /// a class's own Canvas page or folder page only when it's that class's news, and Due's term only for those
    /// classes. A page read again the same stays as it is.</summary>
    static async Task CanvasUpdatedAsync(IReadOnlySet<string>? touched = null)
    {
        if (quitting) return;
        UpdateNextDue();
        UpdateDueItem();
        // A sync's news (new work, a score, an announcement) shows now, not at the notifications' next minute.
        Run(() => PollCanvasNotificationsAsync(), "PollCanvasNotifications");
        ForgetTerm(touched);
        if (mainWindow?.IsVisible != true) return;
        bool Touched(string cls) => touched is null || touched.Contains(cls);
        // Home and Due show every class's work; a folder's page only its own.
        if (pageState.Overview is { } over) { if (over.Length == 0 || over == DueHome || Touched(over)) await ShowOverviewAsync(over); }
        else if (pageState.CanvasClass is { } name && Touched(name) && library.CanvasClass is { } cls && CanvasClassRow(name) is { } row) await cls.LoadAsync(row);
    }

    /// <summary>Asks the library what's due and which classes are linked. False when it couldn't (no library, an
    /// older one, or no answer), leaving what was known before.</summary>
    static Task<bool> LoadCanvasAsync()
    {
        Canvas();
        return canvasFeed!.LoadAsync();
    }

    static bool CanvasLinked => canvasClasses.Any(c => c.Linked);

    static CanvasApi.ClassRow? CanvasClassRow(string? name) =>
        name is null ? null : canvasClasses.FirstOrDefault(c => c.Linked && c.Class == name);

    /// <summary>"Library connected · Canvas synced 10:24" once Canvas has synced; otherwise the library alone.</summary>
    static string LibraryStatus()
    {
        string status = host.Settings.Role != AppRole.Laptop && host.LocalLibrary?.State == LibraryServiceState.Running
            ? $"Library running on this {Machine.Device}"
            : host.Library switch
            {
                LibraryState.Connected => "Library connected",
                LibraryState.Starting => "Starting your library…",
                LibraryState.Unreachable => "Can't reach your library",
                LibraryState.WrongPassword => "Library password changed",
                _ => "No library yet",
            };
        if (host.Library == LibraryState.Connected && canvasWatch?.State is { Status: "connected" or "syncing" or "updated", LastSync: { } synced })
            status += $" · Canvas synced {CanvasWords.Clock(synced, TimeZoneInfo.Local)}";
        // Lectures waiting for their notes aren't in a class yet (the notes say which): without this, a class that has
        // a week of lectures queued reads 0 and they look lost.
        if (host.Library == LibraryState.Connected && host.Overview?["writing"] is JsonValue w && w.TryGetValue(out int writing) && writing > 0)
            status += NotesOff ? writing == 1 ? " · Filing 1 lecture" : $" · Filing {writing} lectures"
                : writing == 1 ? " · Writing notes for 1 lecture" : $" · Writing notes for {writing} lectures";
        return status;
    }

    /// <summary>The Due page's card for when Canvas needs something: any state but connected or syncing. Its button
    /// connects Canvas (or shows how) in a window of its own.</summary>
    static CanvasStatusModel? DueStatus()
    {
        if (canvasWatch?.State is not { } s || s.Status is "connected" or "syncing" or "") return null;
        var card = library.Overview?.CanvasStatus ?? new CanvasStatusModel(Canvas()) { OnConnect = ShowCanvasConnect, OnShowMeHow = ShowCanvasConnect };
        card.Show(s);
        return card;
    }

    /// <summary>Due's page (if it's showing) says what Canvas needs now.</summary>
    static void UpdateDueStatus()
    {
        if (library.Overview is { IsDuePage: true } due) due.CanvasStatus = DueStatus();
    }

    /// <summary>The dropdown's "Next due" line, from the last answer about what's due.</summary>
    static void UpdateNextDue()
    {
        var ctx = Canvas();
        panel.NextDue = CanvasQuick.NextDue(canvasDue, ctx.Clock.Zone, ctx.Clock.Now(), item => OpenDueItem(item.Class, item.Id));
    }

    /// <summary>The sidebar's Due: there once a class is linked to Canvas, counting what's still to hand in.</summary>
    static void UpdateDueItem()
    {
        var item = library.Classes.FirstOrDefault(c => c.IsDue);
        if (canvasDue is null || !CanvasLinked)
        {
            if (item is not null) library.Classes.Remove(item);
            return;
        }
        if (item is null) library.Classes.Insert(0, new ClassItem { Name = "Due", IsDue = true, Count = canvasDue.ToHandIn, Selected = pageState.Overview == DueHome });
        else item.Count = canvasDue.ToHandIn;
    }

    /// <summary>Opens the library at one assignment (the dropdown's next due, a quick panel row, a notification).</summary>
    static void OpenDueItem(string cls, string id)
    {
        Remember();
        panelWindow?.Hide();
        quickWindow?.Hide();
        pageState = pageState.Left() with { Assignment = (cls, id) };
        ShowLibrary();
    }

    /// <summary>A notifications poll is out: the minute's timer and a sync's update never ask at once (both would
    /// show the same news).</summary>
    static bool canvasNotifyPolling;

    static async Task PollCanvasNotificationsAsync()
    {
        if (quitting || !canvasWatching || canvasNotifyPolling || canvasNotifier is not { } notifier) return;
        IReadOnlyList<CanvasToastModel> toasts;
        canvasNotifyPolling = true;
        try
        {
            toasts = await notifier.PollAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return;
        }
        finally
        {
            canvasNotifyPolling = false;
        }
        // Newest first from the notifier; shown oldest first, so the newest lands on top of the stack.
        foreach (var toast in toasts.Reverse()) CanvasToast(toast);
    }

    /// <summary>The announcement a notification asked for, opened as soon as its class's page has loaded.</summary>
    static long? announcementToOpen;

    /// <summary>What a notification opens: its assignment on the Due page, its announcement in its class, or Due.</summary>
    static void OpenCanvasNotification(CanvasApi.NotificationRow n)
    {
        if (n.Class is { Length: > 0 } cls && n.AssignmentId is { } a)
        {
            OpenDueItem(cls, a.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }
        if (n.Class is { Length: > 0 } c && CanvasClassRow(c) is not null)
        {
            // An announcement opens in its class's reader, once the class's Canvas lists have loaded.
            Remember();
            announcementToOpen = n.AnnouncementId;
            pageState = pageState.Left() with { CanvasClass = c };
            canvasTab = ClassTab.Announcements;
            ShowLibrary();
            return;
        }
        Remember();
        pageState = pageState.Left() with { Overview = DueHome };
        ShowLibrary();
    }

    /// <summary>A Canvas notification in the same stack as the app's own (three at most on screen: a fourth folds
    /// the oldest away). Its words are the library's own; Open, Later/Dismiss and the × mark it seen.</summary>
    static void CanvasToast(CanvasToastModel toast) => Notify(new Notice
    {
        Title = toast.Title, Text = toast.Text, ActionLabel = "Open", Model = toast,
        Key = "canvas:" + toast.Item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        // The freshest of a batch, with its buttons, stays longer than the ones folded under it.
        Stay = TimeSpan.FromSeconds(toast.Expanded ? 12 : 7),
    });

    /// <summary>Connecting Canvas, in a small window of its own (from Settings' or the Due page's status card).</summary>
    public static void ShowCanvasConnect()
    {
        if (canvasConnectWindow is { IsVisible: true })
        {
            AppWindows.Raise(canvasConnectWindow);
            return;
        }
        var watch = CanvasPoll();
        var model = canvasModels.ForWindow(new CanvasConnectExits(Skip: CloseCanvasConnect, Back: CloseCanvasConnect, Finish: () =>
        {
            CloseCanvasConnect();
            Run(() => CanvasSyncedAsync(), "CanvasSynced");
        }));
        Control view = Skin.Current == SkinKind.Mac ? new MacCanvasConnect { DataContext = model } : new WinCanvasConnect { DataContext = model };
        // The course list takes the room the steps leave above the footer.
        if (view.FindControl<ScrollViewer>("Page") is { } page) PickerRoom.Follow(page);
        // The same title bar as the library and Settings: drag it by it, the window buttons sit in it.
        var header = new WindowHeader { Title = "Connect Canvas" };
        DockPanel.SetDock(header, Dock.Top);
        // Windows: the header on the window's Mica, the steps on a content layer under it (as setup's are).
        var body = new Border { Padding = new Avalonia.Thickness(32, 12, 32, 24), Child = view };
        if (Skin.Current == SkinKind.Win)
        {
            body.Bind(Border.BackgroundProperty, body.GetResourceObservable("Layer"));
            body.Bind(Border.BorderBrushProperty, body.GetResourceObservable("LayerStroke"));
            body.BorderThickness = new Avalonia.Thickness(0, 1, 0, 0);
        }
        var spec = new WindowSpec("canvas-connect", "Canvas", new DockPanel { Children = { header, body } })
        {
            Kind = WindowKind.Main, Size = WindowSize.Centred, Centred = new Avalonia.Size(640, 680),
        };
        AppWindows.Open(spec, w =>
        {
            if (Skin.Current == SkinKind.Mac) w.Bind(Window.BackgroundProperty, w.GetResourceObservable("Win"));
            w.Closed += (_, _) =>
            {
                if (canvasConnectWindow == w) canvasConnectWindow = null;
                model.Dispose();
            };
            canvasConnectWindow = w;
        });
        Run(() => StartConnectAsync(model, watch), "StartConnect");
    }

    /// <summary>Connect Canvas's own window closes (its Skip for now, Back and Finish).</summary>
    static void CloseCanvasConnect() => canvasConnectWindow?.Close();

    static async Task StartConnectAsync(CanvasConnectModel model, CanvasWatch watch)
    {
        try
        {
            await watch.RefreshAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
        }
        await LoadCanvasAsync();
        await model.StartAsync(watch.State ?? new CanvasApi.State(), canvasClasses);
    }
}
