using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace StudyStash.App.Platform;

/// <summary>
/// Windows' own notifications (the ones that slide in above the taskbar and stay in the notification centre),
/// spoken to straight from C# through Windows' notification interfaces (Windows.UI.Notifications). Windows shows them
/// its way: its look, the app's name and icon along the top, how long one stays, Focus and Do Not Disturb.
/// <para>An app that isn't a Store package gets them by saying who it is in the registry (its name and icon, under an
/// id of its own), which <see cref="Make"/> does for this account. A click on one, or on its button, opens a
/// <c>studystash:</c> link (<see cref="NoticeLinks"/>) that comes back to the running app as a word, so nothing has to
/// stay in memory for Windows to call. Windows doesn't say when one is cleared away.</para>
/// <para>Every call that can fail says so (<see cref="LastError"/>) instead of throwing: where Windows won't have
/// them, <see cref="Make"/> gives null and the app shows its own cards.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WinNotifications : ISystemNotifications
{
    /// <summary>Who Study Stash is to Windows' notifications.</summary>
    public const string AppId = "StudyStash.App";

    const string Group = "study-stash";

    [DllImport("combase.dll")] static extern int RoInitialize(int type);
    [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, uint length, out IntPtr text);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr text);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr type, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll")] static extern int RoActivateInstance(IntPtr type, out IntPtr instance);

    // Windows' own interfaces, as its metadata (Windows.Foundation.UniversalApiContract) gives them: each one's id,
    // and the place of each method after the six every such interface starts with.
    static Guid ManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");   // 7: CreateToastNotifier(applicationId)
    static Guid ManagerStatics2 = new("7AB93C52-0E48-4750-BA9D-1A4113981847");  // 6: get_History
    static Guid NotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53"); // 6: CreateToastNotification(content)
    static Guid Notification2 = new("9DFB9FD1-143A-490E-90BF-B9FBA7132DE7");    // 6: put_Tag, 8: put_Group
    static Guid XmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    static Guid XmlDocumentIO = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");    // 6: LoadXml(xml)
    // IToastNotifier: 6: Show(notification), 8: get_Setting. IToastNotificationHistory: 8: Remove(tag, group, applicationId).

    static WinNotifications? made;

    readonly string appId;
    readonly IntPtr notifier, factory, history;

    /// <summary>What went wrong last, and where ("showing: 0x803E0105"); null when nothing has.</summary>
    public static string? LastError { get; private set; }

    public event Action<string, string>? Responded;
#pragma warning disable CS0067 // Windows asks nobody's leave: what it shows is read fresh each time, never announced
    public event Action? StateChanged;
