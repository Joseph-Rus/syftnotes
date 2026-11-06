using System.Runtime.InteropServices;
using System.Text;

namespace StudyStash.Core.Filing;

/// <summary>The system's Trash (the Recycle Bin on Windows). Submit &amp; Organize's Clean sends files there, never deletes
/// them; tests give a pretend one.</summary>
public interface ITrash
{
    /// <summary>Sends a file to the Trash. Where it is now in the Trash ("" when the system doesn't say), or null when
    /// it couldn't go (this disk has no Trash, or the Trash wouldn't keep it): then nothing happened to it.</summary>
    string? Send(string path);

    /// <summary>Puts a file back from the Trash (<paramref name="trashed"/>, where <see cref="Send"/> said it went) at
    /// <paramref name="to"/>. False when it can't (it isn't there any more, or where it went was never known): the
    /// student puts it back from the Trash themselves.</summary>
    bool Restore(string trashed, string to);
}

/// <summary>
/// The Trash of the computer this runs on: a Mac's (NSFileManager's trashItemAtURL, which says where the file went, so
/// it's put back by moving it from there), or Windows' <see cref="RecycleBin"/>. Anywhere else (Linux), null: Clean puts
/// the file in the assignment's Materials folder instead.
/// </summary>
public sealed class SystemTrash : ITrash
{
    readonly RecycleBin? bin = OperatingSystem.IsWindows() ? new RecycleBin() : null;

