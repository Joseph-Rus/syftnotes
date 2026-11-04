namespace StudyStash.Core.Tests;

/// <summary>What still names the app's program from before it moved (Moves.cs), and what is left alone.</summary>
public class MovesTests
{
    [Fact]
    public void Only_our_program_in_an_old_place_or_gone_counts_as_moved()
    {
        string now = OperatingSystem.IsWindows() ? @"C:\Users\sam\AppData\Local\SyftNotes\current\StudyStash.exe" : "/Applications/Syft Notes.app/Contents/MacOS/StudyStash";
        bool Exists(string _) => true;
        // The old places, even while the old file is still there.
        Assert.True(Moves.OldProgram(@"C:\Users\sam\AppData\Local\Programs\Study Stash\StudyStash.exe", now, Exists));
        Assert.True(Moves.OldProgram("/Applications/Study Stash.app/Contents/MacOS/StudyStash", now, Exists));
        Assert.True(Moves.OldProgram("/home/sam/.local/share/syft-notes/StudyStash", now, Exists));
        // Ours by name and gone: a copy that was moved or deleted.
        Assert.True(Moves.OldProgram("/somewhere/gone/StudyStash", now, _ => false));
        // Left alone: this program, another copy that's still there (a build folder), and someone else's program.
        Assert.False(Moves.OldProgram(now, now, _ => false));
        Assert.False(Moves.OldProgram("/Users/sam/dev/bin/StudyStash", now, Exists));
        Assert.False(Moves.OldProgram("/usr/local/bin/node", now, _ => false));
        Assert.False(Moves.OldProgram(null, now));
    }

    [Fact]
    public void Reads_the_program_a_Run_value_a_LaunchAgent_and_a_desktop_entry_start()
    {
        Assert.Equal(@"C:\Program Files\Study Stash\StudyStash.exe", Moves.RunProgram(@"""C:\Program Files\Study Stash\StudyStash.exe"" --background"));
        Assert.Equal(@"C:\x\StudyStash.exe", Moves.RunProgram(@"C:\x\StudyStash.exe --background"));
        Assert.Equal("/Applications/Study Stash.app/Contents/MacOS/StudyStash", Moves.PlistProgram(
            "<key>Label</key><string>com.study-stash.app</string><key>ProgramArguments</key><array><string>/Applications/Study Stash.app/Contents/MacOS/StudyStash</string><string>--background</string></array>"));
        Assert.Equal("/home/sam/My \"Apps\"/StudyStash", Moves.ExecProgram("[Desktop Entry]\nExec=\"/home/sam/My \\\"Apps\\\"/StudyStash\" --background\n"));
        Assert.Equal("/home/sam/StudyStash", Moves.ExecProgram("Exec=/home/sam/StudyStash --background"));
    }
}