#pragma warning restore CS0067

    WinNotifications(string appId, IntPtr notifier, IntPtr factory, IntPtr history)
    {
        this.appId = appId;
        this.notifier = notifier;
        this.factory = factory;
        this.history = history;
    }

    static IntPtr Slot(IntPtr obj, int n) => (*(IntPtr**)obj)[n];

    static int Query(IntPtr obj, ref Guid iid, out IntPtr found)
    {
        found = IntPtr.Zero;
        fixed (Guid* id = &iid)
        fixed (IntPtr* result = &found)
            return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(obj, 0))(obj, id, result);
    }

    static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
    }

    /// <summary>A string as Windows' interfaces take one: freed when it's done with.</summary>
    readonly struct Text : IDisposable
    {
        public readonly IntPtr Handle;

        public Text(string value) => _ = WindowsCreateString(value, (uint)value.Length, out Handle);

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) _ = WindowsDeleteString(Handle);
        }
    }

    static bool Failed(int result, string doing)
    {
        if (result >= 0) return false;
        LastError = $"{doing}: 0x{result:X8}";
        return true;
    }

    /// <summary>Tells Windows who Study Stash is, for this account: its name and icon for the notifications. The same
    /// values every time, so doing it again changes nothing. (Setup.exe's uninstaller takes them out.)</summary>
    public static void RegisterApp(string appId, string name, string? icon)
    {
        using var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appId}");
        app.SetValue("DisplayName", name);
        if (icon is { Length: > 0 }) app.SetValue("IconUri", icon);
        else app.DeleteValue("IconUri", throwOnMissingValue: false);
    }

    /// <summary>Tells Windows that a <c>studystash:</c> link opens <paramref name="program"/>: how a click on a
    /// notification comes back.</summary>
    public static void RegisterLink(string name, string program)
    {
        using (var link = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{NoticeLinks.Scheme}"))
        {
            link.SetValue("", "URL:" + name);
            link.SetValue("URL Protocol", "");
        }
        using var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{NoticeLinks.Scheme}\shell\open\command");
        command.SetValue("", $"\"{program}\" --open \"%1\"");
    }

    /// <summary>Takes back what <see cref="RegisterApp"/> wrote (a test's own id; the app's stays until it's
    /// uninstalled).</summary>
    public static void UnregisterApp(string appId) =>
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{appId}", throwOnMissingSubKey: false);

    /// <summary>Windows' notifications for this app, or null where they can't be had (<see cref="LastError"/> says
    /// why). <paramref name="program"/> null leaves the link alone (a test, which must never take the real app's).</summary>
    public static WinNotifications? Make(string appId = AppId, string name = "Study Stash", string? icon = null, string? program = null)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return null;
        if (made is not null && made.appId == appId) return made;
        IntPtr statics = IntPtr.Zero, statics2 = IntPtr.Zero, notifier = IntPtr.Zero, factory = IntPtr.Zero, history = IntPtr.Zero;
        try
        {
            RegisterApp(appId, name, icon);
            if (program is not null) RegisterLink(name, program);
            _ = RoInitialize(1); // already done on this thread, in its own way, is fine too
            using var managerType = new Text("Windows.UI.Notifications.ToastNotificationManager");
            using var notificationType = new Text("Windows.UI.Notifications.ToastNotification");
            using var id = new Text(appId);
            if (Failed(RoGetActivationFactory(managerType.Handle, ref ManagerStatics, out statics), "finding Windows' notifications")) return null;
            if (Failed(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, 7))(statics, id.Handle, &notifier), "asking for a notifier")) return null;
            if (Failed(RoGetActivationFactory(notificationType.Handle, ref NotificationFactory, out factory), "finding how a notification is made")) return null;
            // The list of earlier ones, to take one down: without it they still show, they just can't be taken back.
            if (Query(statics, ref ManagerStatics2, out statics2) >= 0)
                _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(statics2, 6))(statics2, &history);
            made = new WinNotifications(appId, notifier, factory, history);
            notifier = factory = history = IntPtr.Zero;
            return made;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SecurityException or UnauthorizedAccessException or IOException)
        {
            LastError = "setting up: " + e.Message;
            return null;
        }
        finally
        {
            Release(statics);
            Release(statics2);
            Release(notifier);
            Release(factory);
            Release(history);
        }
    }

    /// <summary>Windows' own setting for this app: 0 is on; anything else is off, by the student (for this app, or for
    /// everything) or by whoever runs the computer.</summary>
    public NotificationState State
    {
        get
        {
            int setting = 0;
            return Failed(((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(notifier, 8))(notifier, &setting), "reading the setting")
                ? NotificationState.Unavailable
                : setting == 0 ? NotificationState.Allowed : NotificationState.Denied;
        }
    }

    public void Show(string id, string title, string body, IReadOnlyList<NotificationButton> buttons) => ShowXml(id, NoticeLinks.WindowsXml(id, title, body, buttons));

    /// <summary>Hands Windows one notification. False when it wouldn't take it (<see cref="LastError"/> says where).</summary>
    public bool ShowXml(string id, string xml)
    {
        IntPtr made = IntPtr.Zero, io = IntPtr.Zero, document = IntPtr.Zero, toast = IntPtr.Zero, named = IntPtr.Zero;
        try
        {
            using var documentType = new Text("Windows.Data.Xml.Dom.XmlDocument");
            using var content = new Text(xml);
            using var tag = new Text(id);
            using var group = new Text(Group);
            if (Failed(RoActivateInstance(documentType.Handle, out made), "making the notification's words")) return false;
            if (Failed(Query(made, ref XmlDocumentIO, out io), "making the notification's words")) return false;
            if (Failed(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(io, 6))(io, content.Handle), "reading the notification's words")) return false;
            if (Failed(Query(made, ref XmlDocument, out document), "reading the notification's words")) return false;
            if (Failed(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(factory, 6))(factory, document, &toast), "making the notification")) return false;
            // Its name with Windows: the same one said again takes the first one's place, and it can be taken down.
            if (Query(toast, ref Notification2, out named) >= 0)
            {
                _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(named, 6))(named, tag.Handle);
                _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(named, 8))(named, group.Handle);
            }
            return !Failed(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(notifier, 6))(notifier, toast), "showing");
        }
        finally
        {
            Release(named);
            Release(toast);
            Release(document);
            Release(io);
            Release(made);
        }
    }

    public void Remove(string id)
    {
        if (history == IntPtr.Zero) return;
        using var tag = new Text(id);
        using var group = new Text(Group);
        using var app = new Text(appId);
        _ = Failed(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, int>)Slot(history, 8))(history, tag.Handle, group.Handle, app.Handle), "taking one down");
    }

    /// <summary>A click came back (<see cref="NoticeLinks"/>): whoever listens hears which notification, and what was
    /// done with it.</summary>
    public static void Opened(string word)
    {
        if (NoticeLinks.Parse(word) is { } click) made?.Responded?.Invoke(click.Id, click.What);
    }
}
