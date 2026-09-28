using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core.Ai;

namespace StudyStash.App.Tests;

/// <summary>Pictures of the recorder's chat, in both looks: open with two questions (its close button at the top
/// right), and closed again, the transcript back and "Show the chat" above the field.</summary>
public class RecorderChatShots
{
    static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    static RecorderChatShots() => Environment.SetEnvironmentVariable("STUDYSTASH_STILL", "1");

    static RecorderModel Chatting(bool closed)
    {
        var r = Demo.Recorder(expanded: true);
        r.Ask!.Turns.Add(new AiTurn("And what about Big-O?", "Ollama")
        {
            Answer = "Big-O proofs won't be on it, but you should still say how long each call takes.",
            Byline = AiWords.AskByline("Ollama", [new AskSource("lec-recursion", "Recursion", null, null, 22 * 60 + 10, "")]),
        });
        if (closed) r.Ask.CloseChatCommand.Execute(null);
        return r;
    }

    static StackPanel Both(Func<RecorderModel, Control> view) => Shot.Side(view(Chatting(closed: false)), view(Chatting(closed: true)));

    [AvaloniaFact]
    public void Mac_recorder_chat()
    {
        foreach (var t in Themes)
            Shot.Take("mac-02-recorder-chat", SkinKind.Mac, t, () => Both(m => new MacRecorder { DataContext = m, VerticalAlignment = VerticalAlignment.Top }));
    }

    [AvaloniaFact]
    public void Win_recorder_chat()
    {
        foreach (var t in Themes)
            Shot.Take("win-02-recorder-chat", SkinKind.Win, t, () => Both(m => new WinRecorder { DataContext = m, VerticalAlignment = VerticalAlignment.Top }));
    }
}
