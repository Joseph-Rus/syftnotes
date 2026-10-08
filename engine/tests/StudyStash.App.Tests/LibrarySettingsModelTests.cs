using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.Core;

namespace StudyStash.App.Tests;

/// <summary>Settings → Your library: the library's own settings, read when a page opens and changed one at a time
/// through its API, so the laptop needs nothing else to change its library.</summary>
public class LibrarySettingsModelTests
{
    static (SettingsModel Model, AppHost Host, TempHome Home) Open(FakeLibrarySettings fake, AppRole role = AppRole.Laptop)
    {
        var home = new TempHome();
        new AppSettings { SetupDone = true, Role = role }.Save(home.Path);
        var cc = Configs.LoadClient(home.Path);
        cc.ServerUrl = "http://mac-mini:8787";
        cc.PoolKey = "pw";
        cc.PoolName = "Sam's library";
        Configs.SaveClient(cc);
        var host = new AppHost(home.Path, log: _ => { });
        return (SettingsModel.Make(host, library: () => fake.Call), host, home);
    }

    [AvaloniaFact]
    public void The_sidebar_keeps_this_laptop_and_your_library_apart()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;

        Assert.Equal("This laptop", model.ComputerNavTitle);
        Assert.Equal(["General", "Appearance", "Shortcuts", "Recording", "Calendars", "Connection"], model.ComputerNav.Select(n => n.Label));
        Assert.Equal(["Library", "Classes", "Notes and sorting", "Your AI", "AI apps", "Canvas", "Folders", "Phone"], model.LibraryNav.Select(n => n.Label));
        Assert.Empty(fake.Calls); // nothing asked before a library page opens
    }

    [AvaloniaFact]
    public void The_librarys_own_computer_has_no_recording_and_opens_on_its_library()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake, AppRole.Library);
        using var _h = home;
        using var _host = host;
        using var _m = model;

        Assert.Equal(["General", "Appearance", "Shortcuts", "Connection"], model.ComputerNav.Select(n => n.Label));
        Assert.True(model.OnLibrary);
        Assert.True(model.Lib.IsHere);
        Assert.True(model.Lib.IsReady);
    }

    [AvaloniaFact]
    public async Task Just_this_computers_library_hides_the_laptop_bits_until_a_laptop_is_added()
    {
        var fake = new FakeLibrarySettings();
        fake.Settings["reach"]!["laptops"] = false;
        var calls = new List<(bool On, string? Password)>();
        string? told = null;
        var lib = new LibrarySettingsModel(() => fake.Call)
        {
            IsHere = true,
            PasswordChanged = p => told = p,
            LetLaptopsConnect = (on, password) =>
            {
                calls.Add((on, password));
                fake.Settings["reach"]!["laptops"] = on;
                return Task.CompletedTask;
            },
        };
        await lib.Load();

        Assert.True(lib.OnlyThisComputer);
        Assert.False(lib.ShowReach); // no addresses or password: no laptop uses them
        Assert.True(lib.CanChangeLaptops);
        Assert.Equal("Add a laptop", lib.LaptopsTitle);
        Assert.StartsWith("What the app", lib.NameSub, StringComparison.Ordinal);

        lib.AddLaptopCommand.Execute(null);
        Assert.True(lib.AddingLaptop);
        lib.LaptopPassword = "abc";
        await lib.TurnOnLaptopsCommand.ExecuteAsync(null);
        Assert.Empty(calls);
        Assert.Equal("Use a password of at least 4 characters.", lib.LaptopSay);

        lib.LaptopPassword = "correct-horse";
        await lib.TurnOnLaptopsCommand.ExecuteAsync(null);

        Assert.Equal([(true, (string?)"correct-horse")], calls);
        Assert.Equal("correct-horse", told); // this computer connects with it from now on
        Assert.False(lib.AddingLaptop);
        Assert.True(lib.LaptopsCanConnect);
        Assert.True(lib.ShowReach);
        Assert.NotEmpty(lib.Addresses);
        Assert.Equal("Laptops can connect", lib.LaptopsTitle);
        Assert.Contains("This is my laptop", lib.LaptopSay, StringComparison.Ordinal);

        await lib.TurnOffLaptopsCommand.ExecuteAsync(null);
        Assert.Equal((false, (string?)null), calls[1]);
        Assert.True(lib.OnlyThisComputer);
    }

    [AvaloniaFact]
    public async Task Adding_a_laptop_that_fails_says_why_and_stays_open()
    {
        var fake = new FakeLibrarySettings();
        fake.Settings["reach"]!["laptops"] = false;
        var lib = new LibrarySettingsModel(() => fake.Call)
        {
            IsHere = true,
            LetLaptopsConnect = (_, _) => throw new InvalidOperationException("The library didn't start."),
        };
        await lib.Load();
        lib.AddLaptopCommand.Execute(null);
        lib.LaptopPassword = "correct-horse";

        await lib.TurnOnLaptopsCommand.ExecuteAsync(null);

        Assert.Equal("The library didn't start.", lib.LaptopSay);
        Assert.True(lib.AddingLaptop);
        Assert.True(lib.OnlyThisComputer);
        Assert.False(lib.ChangingLaptops);
    }

    [AvaloniaFact]
    public async Task A_library_that_doesnt_say_is_reachable_by_laptops_as_before()
    {
        var fake = new FakeLibrarySettings();
        ((JsonObject)fake.Settings["reach"]!).Remove("laptops");
        var lib = new LibrarySettingsModel(() => fake.Call);
        await lib.Load();
        Assert.True(lib.LaptopsCanConnect);
        Assert.True(lib.ShowReach);
        Assert.False(lib.CanChangeLaptops); // on another computer: nothing to open or close from here
    }

    [AvaloniaFact]
    public void Only_just_this_computer_can_open_or_close_its_library_to_laptops_and_its_AI_runs_here()
    {
        foreach (var (role, can) in new[] { (AppRole.Both, true), (AppRole.Library, false), (AppRole.Laptop, false) })
        {
            var (model, host, home) = Open(new FakeLibrarySettings(), role);
            using var _h = home;
            using var _host = host;
            using var _m = model;
            Assert.Equal(can, model.Lib.CanChangeLaptops);
            if (role == AppRole.Laptop) Assert.Contains("from this laptop", model.Engines.Lede, StringComparison.Ordinal);
            else Assert.Contains($"It runs on this {(OperatingSystem.IsWindows() ? "PC" : "Mac")}", model.Engines.Lede, StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public void Opening_a_library_page_reads_everything_and_changes_nothing()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;

        model.Section = "Library";

        Assert.Equal([("GET", "")], fake.Calls.Select(c => (c.Method, c.Path)));
        var lib = model.Lib;
        Assert.True(lib.IsReady);
        Assert.Equal("Sam's library", lib.Name);
        Assert.Equal(3, lib.Addresses.Count);
        Assert.False(lib.StartAtLoginOn);
        Assert.True(lib.CanStartAtLogin);
        Assert.Equal(["CS 101", "BIO 110", "HIST 210", "MATH 221"], lib.Classes.Select(c => c.Name));
        Assert.Equal("cs101, intro to cs", lib.Classes[0].Aliases);
        Assert.Equal("60% sure", lib.ConfidenceLabel);
        Assert.Equal("Same as the sorting model", lib.SummaryLabel);
        Assert.Equal("Ghostty", lib.TerminalLabel);
        Assert.Equal(2, lib.Folders.Count);
        Assert.Equal("Rewrite all 42 summaries", lib.RewriteTitle);
    }

    [AvaloniaFact]
    public async Task Each_change_is_sent_by_itself_as_its_made()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Notes";
        var lib = model.Lib;

        lib.WriteNotes = false;
        lib.ConfidenceChoices.Single(c => c.Label == "80% sure").Pick.Execute(null);
        lib.SortChoices.Single(c => c.Id == "qwen3:1.7b").Pick.Execute(null);
        Assert.Equal("Ollama (the library's main AI)", lib.SortEngineLabel);
        lib.SortEngineChoices.Single(c => c.Id == "claude").Pick.Execute(null);
        lib.StartAtLoginOn = true;
        lib.AutoUpdate = false;
        await Task.Yield();

        var sent = fake.Calls.Where(c => c.Method == "POST").Select(c => c.Body!.ToJsonString()).ToList();
        Assert.Equal(
        [
            """{"notes":{"write":false}}""",
            """{"notes":{"min_confidence":0.8}}""",
            """{"ollama":{"sort_model":"qwen3:1.7b"}}""",
            """{"sort_engine":"claude"}""",
            """{"start_at_login":true}""",
            """{"auto_update":false}""",
        ], sent);
        Assert.Equal("80% sure", lib.ConfidenceLabel);
        Assert.Equal("qwen3:1.7b", lib.SortLabel);
        Assert.Equal("Claude", lib.SortEngineLabel);
        Assert.Contains(lib.SortEngineChoices, c => c.Label == "ChatGPT (not installed)");
        Assert.True(lib.StartAtLoginOn);
    }

    [AvaloniaFact]
    public async Task A_new_name_and_password_keep_this_laptop_connected()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Library";
        var lib = model.Lib;

        lib.Name = "Lecture notes";
        await lib.SaveNameCommand.ExecuteAsync(null);
        Assert.Equal("Lecture notes", host.Client().PoolName);

        lib.ChangePasswordCommand.Execute(null);
        lib.NewPassword = "ab";
        await lib.SavePasswordCommand.ExecuteAsync(null);
        Assert.Null(fake.Password); // too short: never sent
        Assert.Contains("at least 4", lib.PasswordSay);

        lib.NewPassword = "correct horse";
        await lib.SavePasswordCommand.ExecuteAsync(null);
        Assert.Equal("correct horse", fake.Password);
        Assert.Equal("correct horse", host.Client().PoolKey);
        Assert.False(lib.ChangingPassword);
        Assert.Contains("other computers need the new password", lib.PasswordSay);
    }

    [AvaloniaFact]
    public async Task Classes_named_from_course_codes_are_previewed_then_renamed_and_the_lectures_here_follow()
    {
        var fake = new FakeLibrarySettings { Settings = FakeLibrarySettings.CodeNamed() };
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        host.Lectures.Add(new Lecture { Id = "rec-1", Started = "2026-09-22T10:00:00-07:00", State = LectureState.Sending, ClassName = "202710.TS.CSCI321.A" });
        model.Section = "Classes";
        var lib = model.Lib;
        int heard = 0;
        lib.ClassesChanged = () => heard++;

        Assert.True(lib.HasCourseNames);
        Assert.True(lib.CanUseCourseNames);
        Assert.Equal("2 classes are named from course codes. Their lectures move with them.", lib.CourseNamesSub);
        Assert.Equal("Rename 2 classes", lib.RenameLabel);
        Assert.False(lib.ConfirmingCourseNames);
        lib.AskUseCourseNamesCommand.Execute(null);
        Assert.True(lib.ConfirmingCourseNames); // the preview: each class, its new name, and what moves with it
        Assert.Equal([("Software Engineering", "Now 202710.TS.CSCI321.A", "18 lectures move with it"), ("Senior Design", "Now 202710.TS.ENGR401.A", "14 lectures move with it")],
            lib.CourseNames.Select(r => (r.To, r.Now, r.Meta)));
        Assert.DoesNotContain(fake.Calls, c => c.Path == "/course-names"); // nothing renamed until asked

        await lib.UseCourseNamesCommand.ExecuteAsync(null);

        Assert.Contains(fake.Calls, c => (c.Method, c.Path) == ("POST", "/course-names"));
        Assert.Equal("Renamed 2 classes to their Canvas course names; 32 lectures moved with them.", lib.Say);
        Assert.Equal(["Software Engineering", "Senior Design", "HIST 210", "MATH 221"], lib.Classes.Select(c => c.Name));
        Assert.False(lib.HasCourseNames);
        Assert.False(lib.ConfirmingCourseNames);
        Assert.Equal(1, heard);
        Assert.Equal("Software Engineering", host.Lectures.Get("rec-1")!.ClassName); // a lecture waiting here follows
    }

    [AvaloniaFact]
    public void While_Canvas_syncs_the_rename_waits_and_says_why()
    {
        var fake = new FakeLibrarySettings { Settings = FakeLibrarySettings.CodeNamed() };
        fake.Settings["course_names"]!["blocked"] = "Canvas is syncing. Try again when it's done.";
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Classes";

        Assert.True(model.Lib.HasCourseNames);
        Assert.False(model.Lib.CanUseCourseNames);
        Assert.Equal("Canvas is syncing. Try again when it's done.", model.Lib.CourseNamesSub);
    }

    [AvaloniaFact]
    public async Task A_refused_change_goes_back_and_says_why()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Classes";
        var lib = model.Lib;

        fake.Refuse = "There are two classes called CS 101.";
        lib.Classes[1].Name = "CS 101";
        await lib.SaveClassesCommand.ExecuteAsync(null);

        Assert.Equal("There are two classes called CS 101.", lib.Say);
        Assert.Equal("BIO 110", lib.Classes[1].Name); // read again from the library
    }

    [AvaloniaFact]
    public async Task Classes_are_added_edited_and_removed_and_a_row_being_typed_in_stays()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Classes";
        var lib = model.Lib;
        var cs = lib.Classes[0];

        cs.Aliases = "cs101,  intro to cs, CS1";
        await lib.SaveClassesCommand.ExecuteAsync(null);
        var sent = fake.Calls.Last().Body!["classes"]![0]!;
        Assert.Equal(["cs101", "intro to cs", "CS1"], sent["aliases"]!.AsArray().Select(a => a!.GetValue<string>()));
        Assert.Same(cs, lib.Classes[0]); // the echo doesn't rebuild the rows under the pointer

        lib.NewClass = "PHYS 150";
        await lib.AddClassCommand.ExecuteAsync(null);
        Assert.Equal("PHYS 150", lib.Classes.Last().Name);
        Assert.Equal("", lib.NewClass);
        Assert.Equal("/Users/sam/Study Stash/Lecture notes/PHYS 150", lib.Classes.Last().Folder); // from the library's answer
        Assert.Same(cs, lib.Classes[0]);

        await lib.RemoveClassCommand.ExecuteAsync(lib.Classes.Single(c => c.Name == "HIST 210"));
        Assert.Equal(["CS 101", "BIO 110", "MATH 221", "PHYS 150"], fake.Settings["classes"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()));
    }

    [AvaloniaFact]
    public async Task The_app_hears_when_the_classes_change_and_not_when_the_library_says_no()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Classes";
        var lib = model.Lib;
        int heard = 0;
        lib.ClassesChanged = () => heard++;

        lib.NewClass = "PHYS 150";
        await lib.AddClassCommand.ExecuteAsync(null);
        lib.Classes[0].Description = "Recursion";
        await lib.SaveClassesCommand.ExecuteAsync(null);
        await lib.RemoveClassCommand.ExecuteAsync(lib.Classes.Single(c => c.Name == "PHYS 150"));
        Assert.Equal(3, heard);

        fake.Refuse = "There are two classes called CS 101.";
        lib.NewClass = "CS 101";
        await lib.AddClassCommand.ExecuteAsync(null);
        Assert.Equal(3, heard);
    }

    [AvaloniaFact]
    public async Task Folders_it_may_read_change_from_here()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Folders";
        var lib = model.Lib;

        lib.Folders[0].Private = true;
        await Task.Yield();
        Assert.Equal("""{"folders":[{"path":"/Users/sam/Documents/School","ai":true,"private":true},{"path":"/Users/sam/Documents/Journal","ai":false,"private":true}]}""",
            fake.Calls.Last().Body!.ToJsonString());

        lib.NewFolder = "/Users/sam/Documents/Papers";
        await lib.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal("/Users/sam/Documents/Papers", fake.Calls.Last().Body!["add_folder"]!.GetValue<string>());
        Assert.Equal(3, lib.Folders.Count);

        await lib.RemoveFolderCommand.ExecuteAsync(lib.Folders[1]);
        Assert.Equal(["School", "Papers"], lib.Folders.Select(f => f.Name));
    }

    [AvaloniaFact]
    public async Task Rewriting_every_summary_asks_first()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Notes";
        var lib = model.Lib;

        lib.AskRewriteAllCommand.Execute(null);
        Assert.True(lib.ConfirmingRewrite);
        Assert.DoesNotContain(fake.Calls, c => c.Path == "/rewrite-all");
        await lib.RewriteAllCommand.ExecuteAsync(null);

        Assert.Contains(fake.Calls, c => c.Path == "/rewrite-all");
        Assert.Equal("42 lectures are queued for a new summary.", lib.Say);
    }

    [AvaloniaFact]
    public async Task A_library_that_cant_be_reached_or_is_older_says_so_and_changes_nothing()
    {
        var fake = new FakeLibrarySettings { Down = true };
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;

        model.Section = "Library";
        await Task.Yield();
        Assert.Equal(LibrarySettingsState.Unreachable, model.Lib.State);
        Assert.True(model.Lib.CanRetry);
        Assert.False(model.Lib.IsReady);

        fake.Down = false;
        await model.Lib.LoadCommand.ExecuteAsync(null);
        Assert.True(model.Lib.IsReady);

        fake.Older = true;
        await model.Lib.LoadCommand.ExecuteAsync(null);
        Assert.Equal(LibrarySettingsState.Older, model.Lib.State);
        Assert.True(model.Lib.CanOpenPage);
    }

    [AvaloniaFact]
    public void Without_a_library_its_pages_say_where_to_connect()
    {
        using var home = new TempHome();
        using var host = new AppHost(home.Path, log: _ => { });
        using var model = SettingsModel.Make(host);

        model.Section = "Classes";

        Assert.Equal(LibrarySettingsState.NoLibrary, model.Lib.State);
        Assert.Contains("Connection", model.Lib.Problem);
    }

    [AvaloniaFact]
    public void A_library_run_without_the_app_cant_start_at_login_from_here()
    {
        var fake = new FakeLibrarySettings();
        fake.Settings["start_at_login"] = null;
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        model.Section = "Library";

        model.Lib.StartAtLoginOn = true;

        Assert.False(model.Lib.CanStartAtLogin);
        Assert.DoesNotContain(fake.Calls, c => c.Method == "POST");
        Assert.Contains("without the Study Stash app", model.Lib.StartAtLoginSub);
    }

    /// <summary>A laptop updates itself on its own say: General's Update automatically is client.toml's, it survives a
    /// restart, and it and the library's switch (Your library → Library) never change each other.</summary>
    [AvaloniaFact]
    public async Task A_laptops_own_Update_automatically_and_its_librarys_are_two_switches()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        Assert.True(model.IsLaptopRole);
        Assert.True(model.AutoUpdateHere);

        model.AutoUpdateHere = false;

        Assert.False(Configs.LoadClient(home.Path).AutoUpdate);
        Assert.False(AppUpdates.AutoUpdateOn(host));
        Assert.DoesNotContain(fake.Calls, c => c.Method == "POST");
        Assert.True(fake.Settings["updates"]!["auto"]!.GetValue<bool>());
        using (var again = new AppHost(home.Path, log: _ => { }))
        using (var reopened = SettingsModel.Make(again, library: () => fake.Call))
            Assert.False(reopened.AutoUpdateHere);

        // The library's switch shows the library's, not this laptop's, and changes only the library.
        model.AutoUpdateHere = true;
        model.Section = "Library";
        Assert.True(model.Lib.AutoUpdate);
        model.Lib.AutoUpdate = false;
        await Task.Yield();

        Assert.False(fake.Settings["updates"]!["auto"]!.GetValue<bool>());
        Assert.True(Configs.LoadClient(home.Path).AutoUpdate);
        Assert.True(AppUpdates.AutoUpdateOn(host));
    }

    /// <summary>A computer that is the library has one switch, Your library's: the app reads client.toml's too, so
    /// that switch sets both, and shows off when this computer's is off whatever the library says.</summary>
    [AvaloniaFact]
    public async Task On_the_librarys_own_computer_its_one_switch_sets_both()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake, AppRole.Both);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        Assert.False(model.IsLaptopRole);
        var cc = host.Client();
        cc.AutoUpdate = false; // switched off back when this was a laptop
        host.SaveClient(cc);

        model.Section = "Library";
        Assert.False(model.Lib.AutoUpdate);

        model.Lib.AutoUpdate = true;
        await Task.Yield();
        Assert.True(Configs.LoadClient(home.Path).AutoUpdate);
        Assert.True(fake.Settings["updates"]!["auto"]!.GetValue<bool>());

        model.Lib.AutoUpdate = false;
        await Task.Yield();
        Assert.False(Configs.LoadClient(home.Path).AutoUpdate);
        Assert.False(fake.Settings["updates"]!["auto"]!.GetValue<bool>());
    }

    /// <summary>General's Check now says whether a newer version is out without installing it; Update now installs it.</summary>
    [AvaloniaFact]
    public async Task A_laptops_Check_now_looks_and_Update_now_installs()
    {
        var fake = new FakeLibrarySettings();
        var (model, host, home) = Open(fake);
        using var _h = home;
        using var _host = host;
        using var _m = model;
        Release? out_ = null;
        bool applied = false;
        model.WithUpdater(new AppUpdates
        {
            Latest = () => Task.FromResult(out_),
            Idle = () => true,
            Enabled = () => true,
            OnDiskVersion = () => Engine.Version,
            Apply = (_, _, _, _) => { applied = true; return Task.FromResult(true); },
            RelaunchAndQuit = () => { },
            Log = _ => { },
            Home = home.Path,
        });

        await model.CheckUpdateHereCommand.ExecuteAsync(null);
        Assert.Contains("the newest", model.UpdateHereLine);
        Assert.False(model.CanUpdateHere);

        out_ = new Release("v99.0.0", Updates.ParseVersion("99.0.0"), "https://example.com/99", "https://example.com");
        await model.CheckUpdateHereCommand.ExecuteAsync(null);
        Assert.Equal($"Version 99.0.0 is out. This laptop has {Engine.Version}.", model.UpdateHereLine);
        Assert.True(model.CanUpdateHere);
        Assert.False(applied);

        await model.UpdateHereNowCommand.ExecuteAsync(null);
        Assert.True(applied);
        Assert.False(model.CanUpdateHere);
        Assert.StartsWith("Updating", model.UpdateHereLine);
    }
}
