using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using StudyStash.App.Controls;
using StudyStash.App.Services;
using StudyStash.App.ViewModels;
using StudyStash.App.Views;
using StudyStash.Core;

[assembly: AvaloniaTestApplication(typeof(StudyStash.App.Tests.TestApp))]
// Every test on Avalonia's one UI thread, plain [Fact]s too: a plain test touching Avalonia first can otherwise
// make a worker thread the UI thread, and the pictures drawn after it fail (it happened on Windows CI).
[assembly: AvaloniaTestFramework]


namespace StudyStash.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<StudyStash.App.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
