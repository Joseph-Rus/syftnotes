using Avalonia.Headless.XUnit;
using StudyStash.App.Platform;
using StudyStash.App.Services;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Login items as a test sees them: what they say, and every call to change them.</summary>
public sealed class CountingLoginItems(bool starts = false, bool refuse = false) : ILoginItems
{
    public List<bool> Calls { get; } = [];

    public bool StartsAtLogin(string home) => starts;

    public void StartAtLogin(bool on, string home)
    {
        Calls.Add(on);
        if (refuse) throw new InvalidOperationException("Start at login is off here.");
    }
}
