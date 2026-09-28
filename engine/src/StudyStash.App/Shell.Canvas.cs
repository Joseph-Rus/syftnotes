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
    static DateTimeOffset? canvasSynced;
    /// <summary>The library's last answers about Canvas: what's due, and every class with its course.</summary>
    static CanvasApi.DueResponse? canvasDue;
    static List<CanvasApi.ClassRow> canvasClasses = [];
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
        canvasWatch = new CanvasWatch(context) { Extension = new ExtensionKeeper(host.Home, () => context.Client, host.Log) };
        canvasWatch.Changed += OnCanvasChanged;
        canvasNotifier = new CanvasNotifier(canvas) { OnOpen = OpenCanvasNotification };
        canvasDue = null;
        canvasClasses = [];
        canvasSynced = null;
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

    /// <summary>The poll heard from the library: the status line follows it, and a finished sync brings in what's
    /// due (the sidebar's count, the dropdown's next due, the Due page and the open class).</summary>
    static void OnCanvasChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (quitting || canvasWatch?.State is not { } state) return;
        library.Status = LibraryStatus();
        UpdateDueStatus();
        if (canvasDue is not null && state.LastSync == canvasSynced) return;
        canvasSynced = state.LastSync;
        _ = CanvasSyncedAsync();
    });

    static async Task CanvasSyncedAsync()
    {
        if (!await LoadCanvasAsync()) return;
        UpdateNextDue();
        UpdateDueItem();
        if (mainWindow?.IsVisible != true) return;
        if (dueOpen) await ShowDueAsync();
        else if (library.List == LibraryList.CanvasClass && library.CanvasClass is { } cls && CanvasClassRow(openClass) is { } row)
            await cls.LoadAsync(row);
    }

    /// <summary>Asks the library what's due and which classes are linked. False when it couldn't (no library, an
    /// older one, or no answer), leaving what was known before.</summary>
    static async Task<bool> LoadCanvasAsync()
    {
        if (Canvas().Client is not { } client) return false;
        try
        {
            var dueTask = client.DueAsync();
            var classesTask = client.ClassesAsync();
            await Task.WhenAll(dueTask, classesTask);
            if (await dueTask is not { } due) return false;
            canvasDue = due;
            canvasClasses = await classesTask ?? [];
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
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
        panelWindow?.Hide();
        quickWindow?.Hide();
        dueSelection = (cls, id);
        ShowLibrary();
    }

    static async Task PollCanvasNotificationsAsync()
    {
        if (quitting || !canvasWatching || canvasNotifier is not { } notifier) return;
        IReadOnlyList<CanvasToastModel> toasts;
        try
        {
            toasts = await notifier.PollAsync();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CanvasLibraryException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return;
        }
        // Newest first from the notifier; shown oldest first, so the newest lands on top of the stack.
        foreach (var toast in toasts.Reverse()) CanvasToast(toast);
    }

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
            openClass = c;
            dueOpen = false;
            dueSelection = null;
            ShowLibrary();
            return;
        }
        dueOpen = true;
        ShowLibrary();
    }

    /// <summary>A Canvas notification in its own floating card, stacked with the app's other toasts (three at most on
    /// screen: a fourth pushes out the oldest).</summary>
    static void CanvasToast(CanvasToastModel toast)
    {
        if (quitting) return;
        toasts.RemoveAll(t => !t.Window.IsVisible);
        while (toasts.Count >= 3)
        {
            toasts[0].Window.Close();
            toasts.RemoveAt(0);
        }
        var view = ToastView.For(toast);
        var w = new Floating { Content = view, Title = toast.Title, ShowActivated = false };
        void Close() => Dispatcher.UIThread.Post(w.Close);
        toast.OpenCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommunityToolkit.Mvvm.Input.IAsyncRelayCommand.IsRunning) && !toast.OpenCommand.IsRunning) Close();
        };
        toast.DismissCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommunityToolkit.Mvvm.Input.IAsyncRelayCommand.IsRunning) && !toast.DismissCommand.IsRunning) Close();
        };
        w.Closed += (_, _) => toasts.RemoveAll(t => t.Window == w);
        var (_, scale) = w.WorkArea();
        var size = w.Measured(scale);
        int room = (int)(Floating.ShadowRoom * scale);
        w.Position = Placement.ToastSpot(w.ScreenList(), toasts.Count, size, OperatingSystem.IsMacOS(), room);
        toasts.Add((toast.Title, DateTime.UtcNow, w));
        w.Show();
        DispatcherTimer.RunOnce(() =>
        {
            if (w.IsVisible && !view.IsPointerOver) w.Close();
        }, TimeSpan.FromSeconds(toast.Expanded ? 12 : 7));
    }

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
