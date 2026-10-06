using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StudyStash.App.Platform;

/// <summary>
/// A Mac's Notification Center (UNUserNotificationCenter), spoken to straight from C#. macOS ties notifications, and
/// the student's choice to allow them, to an app's bundle and signature, so only the app itself has one: a build run
/// from its folder gets null from <see cref="Make"/> and shows the app's own cards instead. The first notification
/// asks macOS to allow them (its own question, once); what's said before the answer waits for it, and a "Don't Allow"
/// means macOS shows none, which is the student's choice to make in System Settings → Notifications.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed unsafe class MacNotifications : ISystemNotifications
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
    [DllImport(ObjC)] static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(ObjC)] static extern bool class_addMethod(IntPtr cls, IntPtr sel, delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void> imp, string types);
    [DllImport(ObjC)] static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr r, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendId(IntPtr r, IntPtr sel, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendText(IntPtr r, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendVoid(IntPtr r, IntPtr sel, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendVoid2(IntPtr r, IntPtr sel, IntPtr a, IntPtr b);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendRequest(IntPtr r, IntPtr sel, IntPtr id, IntPtr content, IntPtr trigger);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendAction(IntPtr r, IntPtr sel, IntPtr id, IntPtr title, nuint options);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendCategory(IntPtr r, IntPtr sel, IntPtr id, IntPtr actions, IntPtr intents, nuint options);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendAsk(IntPtr r, IntPtr sel, nuint options, Block* answer);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendBlock(IntPtr r, IntPtr sel, Block* block);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendGetLong(IntPtr r, IntPtr sel);

    [StructLayout(LayoutKind.Sequential)]
    struct BlockDescriptor
    {
        public nuint Reserved, Size;
    }

    /// <summary>An Objective-C block, as far as its function: how macOS hands over, and takes, a callback.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct Block
    {
        public IntPtr Isa;
        public int Flags, Reserved;
        public IntPtr Invoke;
        public BlockDescriptor* Descriptor;
    }

    const int BlockIsGlobal = 1 << 28;
    // UserNotifications' own numbers.
    const nuint AllowAlerts = 1 << 2, PresentInList = 1 << 3, PresentAsBanner = 1 << 4, ActionOpensApp = 1 << 2, TellWhenCleared = 1 << 0;
    const string ClickedIt = "com.apple.UNNotificationDefaultActionIdentifier", ClearedIt = "com.apple.UNNotificationDismissActionIdentifier";

    /// <summary>The one there is (its callbacks from macOS are static).</summary>
    static MacNotifications? made;
    static Block* answerBlock, settingsBlock;

    readonly IntPtr center;
    readonly Lock gate = new();
    readonly List<Action> waiting = [];
    /// <summary>Taken down while still waiting for macOS's answer: not shown when it comes.</summary>
    readonly HashSet<string> withdrawn = [];
    readonly Dictionary<string, IntPtr> categories = [];
    /// <summary>0: not asked yet; 1: macOS is asking the student; 2: answered.</summary>
    int asked;

    /// <summary>Whether macOS lets Study Stash show notifications: null until it has said.</summary>
    public bool? Allowed { get; private set; }
    /// <summary>What macOS said when it wouldn't even ask (its own words, for the log); null otherwise. It refuses an
    /// app run from a temporary folder or a disk image, and one that isn't properly signed.</summary>
    public string? Refusal { get; private set; }

    /// <summary>What macOS's own settings say after a no (UNAuthorizationStatus: 0 never decided, 1 denied, 2
    /// authorized): denied is the student's choice; anything else is macOS refusing this copy of the app.</summary>
    public int? Status { get; private set; }

    public NotificationState State => Allowed switch
    {
        null => NotificationState.Unknown,
        true => NotificationState.Allowed,
        false => Status == 1 ? NotificationState.Denied : NotificationState.Unavailable,
    };

    public event Action? StateChanged;
    public event Action<string, string>? Responded;

    static IntPtr Sel(string name) => sel_registerName(name);

    static IntPtr Str(string text) => SendText(objc_getClass("NSString"), Sel("stringWithUTF8String:"), text);

    static string? Text(IntPtr nsString) => nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    /// <summary>This app's Notification Center, or null where there isn't one: not a Mac, or not running as an app
    /// bundle (a build folder, a test), where asking for one would end the program.</summary>
    public static MacNotifications? Make()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        if (made is not null) return made;
        try
        {
            IntPtr bundle = Send(objc_getClass("NSBundle"), Sel("mainBundle"));
            if (bundle == IntPtr.Zero || Send(bundle, Sel("bundleIdentifier")) == IntPtr.Zero) return null;
            if (Text(Send(bundle, Sel("bundlePath"))) is not { } path || !path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return null;
            NativeLibrary.Load("/System/Library/Frameworks/UserNotifications.framework/UserNotifications");
            IntPtr type = objc_getClass("UNUserNotificationCenter");
            IntPtr center = type == IntPtr.Zero ? IntPtr.Zero : Send(type, Sel("currentNotificationCenter"));
            return center == IntPtr.Zero ? null : made = new MacNotifications(center);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    MacNotifications(IntPtr center)
    {
        this.center = center;
        // macOS tells this object what the student did with a notification, and asks it whether to show one while
        // Study Stash is the app in front.
        IntPtr type = objc_allocateClassPair(objc_getClass("NSObject"), "StudyStashNotificationDelegate", 0);
        class_addMethod(type, Sel("userNotificationCenter:willPresentNotification:withCompletionHandler:"), &WillPresent, "v@:@@@?");
        class_addMethod(type, Sel("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"), &DidReceive, "v@:@@@?");
        objc_registerClassPair(type);
        SendVoid(center, Sel("setDelegate:"), Send(Send(type, Sel("alloc")), Sel("init")));
    }

    /// <summary>Study Stash is in front: macOS shows the notification all the same, as a banner and in its list.</summary>
    [UnmanagedCallersOnly]
    static void WillPresent(IntPtr self, IntPtr cmd, IntPtr center, IntPtr notification, IntPtr completion)
    {
        if (completion != IntPtr.Zero) ((delegate* unmanaged<IntPtr, nuint, void>)((Block*)completion)->Invoke)(completion, PresentAsBanner | PresentInList);
    }

    /// <summary>The student clicked a notification, pressed one of its buttons, or cleared it.</summary>
    [UnmanagedCallersOnly]
    static void DidReceive(IntPtr self, IntPtr cmd, IntPtr center, IntPtr response, IntPtr completion)
    {
#pragma warning disable CA1031 // nothing thrown here may reach macOS: it would end the app
        try
        {
            string action = Text(Send(response, Sel("actionIdentifier"))) ?? "";
            string id = Text(Send(Send(Send(response, Sel("notification")), Sel("request")), Sel("identifier"))) ?? "";
            made?.Responded?.Invoke(id, action switch
            {
                ClickedIt => NotificationResponse.Clicked,
                ClearedIt => NotificationResponse.Dismissed,
                _ => action,
            });
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
        if (completion != IntPtr.Zero) ((delegate* unmanaged<IntPtr, void>)((Block*)completion)->Invoke)(completion);
    }

    [UnmanagedCallersOnly]
    static void OnAnswer(Block* block, byte granted, IntPtr error)
    {
#pragma warning disable CA1031 // nothing thrown here may reach macOS: it would end the app
        try
        {
            made?.Answered(granted != 0, error == IntPtr.Zero ? null : Text(Send(error, Sel("localizedDescription"))));
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    void Answered(bool granted, string? refusal)
    {
        if (granted)
        {
            Settle(true, refusal, null);
            return;
        }
        // A no: was it the student's (denied in macOS's settings), or macOS refusing to have this copy at all?
        lock (gate)
        {
            Refusal = refusal;
            settingsBlock = settingsBlock is null ? MakeBlock((IntPtr)(delegate* unmanaged<Block*, IntPtr, void>)&OnSettings) : settingsBlock;
        }
        SendBlock(center, Sel("getNotificationSettingsWithCompletionHandler:"), settingsBlock);
    }

    [UnmanagedCallersOnly]
    static void OnSettings(Block* block, IntPtr settings)
    {
#pragma warning disable CA1031 // nothing thrown here may reach macOS: it would end the app
        try
        {
            made?.Settle(false, made.Refusal, settings == IntPtr.Zero ? null : (int)SendGetLong(settings, Sel("authorizationStatus")));
        }
        catch (Exception)
        {
        }
#pragma warning restore CA1031
    }

    void Settle(bool granted, string? refusal, int? status)
    {
        List<Action> held;
        lock (gate)
        {
            Allowed = granted;
            Refusal = refusal;
            Status = status;
            asked = 2;
            held = [.. waiting];
            waiting.Clear();
        }
        StateChanged?.Invoke();
        // Said no, or refused: macOS would drop them anyway, and whoever asked hears of it from StateChanged.
        if (granted)
            foreach (var post in held) post();
    }

    static Block* MakeBlock(IntPtr invoke)
    {
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(Block);
        var block = (Block*)NativeMemory.AllocZeroed((nuint)sizeof(Block));
        block->Isa = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock");
        block->Flags = BlockIsGlobal;
        block->Invoke = invoke;
        block->Descriptor = descriptor;
        return block;
    }

    /// <summary>Runs <paramref name="post"/> once macOS has said whether notifications are allowed (asking it, the
    /// first time): one posted before the answer would be dropped.</summary>
    void WhenAnswered(Action post)
    {
        lock (gate)
        {
            if (asked != 2)
            {
                waiting.Add(post);
                if (asked != 0) return;
                asked = 1;
                if (answerBlock is null) answerBlock = MakeBlock((IntPtr)(delegate* unmanaged<Block*, byte, IntPtr, void>)&OnAnswer);
                SendAsk(center, Sel("requestAuthorizationWithOptions:completionHandler:"), AllowAlerts, answerBlock);
                return;
            }
        }
        post();
    }

    public void Show(string id, string title, string body, IReadOnlyList<NotificationButton> buttons)
    {
        lock (gate) withdrawn.Remove(id);
        WhenAnswered(() => Post(id, title, body, buttons));
    }

    void Post(string id, string title, string body, IReadOnlyList<NotificationButton> buttons)
    {
        lock (gate)
            if (withdrawn.Remove(id)) return;
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            // The words and the buttons: nothing else. No picture of Study Stash's, no sound.
            IntPtr content = Send(Send(objc_getClass("UNMutableNotificationContent"), Sel("alloc")), Sel("init"));
            SendVoid(content, Sel("setTitle:"), Str(title));
            SendVoid(content, Sel("setBody:"), Str(body));
            SendVoid(content, Sel("setCategoryIdentifier:"), Str(CategoryFor(buttons)));
            IntPtr request = SendRequest(objc_getClass("UNNotificationRequest"), Sel("requestWithIdentifier:content:trigger:"), Str(id), content, IntPtr.Zero);
            SendVoid2(center, Sel("addNotificationRequest:withCompletionHandler:"), request, IntPtr.Zero);
            Send(content, Sel("release"));
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>The set of buttons a notification has is a "category" macOS knows by name: made the first time those
    /// buttons are needed. Every one asks to be told when its notification is cleared.</summary>
    string CategoryFor(IReadOnlyList<NotificationButton> buttons)
    {
        string name = "study-stash:" + string.Join("|", buttons.Select(b => $"{b.Id}={b.Label}{(b.OpensApp ? "" : "~")}"));
        lock (categories)
        {
            if (categories.ContainsKey(name)) return name;
            IntPtr actions = Send(objc_getClass("NSMutableArray"), Sel("array"));
            foreach (var b in buttons)
                SendVoid(actions, Sel("addObject:"), SendAction(objc_getClass("UNNotificationAction"), Sel("actionWithIdentifier:title:options:"),
                    Str(b.Id), Str(b.Label), b.OpensApp ? ActionOpensApp : 0));
            IntPtr category = SendCategory(objc_getClass("UNNotificationCategory"), Sel("categoryWithIdentifier:actions:intentIdentifiers:options:"),
                Str(name), actions, Send(objc_getClass("NSArray"), Sel("array")), TellWhenCleared);
            categories[name] = Send(category, Sel("retain"));
            // macOS takes the whole set each time.
            IntPtr all = Send(objc_getClass("NSMutableSet"), Sel("set"));
            foreach (IntPtr c in categories.Values) SendVoid(all, Sel("addObject:"), c);
            SendVoid(center, Sel("setNotificationCategories:"), all);
        }
        return name;
    }

    public void Remove(string id)
    {
        lock (gate)
            if (asked != 2) withdrawn.Add(id);
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr ids = SendId(objc_getClass("NSArray"), Sel("arrayWithObject:"), Str(id));
            SendVoid(center, Sel("removePendingNotificationRequestsWithIdentifiers:"), ids);
            SendVoid(center, Sel("removeDeliveredNotificationsWithIdentifiers:"), ids);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }
}
