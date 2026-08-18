using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CrazyBatto.RedManager;

namespace CrazyBatto.RedManager.Tests;

public sealed class ApiCompatibilityTests
{
    [Fact]
    public void Dependencies_accept_comma_separated_string()
    {
        const string json = """
            {
              "mod_id": "example",
              "name": "Example",
              "dependencies": "core-lib, ui-lib, core-lib"
            }
            """;

        var mod = JsonSerializer.Deserialize<OnlineMod>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(mod);
        Assert.Equal(new[] { "core-lib", "ui-lib" }, mod.Dependencies);
    }

    [Fact]
    public void Dependencies_accept_array()
    {
        const string json = """
            {
              "mod_id": "example",
              "name": "Example",
              "dependencies": ["core-lib", "ui-lib"]
            }
            """;

        var mod = JsonSerializer.Deserialize<OnlineMod>(json);

        Assert.NotNull(mod);
        Assert.Equal(new[] { "core-lib", "ui-lib" }, mod.Dependencies);
    }

    [Fact]
    public void Explicit_latest_release_wins()
    {
        var releases = new[]
        {
            new ModRelease { Version = "3.0.0-beta" },
            new ModRelease { Version = "2.5.0", IsLatest = true },
            new ModRelease { Version = "2.6.0" }
        };

        Assert.Equal("2.5.0", VersionSelector.SelectLatest(releases)?.Version);
    }

    [Fact]
    public void Stable_semantic_release_wins_over_prerelease()
    {
        var releases = new[]
        {
            new ModRelease { Version = "1.9.9" },
            new ModRelease { Version = "2.0.0-beta" },
            new ModRelease { Version = "2.0.0" }
        };

        Assert.Equal("2.0.0", VersionSelector.SelectLatest(releases)?.Version);
    }
}

public sealed class SafeArchiveInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CrazyBatto-RedManager-Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Valid_wrapped_redloader_layout_is_installed()
    {
        var game = CreateGameDirectory();
        var archivePath = Path.Combine(_root, "valid.zip");
        Directory.CreateDirectory(_root);

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "Package/Mods/TestMod.dll", "not-a-real-dll");
            WriteEntry(archive, "Package/Mods/TestMod/manifest.json", "{\"id\":\"TestMod\",\"version\":\"1.0.0\"}");
            WriteEntry(archive, "Package/README.md", "documentation");
        }

        var installer = new SafeArchiveInstaller();
        var installed = await installer.InstallPackageAsync(archivePath, game);

        Assert.Contains(installed, path => string.Equals(path, "Mods/TestMod.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(installed, path => string.Equals(path, "Mods/TestMod/manifest.json", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(game, "Mods", "TestMod.dll")));
        Assert.False(File.Exists(Path.Combine(game, "README.md")));
    }

    [Fact]
    public async Task Traversal_entry_is_rejected_before_extraction()
    {
        var game = CreateGameDirectory();
        var archivePath = Path.Combine(_root, "traversal.zip");
        Directory.CreateDirectory(_root);

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "../outside.dll", "bad");
            WriteEntry(archive, "Mods/Good.dll", "good");
        }

        var installer = new SafeArchiveInstaller();
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallPackageAsync(archivePath, game));
        Assert.False(File.Exists(Path.Combine(_root, "outside.dll")));
        Assert.False(File.Exists(Path.Combine(game, "Mods", "Good.dll")));
    }

    [Theory]
    [InlineData("Mods/payload.exe")]
    [InlineData("Mods/install.cmd")]
    [InlineData("Mods/script.ps1")]
    [InlineData("Mods/shortcut.lnk")]
    public async Task Executable_payloads_are_rejected(string entryName)
    {
        var game = CreateGameDirectory();
        var archivePath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        Directory.CreateDirectory(_root);

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, entryName, "blocked");
        }

        var installer = new SafeArchiveInstaller();
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallPackageAsync(archivePath, game));
    }

    private string CreateGameDirectory()
    {
        var game = Path.Combine(_root, "Sons Of The Forest");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "SonsOfTheForest.exe"), string.Empty);
        return game;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, 1024, leaveOpen: false);
        writer.Write(content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch
        {
            // Test cleanup is best effort.
        }
    }
}
