using System.Runtime.InteropServices;
using System.Text;

namespace StudyStash.Audio;

/// <summary>File names as the sherpa-onnx library takes them.</summary>
static class NativePath
{
    /// <summary>sherpa-onnx takes file names as ANSI text on Windows, so a folder with a name the code page can't
    /// spell (a user called Zoë on some computers) would not open: its short name, all plain letters, does.</summary>
    public static string Readable(string path)
    {
        if (!OperatingSystem.IsWindows() || path.All(c => c < 128)) return path;
        var buffer = new StringBuilder(520);
        if (GetShortPathName(path, buffer, buffer.Capacity) > 0 && buffer.ToString().All(c => c < 128)) return buffer.ToString();
        throw new IOException($"Study Stash can't open the model in {path}: the folder's name has letters Windows can't pass on. Move Study Stash's folder to one with a plain name.");
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    static extern int GetShortPathName(string path, StringBuilder shortPath, int length);
}