    public string? Send(string path)
    {
        try
        {
            return bin is not null ? bin.Send(path) : OperatingSystem.IsMacOS() ? MacSend(Path.GetFullPath(path)) : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool Restore(string trashed, string to)
    {
        if (bin is not null) return bin.Restore(trashed, to);
        if (trashed.Length == 0 || !File.Exists(trashed)) return false;
        File.Move(trashed, to, overwrite: false);
        return true;
    }

    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr r, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr MsgId(IntPtr r, IntPtr sel, IntPtr a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr MsgText(IntPtr r, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern byte MsgTrash(IntPtr r, IntPtr sel, IntPtr url, out IntPtr result, out IntPtr error);

    static string? MacSend(string path)
    {
        NativeLibrary.TryLoad(Foundation, out _);
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            IntPtr text = MsgText(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), path);
            IntPtr url = text == IntPtr.Zero ? IntPtr.Zero : MsgId(objc_getClass("NSURL"), sel_registerName("fileURLWithPath:"), text);
            IntPtr files = Msg(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));
            if (url == IntPtr.Zero || files == IntPtr.Zero) return null;
            if (MsgTrash(files, sel_registerName("trashItemAtURL:resultingItemURL:error:"), url, out IntPtr result, out _) == 0) return null;
            IntPtr where = result == IntPtr.Zero ? IntPtr.Zero : Msg(result, sel_registerName("path"));
            IntPtr utf8 = where == IntPtr.Zero ? IntPtr.Zero : Msg(where, sel_registerName("UTF8String"));
            return utf8 == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(utf8) ?? "";
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }
}

/// <summary>What the Recycle Bin of a file's disk is set to: delete for good rather than keep (Recycle Bin properties'
/// "Don't move files to the Recycle Bin"), and the most it keeps, in bytes (null when it doesn't say).</summary>
public sealed record BinRules(bool DeleteForGood, long? MaxBytes);

/// <summary>
/// Windows' Recycle Bin. A file goes there only when the Recycle Bin of its disk would keep it: a fixed disk with a
/// Recycle Bin, whose settings (HKCU\…\Explorer\BitBucket\Volume\{volume}) can be read, that isn't set to delete for
/// good (by the student, or by a school's policy, NoRecycleFiles), and that keeps a file this big. Otherwise (or when the settings can't be read) nothing is done, and Clean puts
/// the file in the assignment's Materials folder. Sent with the shell's delete with undo, asking rather than deleting
/// for good should the Recycle Bin refuse it after all. The Recycle Bin's copy ($R…, beside the $I… record of where it
/// came from) is found afterwards, and put back by moving it.
/// </summary>
public sealed class RecycleBin : ITrash
{
    /// <summary>The Recycle Bin's settings for a file's disk, or null when it has none, or they can't be read. Tests
    /// give their own.</summary>
    internal Func<string, BinRules?> Rules { get; init; } = ReadRules;

    /// <summary>A policy (a school's, on a laptop it manages) switches the Recycle Bin off: files are deleted for good.
    /// Tests give their own.</summary>
    internal Func<bool> SwitchedOff { get; init; } = PolicySaysNo;

    /// <summary>Sends a file to the Recycle Bin; false when the shell didn't (tests give their own).</summary>
    internal Func<string, bool> Delete { get; init; } = ShellDelete;

    /// <summary>Where a file sent is in the Recycle Bin, or null when it can't be found (tests give their own).</summary>
    internal Func<string, string?> Find { get; init; } = InRecycleBin;

    /// <summary>The Recycle Bin would keep a file of <paramref name="size"/> bytes under <paramref name="rules"/>.</summary>
    public static bool Keeps(BinRules? rules, long size) => rules is { DeleteForGood: false } && (rules.MaxBytes is not long most || size <= most);

    public string? Send(string path)
    {
        string full = Path.GetFullPath(path);
        var rules = Rules(full);
        if (rules is not null && SwitchedOff()) rules = rules with { DeleteForGood = true };
        if (!Keeps(rules, new FileInfo(full).Length)) return null;
        if (!Delete(full) || File.Exists(full)) return null;
        return Find(full) ?? "";
    }

    public bool Restore(string trashed, string to)
    {
        if (trashed.Length == 0 || !File.Exists(trashed)) return false;
        File.Move(trashed, to, overwrite: false);
        // The Recycle Bin's record of where it came from goes too, so the Recycle Bin doesn't list a file that's gone.
        string name = Path.GetFileName(trashed);
        if (!name.StartsWith("$R", StringComparison.Ordinal)) return true;
        try
        {
            File.Delete(Path.Combine(Py.Parent(trashed), "$I" + name[2..]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The file is back; a record left behind lists it in the Recycle Bin, where it can't be restored again.
        }
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true)]
    static extern bool VolumeName(string mountPoint, [Out] char[] name, int length);

    const string BitBucket = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\";

    const string Policies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    /// <summary>NoRecycleFiles is 1 for this person or for the whole computer; not knowing counts as switched off.</summary>
    static bool PolicySaysNo()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(Policies);
                if (key?.GetValue("NoRecycleFiles") is int off && off != 0) return true;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    static BinRules? ReadRules(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType != DriveType.Fixed || !Directory.Exists(Path.Combine(root, "$Recycle.Bin")))
                return null;
            // "\\?\Volume{guid}\" names the disk; its Recycle Bin settings are under that {guid}.
            var name = new char[64];
            if (!VolumeName(root, name, name.Length)) return null;
            string volume = new string(name).Split('\0')[0];
            int open = volume.IndexOf('{'), close = volume.IndexOf('}');
            if (open < 0 || close < open) return null;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(BitBucket + volume[open..(close + 1)]);
            if (key is null) return null;
            return new BinRules(key.GetValue("NukeOnDelete") is int nuke && nuke != 0,
                key.GetValue("MaxCapacity") is int mb ? mb * 1024L * 1024L : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException
                                      or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileOperation
    {
        public IntPtr Window;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        public int Aborted;
        public IntPtr NameMappings;
        public string? Title;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    static extern int SHFileOperation(ref FileOperation op);

    const uint FoDelete = 3;
    const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400, FofWantNukeWarning = 0x4000;

    static bool ShellDelete(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var op = new FileOperation
        {
            Window = IntPtr.Zero,
            Func = FoDelete,
            From = path + "\0\0",
            To = null,
            Aborted = 0,
            NameMappings = IntPtr.Zero,
            Title = null,
            // Silent; but should the Recycle Bin refuse the file after all, Windows asks before deleting it for good.
            Flags = FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi | FofWantNukeWarning,
        };
        return SHFileOperation(ref op) == 0 && op.Aborted == 0;
    }

    /// <summary>The Recycle Bin's copy of <paramref name="original"/> (the newest, if it went there more than once):
    /// the $R… file beside the $I… record that names it.</summary>
    static string? InRecycleBin(string original)
    {
        string? root = Path.GetPathRoot(original);
        if (string.IsNullOrEmpty(root)) return null;
        string? best = null;
        var bestAt = DateTime.MinValue;
        List<string> bins;
        try
        {
            bins = Directory.EnumerateDirectories(Path.Combine(root, "$Recycle.Bin")).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        foreach (string bin in bins)
        {
            List<string> records;
            try
            {
                records = Directory.EnumerateFiles(bin, "$I*").ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue; // another person's Recycle Bin
            }
            foreach (string info in records)
            {
                if (Record(info) is not { } r || !r.Path.Equals(original, StringComparison.OrdinalIgnoreCase) || r.At < bestAt) continue;
                string copy = Path.Combine(bin, "$R" + Path.GetFileName(info)[2..]);
                if (!File.Exists(copy)) continue;
                best = copy;
                bestAt = r.At;
            }
        }
        return best;
    }

    /// <summary>What a $I… record says: where the file came from and when it was deleted. Version 2 (Windows 10 on)
    /// keeps the path's length at 24 and the path from 28; version 1 a fixed 260 characters from 24.</summary>
    static (string Path, DateTime At)? Record(string file)
    {
        try
        {
            byte[] b = File.ReadAllBytes(file);
            if (b.Length < 28) return null;
            long version = BitConverter.ToInt64(b, 0);
            var at = DateTime.FromFileTimeUtc(BitConverter.ToInt64(b, 16));
            string path = version >= 2
                ? Encoding.Unicode.GetString(b, 28, Math.Clamp(BitConverter.ToInt32(b, 24) * 2, 0, b.Length - 28))
                : Encoding.Unicode.GetString(b, 24, Math.Min(520, b.Length - 24));
            int end = path.IndexOf('\0');
            return (end >= 0 ? path[..end] : path, at);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
