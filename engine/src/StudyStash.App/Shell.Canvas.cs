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
    static CanvasContext? canvas;
    /// <summary>The library (address and key) <see cref="canvas"/> was built for: a new one gets a new context.</summary>
    static string canvasFor = "";
    static CanvasWatch? canvasWatch;
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
    static CanvasContext Canvas()
    {
        var cc = host.Client();
        string key = cc.ServerUrl + "\n" + cc.PoolKey;
        if (canvas is not null && key == canvasFor) return canvas;
        if (canvasWatch is not null)
        {
            canvasWatch.Stop();
            canvasWatch.Changed -= OnCanvasChanged;
        }
        canvasWatching = false;
        canvasFor = key;
        var context = canvas = CanvasContext.For(host);
        canvasWatch = new CanvasWatch(context)
        {
            Extension = new ExtensionKeeper(host.Home, () => context.Client, host.Log),
            OnError = e => host.Log($"[canvas] asking the library about Canvas: {e.GetType().Name}: {e.Message}"),
        };
        canvasWatch.Changed += OnCanvasChanged;
        canvasFeed?.Dispose();
        canvasFeed = new CanvasFeed(context, canvasWatch);
        canvasFeed.Updated += () => Dispatcher.UIThread.Post(() => _ = CanvasUpdatedAsync());
        canvasNotifier = new CanvasNotifier(canvas) { OnOpen = OpenCanvasNotification };
        return canvas;
    }

    static CanvasWatch CanvasPoll()
    {
        Canvas();
        return canvasWatch!;
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
            canvasNotifyTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => _ = PollCanvasNotificationsAsync());
            canvasNotifyTimer.Start();
            _ = PollCanvasNotificationsAsync();
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
        library.Status = LibraryStatus();
        UpdateDueStatus();
    });

    /// <summary>Reads Canvas again now (Canvas's connect window finished), then shows it.</summary>
    static async Task CanvasSyncedAsync()
    {
        if (!await LoadCanvasAsync()) return;
        await CanvasUpdatedAsync();
    }

    /// <summary>Newer Canvas data is in (a sync finished, a course was linked or chosen): the sidebar's Due and its
    /// count, the dropdown's next due, Canvas's notifications, and the Due page or the open class's page, if the
    /// window shows one.</summary>
    static async Task CanvasUpdatedAsync()
    {
        if (quitting) return;
        UpdateNextDue();
        UpdateDueItem();
        // A sync's news (new work, a score, an announcement) shows now, not at the notifications' next minute.
        _ = PollCanvasNotificationsAsync();
        if (mainWindow?.IsVisible != true) return;
        if (dueOpen) await ShowDueAsync();
        else if (overviewOf is { } over) await ShowOverviewAsync(over);
        else if (library.List == LibraryList.CanvasClass && library.CanvasClass is { } cls && CanvasClassRow(openClass) is { } row)
            await cls.LoadAsync(row);
        // The open class was just linked (or dropped): it becomes its own Canvas page (or plain lectures again).
        else if (openClass is { } name && (CanvasClassRow(name) is not null) != (library.List == LibraryList.CanvasClass) && library.List != LibraryList.Due)
            await ShowClassAsync(name);
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
            ? $"Library running on this {(OperatingSystem.IsMacOS() ? "Mac" : "PC")}"
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
            status += writing == 1 ? " · Writing notes for 1 lecture" : $" · Writing notes for {writing} lectures";
        return status;
    }

    /// <summary>The Due page's card for when Canvas needs something: any state but connected or syncing. Its button
    /// connects Canvas (or shows how) in a window of its own.</summary>
    static void UpdateDueStatus()
    {
        if (canvasWatch?.State is { } s && s.Status is not ("connected" or "syncing" or ""))
        {
            var card = library.DueStatus ?? new CanvasStatusModel(Canvas()) { OnConnect = ShowCanvasConnect, OnShowMeHow = ShowCanvasConnect };
            card.Show(s);
            library.DueStatus = card;
        }
        else library.DueStatus = null;
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
        if (item is null) library.Classes.Insert(0, new ClassItem { Name = "Due", IsDue = true, Count = canvasDue.ToHandIn, Selected = dueOpen });
        else item.Count = canvasDue.ToHandIn;
    }

    /// <summary>Opens the library at one assignment on the Due page (the dropdown's next due, a quick panel row, a
    /// notification).</summary>
    static void OpenDueItem(string cls, string id)
    {
        Remember();
        panelWindow?.Hide();
        quickWindow?.Hide();
        dueSelection = (cls, id);
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
            // An announcement opens in its class's reader, once the class's page has loaded.
            announcementToOpen = n.AnnouncementId;
            openClass = c;
            overviewOf = null;
            dueOpen = false;
            dueSelection = null;
            ShowLibrary();
            return;
        }
        dueOpen = true;
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
            canvasConnectWindow.Activate();
            return;
        }
        var watch = CanvasPoll();
        var model = new CanvasConnectModel(Canvas(), watch) { ShowFooter = true, FinishLabel = "Finish", StepLabel = "" };
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
        var w = new Window
        {
            Title = "Canvas",
            ExtendClientAreaToDecorationsHint = true, ExtendClientAreaTitleBarHeightHint = Skin.Current == SkinKind.Mac ? WindowHeader.MacHeight : 32,
            Content = new DockPanel { Children = { header, body } },
        };
        if (Skin.Current == SkinKind.Mac) w.Bind(Window.BackgroundProperty, w.GetResourceObservable("Win"));
        OpenCentred(w, new Avalonia.Size(640, 680));
        Look.Apply(w);
        WinChrome.Apply(w);
        AppMenu.Attach(w, ShowLibrary, SettingsFromAnywhere);
        if (Skin.Current == SkinKind.Mac) MacTitleBar.Attach(w);
        model.Picker.OnSaved = () =>
        {
            LibraryClassesChanged();
            return Task.CompletedTask;
        };
        model.OnSkip = w.Close;
        model.OnBack = w.Close;
        model.OnFinish = () =>
        {
            w.Close();
            _ = CanvasSyncedAsync();
        };
        w.Closed += (_, _) =>
        {
            if (canvasConnectWindow == w) canvasConnectWindow = null;
            model.Dispose();
            UpdateDock();
        };
        canvasConnectWindow = w;
        w.Show();
        UpdateDock();
        w.Activate();
        Desktop.Activate();
        _ = StartConnectAsync(model, watch);
    }

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
