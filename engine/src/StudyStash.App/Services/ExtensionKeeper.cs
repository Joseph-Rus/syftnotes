using System.Security.Cryptography;
using System.Text;
using StudyStash.Core.Canvas;

namespace StudyStash.App.Services;

/// <summary>
/// Keeps the Chrome extension's folder on this computer ready by itself, so the student never has to make it: once
/// the app is paired with a library that has a Canvas address, the folder points at that library (as this computer
/// reaches it) and that Canvas, and it's written again when either changes or Study Stash updates. Also says what
/// the library last heard from Chrome, so a screen can show "Add to Chrome" and then "Connected". Nothing here
/// touches the UI thread.
/// </summary>
public sealed class ExtensionKeeper(string home, Func<CanvasClient?> client, Action<string>? log = null)
{
    /// <summary>What the folder was last written for (the library's address, the key, the Canvas address and this
    /// engine's extension version, hashed so the key isn't kept here): the same again means nothing to do.</summary>
    string keptFor = "";

    /// <summary>The folder Chrome loads the extension from on this computer ("Load unpacked").</summary>
    public string LocalFolder => Extension.Folder(home);

    /// <summary>The folder is there, complete, and points at the paired library and its Canvas.</summary>
    public bool FolderReady { get; private set; }

    /// <summary>A Chrome has been checking in with the library just now (false for a library from before it said).</summary>
    public bool Connected { get; private set; }

    /// <summary>Which Chrome the library last heard from: "this_computer" (the library's own), "another_computer",
    /// or "" (none yet, or a library from before it said).</summary>
    public string SeenWhere { get; private set; } = "";

    /// <summary>Raised when <see cref="FolderReady"/>, <see cref="Connected"/> or <see cref="SeenWhere"/> changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Asks the library about its extension and brings the folder up to date when what it points at has changed.
    /// Does nothing without a library (or a library too old to say). Never throws for a library that didn't answer
    /// or a folder that couldn't be written: the next call tries again. True when the folder was written.
    /// </summary>
    public async Task<bool> KeepAsync(CancellationToken stop = default)
    {
        if (home.Length == 0 || client() is not { } c) return false;
        CanvasApi.ExtensionKey? info;
        try
        {
            info = await c.ExtensionAsync(stop);
        }
        catch (Exception e) when (e is HttpRequestException or CanvasLibraryException or System.Text.Json.JsonException
                                      || (e is TaskCanceledException && !stop.IsCancellationRequested))
        {
            return false; // the library didn't answer this time: keep what's known
        }
        if (info is null) return false;

        bool wrote = false, ready;
        string folder = LocalFolder;
        if (info.Key.Length == 0 || info.Canvas.Length == 0) ready = false; // nothing to point Chrome at yet
        else if (SameFolder(info.Folder, folder)) ready = info.FolderReady; // the library is on this computer and keeps this very folder
        else
        {
            string wanted = Hash(c.ServerUrl, info.Key, info.Canvas);
            if (wanted == keptFor && Extension.Ready(folder)) ready = true;
            else
            {
                try
                {
                    wrote = Extension.Ensure(folder, c.ServerUrl, info.Key, info.Canvas).Changed;
                    keptFor = wanted;
                    if (wrote) log?.Invoke($"[canvas] the Chrome extension's folder on this computer is ready (version {Extension.Version()}): {folder}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    keptFor = "";
                    log?.Invoke($"[canvas] couldn't write the Chrome extension's folder: {e.Message}");
                }
                ready = Extension.Ready(folder) && keptFor == wanted;
            }
        }

        bool changed = ready != FolderReady || info.Connected != Connected || info.SeenWhere != SeenWhere;
        FolderReady = ready;
        Connected = info.Connected;
        SeenWhere = info.SeenWhere;
        if (changed) Changed?.Invoke();
        return wrote;
    }

    static bool SameFolder(string a, string b) =>
        a.Length > 0 && string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    static string Hash(string library, string key, string canvas) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', library, key, canvas, Extension.Version()))));
}
