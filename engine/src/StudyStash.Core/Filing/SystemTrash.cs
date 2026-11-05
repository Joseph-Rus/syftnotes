using System.Runtime.InteropServices;
using System.Text;

namespace StudyStash.Core.Filing;

/// <summary>The system's Trash (the Recycle Bin on Windows). Submit &amp; Organize's Clean sends files there, never deletes
/// them; tests give a pretend one.</summary>
public interface ITrash
{
    /// <summary>Sends a file to the Trash. Where it is now in the Trash ("" when the system doesn't say), or null when
    /// it couldn't go (this disk has no Trash, or the system said no): then nothing happened to it.</summary>
    string? Send(string path);

    /// <summary>Puts a file back from the Trash (<paramref name="trashed"/>, where <see cref="Send"/> said it went) at
    /// <paramref name="to"/>. False when it can't (it isn't there any more, or where it went was never known): the
    /// student puts it back from the Trash themselves.</summary>
    bool Restore(string trashed, string to);
}

/// <summary>
/// The Trash of the computer this runs on. A Mac: NSFileManager's trashItemAtURL, which says where the file went, so
/// it's put back by moving it from there. Windows: the shell's delete with undo (the Recycle Bin), only on a fixed disk
/// that has a Recycle Bin, and asking rather than deleting for good should the Recycle Bin refuse it; the Recycle Bin's
/// copy ($R…, beside the $I… record of where it came from) is found afterwards, and put back by moving it. Anywhere
/// else (Linux, a network disk), null: Clean puts the file in the assignment's Materials folder instead.
/// </summary>
public sealed class SystemTrash : ITrash
{
    public string? Send(string path)
    {
        try
        {
            return OperatingSystem.IsMacOS() ? MacSend(Path.GetFullPath(path))
                : OperatingSystem.IsWindows() ? WinSend(Path.GetFullPath(path))
                : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool Restore(string trashed, string to)
    {
        if (trashed.Length == 0 || !File.Exists(trashed)) return false;
        File.Move(trashed, to, overwrite: false);
        if (OperatingSystem.IsWindows())
        {
            // The Recycle Bin's record of where it came from goes too, so the Recycle Bin doesn't list a file that's gone.
            string info = Path.Combine(Py.Parent(trashed), "$I" + Path.GetFileName(trashed)[2..]);
            try
            {
                File.Delete(info);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The file is back; a record left behind lists it in the Recycle Bin, where it can't be restored again.
            }
        }
        return true;
    }

    // --- a Mac ------------------------------------------------------------------------------------------------------

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

    // --- Windows ----------------------------------------------------------------------------------------------------

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

    static string? WinSend(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType != DriveType.Fixed || !Directory.Exists(Path.Combine(root, "$Recycle.Bin")))
            return null;
        var op = new FileOperation
        {
            Window = IntPtr.Zero,
            Func = FoDelete,
            From = path + "\0\0",
            To = null,
            Aborted = 0,
            NameMappings = IntPtr.Zero,
            Title = null,
            // Silent, but should the Recycle Bin refuse the file (too big for it), Windows asks before deleting it for good.
            Flags = FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi | FofWantNukeWarning,
        };
        if (SHFileOperation(ref op) != 0 || op.Aborted != 0 || File.Exists(path)) return null;
        return InRecycleBin(root, path) ?? "";
    }

    /// <summary>The Recycle Bin's copy of <paramref name="original"/> (the newest, if it went there more than once):
    /// the $R… file beside the $I… record that names it.</summary>
    static string? InRecycleBin(string root, string original)
    {
        string? best = null;
        var bestAt = DateTime.MinValue;
        IEnumerable<string> bins;
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
