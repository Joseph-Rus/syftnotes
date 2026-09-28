namespace StudyStash.Core.Tests;

/// <summary>Study Stash's four installer names and its checksums file name live in one place, <see
/// cref="Updates"/> (D2). ci.yml's release step and the build scripts that produce those files must use the
/// same names, or a release could ship something the updater doesn't recognize.</summary>
public class ReleaseNamesTests
{
    [Fact]
    public void Ci_releases_every_installer_and_the_checksums_file_by_its_real_name()
    {
        string ci = Read(".github", "workflows", "ci.yml");
        foreach (string name in Updates.Installers)
            Assert.Contains(name, ci);
        Assert.Contains(Updates.ChecksumsAsset, ci);
    }

    [Fact]
    public void The_Mac_build_script_makes_the_one_DMG_with_no_role_in_the_app()
    {
        string buildApp = Read("macos", "build-app.sh");
        Assert.Contains(Updates.MacAsset, buildApp);
        Assert.DoesNotContain("StudyStashRole", buildApp);
        Assert.DoesNotContain("StudyStashRole", Read("macos", "Info.plist"));
    }

    [Fact]
    public void The_Windows_installer_makes_the_one_Setup_exe_and_writes_no_role()
    {
        string setupIss = Read("windows", "setup.iss");
        Assert.Contains("OutputBaseFilename=" + Path.GetFileNameWithoutExtension(Updates.WindowsAsset), setupIss);
        Assert.DoesNotContain("Key: \"role\"", setupIss);
        Assert.DoesNotContain("/DRole", Read("windows", "build.ps1"));
    }

    [Fact]
    public void Ci_publishes_the_old_role_names_as_copies_of_the_one_download()
    {
        // Copies 0.8.x and older update themselves by these names: every release must still carry them, the same bytes.
        string ci = Read(".github", "workflows", "ci.yml");
        Assert.Contains("cp dist/Study-Stash.dmg \"dist/Study-Stash-$role.dmg\"", ci);
        Assert.Contains("cp dist/Study-Stash-Setup.exe \"dist/Study-Stash-$role-Setup.exe\"", ci);
        Assert.Contains("for role in Laptop Library", ci);
    }

    [Fact]
    public void The_install_scripts_name_every_installer_they_can_fetch()
    {
        string installSh = Read("install.sh");
        Assert.Contains(Updates.MacLaptopAsset, installSh);
        Assert.Contains(Updates.MacLibraryAsset, installSh);

        string installPs1 = Read("install.ps1");
        Assert.Contains(Updates.WindowsLaptopAsset, installPs1);
        Assert.Contains(Updates.WindowsLibraryAsset, installPs1);
    }

    [Fact]
    public void Ci_mentions_none_of_the_retired_release_shapes()
    {
        string ci = Read(".github", "workflows", "ci.yml");
        foreach (string retired in new[]
        {
            "Study-Stash-engine-", "Study-Stash-mac.zip", "helper-windows", "App-preview", "python", "pytest", "uv ",
        })
            Assert.DoesNotContain(retired, ci, StringComparison.OrdinalIgnoreCase);
    }

    static string RoleHalf(string setupExeName) => setupExeName[..^".exe".Length];

    static string Read(params string[] pathFromRoot) => File.ReadAllText(Path.Combine([RepoRoot(), .. pathFromRoot]));

    /// <summary>The repo root, found by walking up from where the tests run until engine/Directory.Build.props
    /// turns up (the same trick VersionTests uses).</summary>
    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "engine", "Directory.Build.props")))
                return d.FullName;
        throw new FileNotFoundException("couldn't find the repo root (engine/Directory.Build.props)");
    }
}
