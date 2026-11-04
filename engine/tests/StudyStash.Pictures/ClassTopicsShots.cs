using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using StudyStash.App.Services;
using StudyStash.App.Views;

namespace StudyStash.App.Pictures;

/// <summary>Settings → Classes with what each class covers: one written by the student's AI (Claude's own words, from a
/// real run on made-up Canvas data), one from Canvas's own words (no AI), and one the student typed (no mark), both
/// looks, light and dark (shots/&lt;mac|win&gt;-settings-class-topics-&lt;light|dark&gt;.png). Drawn off screen into
/// STUDYSTASH_SHOTS; only with STUDYSTASH_FILM=1. Made-up classes; no library or AI is asked.</summary>
public class ClassTopicsShots
{
    static JsonObject Library()
    {
        var s = FakeLibrarySettings.Demo();
        s["classes"] = JsonNode.Parse("""
            [
              {"name": "Thermodynamics", "aliases": ["me301"], "description": "entropy and the second law, first law energy balances, Carnot cycles and heat engines, entropy generation, systems, properties and state",
               "description_by": "ai", "folder": "/Users/sam/Syft Notes/Lecture notes/Thermodynamics", "lectures": 12},
              {"name": "Heat Transfer", "aliases": ["me342"], "description": "Conduction and Fourier's Law, Convection, Radiation and View Factors",
               "description_by": "canvas", "folder": "/Users/sam/Syft Notes/Lecture notes/Heat Transfer", "lectures": 9},
              {"name": "Organic Chemistry", "aliases": ["chem 241"], "description": "Dr. Patel's class: reaction mechanisms, stereochemistry",
               "description_by": "", "folder": "/Users/sam/Syft Notes/Lecture notes/Organic Chemistry", "lectures": 7}
            ]
            """);
        return s;
    }

    [AvaloniaFact]
    public async Task Settings_classes_with_what_each_covers()
    {
        if (Environment.GetEnvironmentVariable("STUDYSTASH_FILM") != "1") return;
        foreach (var skin in new[] { SkinKind.Mac, SkinKind.Win })
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                using var home = new TempHome();
                using var host = new AppHost(home.Path, log: _ => { });
                var library = new FakeLibrarySettings { Settings = Library() };
                using var model = SettingsModel.Make(host, library: () => library.Call);
                await model.Lib.Load();
                model.Section = "Classes";
                Shot.Take($"{(skin == SkinKind.Mac ? "mac" : "win")}-settings-class-topics", skin, variant,
                    () => new SettingsView { DataContext = model, DrawChrome = true, Width = 1000, Height = 760 }, size: new Size(1128, 888));
            }
    }
}
