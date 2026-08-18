using Microsoft.Win32;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CrazyBatto.RedManager;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Crazy_Batto",
        "RedManager");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ReceiptsDirectory => Path.Combine(Root, "receipts");
    public static string BackupDirectory => Path.Combine(Root, "backups");
    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ReceiptsDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

public sealed class AppSettings
{
    public string? GameDirectory { get; set; }
    public string? ApiBaseUrl { get; set; }
    public bool IncludeNsfw { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.SettingsFile);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                   ?? new AppSettings();
        }
        catch
        {
            var broken = AppPaths.SettingsFile + $".broken-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(AppPaths.SettingsFile, broken, true);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppPaths.EnsureCreated();
        var temporaryPath = AppPaths.SettingsFile + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, AppPaths.SettingsFile, true);
    }
}

public sealed class ApiEnvelope<T>
{
    [JsonPropertyName("status")]
    public bool Status { get; set; } = true;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("meta")]
    public ApiMeta? Meta { get; set; }
}

public sealed class ApiMeta
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("pages")]
    public int Pages { get; set; }
}

public sealed class OnlineMod
{
    [JsonPropertyName("mod_id")]
    public string ModId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Unbenannter Mod";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("shortDescription")]
    public string ShortDescription { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("modSide")]
    public string? ModSide { get; set; }

    [JsonPropertyName("isNSFW")]
    public bool IsNsfw { get; set; }

    [JsonPropertyName("isApproved")]
    public bool IsApproved { get; set; }

    [JsonPropertyName("isMultiplayerCompatible")]
    public bool IsMultiplayerCompatible { get; set; }

    [JsonPropertyName("requiresAllPlayers")]
    public bool RequiresAllPlayers { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("dependencies")]
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Dependencies { get; set; } = [];

    [JsonPropertyName("versions")]
    public List<ModRelease> Versions { get; set; } = [];

    [JsonPropertyName("images")]
    public List<ModImage> Images { get; set; } = [];

    [JsonPropertyName("user")]
    public ModAuthor? User { get; set; }

    [JsonPropertyName("category")]
    public ModCategory? Category { get; set; }

    public string DisplayAuthor => string.IsNullOrWhiteSpace(User?.Name) ? "Unbekannt" : User.Name;
    public string DisplayVersion => VersionSelector.SelectLatest(Versions, LatestVersion)?.Version
                                    ?? LatestVersion
                                    ?? "–";
    public string DisplaySide => string.IsNullOrWhiteSpace(ModSide) ? "Unbekannt" : ModSide;
    public string MultiplayerText => !IsMultiplayerCompatible
        ? "Nein / unbekannt"
        : RequiresAllPlayers ? "Alle Spieler" : "Host genügt";
    public string DisplayDescription => !string.IsNullOrWhiteSpace(Description)
        ? Description
        : ShortDescription;
}

public sealed class ModRelease
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; set; }

    [JsonPropertyName("changelog")]
    public string Changelog { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class ModAuthor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("isTrusted")]
    public bool IsTrusted { get; set; }
}

public sealed class ModCategory
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;
}

public sealed class ModImage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("isThumbnail")]
    public bool IsThumbnail { get; set; }
}

public sealed class FlexibleStringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType is JsonTokenType.String)
        {
            return Split(reader.GetString());
        }

        if (reader.TokenType is JsonTokenType.StartArray)
        {
            var result = new List<string>();
            while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
            {
                if (reader.TokenType is JsonTokenType.String)
                {
                    result.AddRange(Split(reader.GetString()));
                }
                else
                {
                    using var ignored = JsonDocument.ParseValue(ref reader);
                }
            }

            return Normalize(result);
        }

        using (JsonDocument.ParseValue(ref reader))
        {
            return [];
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var dependency in Normalize(value))
        {
            writer.WriteStringValue(dependency);
        }
        writer.WriteEndArray();
    }

    private static List<string> Split(string? value) => Normalize(
        (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static List<string> Normalize(IEnumerable<string> values) => values
        .Select(value => value.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

public static class VersionSelector
{
    public static ModRelease? SelectLatest(IEnumerable<ModRelease>? releases, string? advertisedVersion = null)
    {
        var available = releases?.Where(item => !string.IsNullOrWhiteSpace(item.Version)).ToList() ?? [];
        if (available.Count == 0)
        {
            return null;
        }

        var explicitlyLatest = available.FirstOrDefault(item => item.IsLatest);
        if (explicitlyLatest is not null)
        {
            return explicitlyLatest;
        }

        if (!string.IsNullOrWhiteSpace(advertisedVersion))
        {
            var advertised = available.FirstOrDefault(item =>
                string.Equals(item.Version.TrimStart('v', 'V'), advertisedVersion.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase));
            if (advertised is not null)
            {
                return advertised;
            }
        }

        return available
            .OrderByDescending(item => SemanticVersionKey.Parse(item.Version))
            .ThenByDescending(item => item.CreatedAt ?? DateTimeOffset.MinValue)
            .First();
    }

    private readonly record struct SemanticVersionKey(int Major, int Minor, int Patch, int Revision, bool Stable, string Suffix)
        : IComparable<SemanticVersionKey>
    {
        public static SemanticVersionKey Parse(string? value)
        {
            var cleaned = (value ?? string.Empty).Trim().TrimStart('v', 'V');
            var split = cleaned.Split('-', 2, StringSplitOptions.TrimEntries);
            var numbers = split[0].Split('.', StringSplitOptions.RemoveEmptyEntries);
            static int Number(string[] values, int index) =>
                index < values.Length && int.TryParse(Regex.Match(values[index], "^\\d+").Value, out var parsed) ? parsed : 0;

            return new SemanticVersionKey(
                Number(numbers, 0),
                Number(numbers, 1),
                Number(numbers, 2),
                Number(numbers, 3),
                split.Length == 1,
                split.Length == 2 ? split[1] : string.Empty);
        }

        public int CompareTo(SemanticVersionKey other)
        {
            var values = new[]
            {
                Major.CompareTo(other.Major),
                Minor.CompareTo(other.Minor),
                Patch.CompareTo(other.Patch),
                Revision.CompareTo(other.Revision),
                Stable.CompareTo(other.Stable)
            };
            foreach (var value in values)
            {
                if (value != 0)
                {
                    return value;
                }
            }

            return string.Compare(Suffix, other.Suffix, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed class SotfModsApiClient : IDisposable
{
    private const long MaximumDownloadBytes = 768L * 1024L * 1024L;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly List<Uri> _baseUris;
    private Uri? _activeBaseUri;

    public SotfModsApiClient(string? configuredBaseUrl = null, HttpMessageHandler? handler = null)
    {
        var clientHandler = handler ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        };
        _httpClient = new HttpClient(clientHandler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CrazyBatto-RedManager", "2.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        _baseUris = [];
        AddBaseUri(configuredBaseUrl);
        AddBaseUri("https://api.sotf-mods.com/");
        AddBaseUri("https://sotf-mods.com/");
    }

    public async Task<IReadOnlyList<OnlineMod>> GetModsAsync(
        string? search,
        bool includeNsfw,
        int page = 1,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = Math.Max(1, page).ToString(),
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["type"] = "Both",
            ["approved"] = "true",
            ["nsfw"] = includeNsfw ? "true" : "false",
            ["orderby"] = "newest",
            ["search"] = string.IsNullOrWhiteSpace(search) ? null : search.Trim()
        };

        var path = "api/mods?" + string.Join("&", query
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        var envelope = await GetEnvelopeAsync<List<OnlineMod>>(path, cancellationToken);
        return (envelope.Data ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.ModId))
            .GroupBy(item => item.ModId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public async Task<OnlineMod> GetModAsync(string modId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
        {
            throw new ArgumentException("Eine Mod-ID ist erforderlich.", nameof(modId));
        }

        var envelope = await GetEnvelopeAsync<OnlineMod>(
            $"api/mods/{Uri.EscapeDataString(modId.Trim())}",
            cancellationToken);
        return envelope.Data ?? throw new InvalidOperationException($"Die API lieferte keine Daten für Mod '{modId}'.");
    }

    public async Task<OnlineMod> ResolveModAsync(string idSlugOrName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetModAsync(idSlugOrName, cancellationToken);
        }
        catch (Exception firstError) when (firstError is HttpRequestException or InvalidOperationException)
        {
            var matches = await GetModsAsync(idSlugOrName, includeNsfw: false, page: 1, limit: 50, cancellationToken);
            var exact = matches.FirstOrDefault(item =>
                string.Equals(item.ModId, idSlugOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Slug, idSlugOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, idSlugOrName, StringComparison.OrdinalIgnoreCase));
            return exact ?? throw new InvalidOperationException(
                $"Abhängigkeit '{idSlugOrName}' wurde nicht gefunden.", firstError);
        }
    }

    public async Task<string> DownloadAsync(
        OnlineMod mod,
        ModRelease release,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(release);

        var downloadUri = ResolveDownloadUri(mod, release);
        if (!string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Mod-Downloads sind ausschließlich über HTTPS erlaubt.");
        }

        progress?.Report($"Lade {mod.Name} {release.Version} herunter …");
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new InvalidDataException("Das Mod-Paket überschreitet die erlaubte Maximalgröße von 768 MiB.");
        }

        var filename = ChooseDownloadFilename(mod, release, response);
        var directory = Path.Combine(Path.GetTempPath(), "CrazyBatto-RedManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(path);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumDownloadBytes)
            {
                throw new InvalidDataException("Der Download überschreitet die erlaubte Maximalgröße von 768 MiB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return path;
    }

    private async Task<ApiEnvelope<T>> GetEnvelopeAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var baseUri in OrderedBaseUris())
        {
            try
            {
                var uri = new Uri(baseUri, relativePath);
                using var response = await _httpClient.GetAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, _jsonOptions, cancellationToken);
                if (envelope?.Data is null)
                {
                    throw new InvalidDataException("Die API-Antwort enthält kein Datenfeld.");
                }

                _activeBaseUri = baseUri;
                return envelope;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or TaskCanceledException)
            {
                errors.Add($"{baseUri.Host}: {exception.Message}");
            }
        }

        throw new InvalidOperationException(
            "Die sotf-mods.com-API konnte nicht erreicht oder gelesen werden. " + string.Join(" | ", errors));
    }

    private IEnumerable<Uri> OrderedBaseUris()
    {
        if (_activeBaseUri is not null)
        {
            yield return _activeBaseUri;
        }

        foreach (var baseUri in _baseUris)
        {
            if (_activeBaseUri is null || baseUri != _activeBaseUri)
            {
                yield return baseUri;
            }
        }
    }

    private Uri ResolveDownloadUri(OnlineMod mod, ModRelease release)
    {
        if (Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var direct))
        {
            return direct;
        }

        var baseUri = _activeBaseUri ?? _baseUris.First();
        if (string.IsNullOrWhiteSpace(mod.User?.Slug) || string.IsNullOrWhiteSpace(mod.Slug))
        {
            throw new InvalidOperationException("Die API lieferte weder eine Downloadadresse noch vollständige Slug-Daten.");
        }

        var path = $"api/mods/slug/{Uri.EscapeDataString(mod.User.Slug)}/{Uri.EscapeDataString(mod.Slug)}/download/{Uri.EscapeDataString(release.Version)}";
        return new Uri(baseUri, path);
    }

    private static string ChooseDownloadFilename(OnlineMod mod, ModRelease release, HttpResponseMessage response)
    {
        var headerName = response.Content.Headers.ContentDisposition?.FileNameStar
                         ?? response.Content.Headers.ContentDisposition?.FileName;
        var filename = SanitizeFilename(headerName?.Trim('"'));
        if (!string.IsNullOrWhiteSpace(filename))
        {
            return filename;
        }

        filename = SanitizeFilename(release.Filename);
        if (!string.IsNullOrWhiteSpace(filename))
        {
            return filename;
        }

        var extension = (release.Extension ?? "zip").Trim().TrimStart('.');
        extension = Regex.IsMatch(extension, "^[a-zA-Z0-9]{1,8}$") ? extension : "zip";
        return $"{SanitizeFilename(mod.ModId) ?? "mod"}-{SanitizeFilename(release.Version) ?? "latest"}.{extension}";
    }

    private static string? SanitizeFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return null;
        }

        var value = Path.GetFileName(filename.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void AddBaseUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalized = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        if (_baseUris.All(existing => existing != normalized))
        {
            _baseUris.Add(normalized);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class GameLocator
{
    public const string SteamAppId = "1326470";
    private static readonly string[] ExpectedExecutableNames = ["SonsOfTheForest.exe", "Sons Of The Forest.exe"];

    public Task<string?> LocateAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var candidate in EnumerateCandidates(configuredPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsGameDirectory(candidate))
            {
                return Task.FromResult<string?>(Path.GetFullPath(candidate));
            }
        }

        return Task.FromResult<string?>(null);
    }

    public static bool IsGameDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return ExpectedExecutableNames.Any(name => File.Exists(Path.Combine(path, name)));
    }

    public static string? FindExecutable(string gameDirectory) => ExpectedExecutableNames
        .Select(name => Path.Combine(gameDirectory, name))
        .FirstOrDefault(File.Exists);

    private static IEnumerable<string> EnumerateCandidates(string? configuredPath)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var direct in new[]
                 {
                     configuredPath,
                     Environment.GetEnvironmentVariable("SOTF_GAME_DIR")
                 })
        {
            if (!string.IsNullOrWhiteSpace(direct) && emitted.Add(direct))
            {
                yield return direct;
            }
        }

        foreach (var steamRoot in EnumerateSteamRoots())
        {
            foreach (var libraryRoot in EnumerateSteamLibraries(steamRoot))
            {
                var common = Path.Combine(libraryRoot, "steamapps", "common");
                var appManifest = Path.Combine(libraryRoot, "steamapps", $"appmanifest_{SteamAppId}.acf");
                var installDir = ReadInstallDirectory(appManifest);
                if (!string.IsNullOrWhiteSpace(installDir))
                {
                    var fromManifest = Path.Combine(common, installDir);
                    if (emitted.Add(fromManifest))
                    {
                        yield return fromManifest;
                    }
                }

                foreach (var folderName in new[] { "Sons Of The Forest", "Sons of the Forest", "SonsOfTheForest" })
                {
                    var standard = Path.Combine(common, folderName);
                    if (emitted.Add(standard))
                    {
                        yield return standard;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var candidates = new List<string?>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(ReadRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            candidates.Add(ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            candidates.Add(ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Replace('/', Path.DirectorySeparatorChar))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamRoot)
    {
        yield return steamRoot;
        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            yield break;
        }

        string content;
        try
        {
            content = File.ReadAllText(vdfPath);
        }
        catch
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(content, "\\\"path\\\"\\s*\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
        {
            var path = match.Groups["path"].Value
                .Replace("\\\\", "\\")
                .Replace('/', Path.DirectorySeparatorChar);
            if (Directory.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static string? ReadInstallDirectory(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(manifestPath);
            var match = Regex.Match(content, "\\\"installdir\\\"\\s*\\\"(?<dir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["dir"].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadRegistryValue(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class InstallationReceipt
{
    public string ModId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
    public List<string> Files { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
}

public sealed class InstalledMod
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = "Mod";
    public bool Enabled { get; set; }
    public string Source { get; set; } = "Lokal";
    public string? ReceiptPath { get; set; }
    public List<string> MainFiles { get; set; } = [];
    public List<string> AllFiles { get; set; } = [];

    public string StatusText => Enabled ? "Aktiv" : "Deaktiviert";
    public string SourceText => Source;
}

public sealed class LocalModIndex
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<InstalledMod>> ScanAsync(string gameDirectory, CancellationToken cancellationToken = default)
    {
        EnsureGameDirectory(gameDirectory);
        AppPaths.EnsureCreated();

        var result = new List<InstalledMod>();
        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var receiptPath in Directory.EnumerateFiles(AppPaths.ReceiptsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallationReceipt? receipt;
            try
            {
                await using var stream = File.OpenRead(receiptPath);
                receipt = await JsonSerializer.DeserializeAsync<InstallationReceipt>(stream, _jsonOptions, cancellationToken);
            }
            catch
            {
                continue;
            }

            if (receipt is null || string.IsNullOrWhiteSpace(receipt.ModId))
            {
                continue;
            }

            var existing = receipt.Files
                .Select(NormalizeRelativePath)
                .Where(relative => FileExistsEnabledOrDisabled(gameDirectory, relative))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (existing.Count == 0)
            {
                continue;
            }

            foreach (var file in existing)
            {
                knownFiles.Add(CanonicalRelative(file));
            }
            knownIds.Add(receipt.ModId);

            var mainFiles = existing.Where(IsModuleFile).ToList();
            result.Add(new InstalledMod
            {
                Id = receipt.ModId,
                Name = string.IsNullOrWhiteSpace(receipt.Name) ? receipt.ModId : receipt.Name,
                Version = receipt.Version,
                Author = receipt.Author,
                Kind = mainFiles.Any(path => path.StartsWith("Libs/", StringComparison.OrdinalIgnoreCase)) ? "Library" : "Mod",
                Enabled = mainFiles.Any(path => File.Exists(Absolute(gameDirectory, path))),
                Source = string.IsNullOrWhiteSpace(receipt.SourceUrl) ? "Installationsbeleg" : "sotf-mods.com",
                ReceiptPath = receiptPath,
                MainFiles = mainFiles,
                AllFiles = existing
            });
        }

        var modsDirectory = Path.Combine(gameDirectory, "Mods");
        if (Directory.Exists(modsDirectory))
        {
            foreach (var manifestPath in Directory.EnumerateFiles(modsDirectory, "manifest.json", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = ReadManifest(manifestPath);
                var id = manifest.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Path.GetFileName(Path.GetDirectoryName(manifestPath));
                }
                if (string.IsNullOrWhiteSpace(id) || knownIds.Contains(id))
                {
                    continue;
                }

                var moduleCandidates = FindManifestModuleCandidates(gameDirectory, manifestPath, id)
                    .Where(path => !knownFiles.Contains(CanonicalRelative(path)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (moduleCandidates.Count == 0)
                {
                    continue;
                }

                foreach (var file in moduleCandidates)
                {
                    knownFiles.Add(CanonicalRelative(file));
                }
                knownFiles.Add(CanonicalRelative(Path.GetRelativePath(gameDirectory, manifestPath)));
                knownIds.Add(id);

                result.Add(new InstalledMod
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name,
                    Version = manifest.Version,
                    Author = manifest.Author,
                    Description = manifest.Description,
                    Kind = string.Equals(manifest.Type, "Library", StringComparison.OrdinalIgnoreCase) ? "Library" : "Mod",
                    Enabled = moduleCandidates.Any(path => File.Exists(Absolute(gameDirectory, path))),
                    Source = "Lokales Manifest",
                    MainFiles = moduleCandidates,
                    AllFiles = moduleCandidates
                        .Append(Path.GetRelativePath(gameDirectory, manifestPath))
                        .Select(NormalizeRelativePath)
                        .ToList()
                });
            }
        }

        foreach (var rootName in new[] { "Mods", "Libs" })
        {
            var root = Path.Combine(gameDirectory, rootName);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeRelativePath(Path.GetRelativePath(gameDirectory, path));
                if (knownFiles.Contains(CanonicalRelative(relative)))
                {
                    continue;
                }

                knownFiles.Add(CanonicalRelative(relative));
                var baseName = Path.GetFileName(path);
                if (baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName[..^".disabled".Length];
                }
                baseName = Path.GetFileNameWithoutExtension(baseName);

                result.Add(new InstalledMod
                {
                    Id = baseName,
                    Name = baseName,
                    Version = "Unbekannt",
                    Author = "Unbekannt",
                    Kind = rootName == "Libs" ? "Library" : "Mod",
                    Enabled = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase),
                    Source = "Lokale DLL",
                    MainFiles = [relative],
                    AllFiles = [relative]
                });
            }
        }

        return result
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<string> SaveReceiptAsync(
        InstallationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        AppPaths.EnsureCreated();
        receipt.Files = receipt.Files
            .Select(NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filename = SafeIdentifier(receipt.ModId) + ".json";
        var path = Path.Combine(AppPaths.ReceiptsDirectory, filename);
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, receipt, _jsonOptions, cancellationToken);
        }
        File.Move(temporary, path, true);
        return path;
    }

    public async Task SetEnabledAsync(
        InstalledMod mod,
        string gameDirectory,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        EnsureGameDirectory(gameDirectory);
        ArgumentNullException.ThrowIfNull(mod);
        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativeInput in mod.MainFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(relativeInput);
            var enabledRelative = relative.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? relative[..^".disabled".Length]
                : relative;
            var disabledRelative = enabledRelative + ".disabled";
            var source = enabled ? Absolute(gameDirectory, disabledRelative) : Absolute(gameDirectory, enabledRelative);
            var destination = enabled ? Absolute(gameDirectory, enabledRelative) : Absolute(gameDirectory, disabledRelative);

            if (!File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, true);
            changed[relative] = enabled ? enabledRelative : disabledRelative;
        }

        if (changed.Count == 0)
        {
            throw new InvalidOperationException("Für diesen Eintrag wurde keine umschaltbare DLL gefunden.");
        }

        mod.MainFiles = mod.MainFiles.Select(path => changed.TryGetValue(NormalizeRelativePath(path), out var replacement)
            ? replacement
            : NormalizeRelativePath(path)).ToList();
        mod.AllFiles = mod.AllFiles.Select(path => changed.TryGetValue(NormalizeRelativePath(path), out var replacement)
            ? replacement
            : NormalizeRelativePath(path)).ToList();
        mod.Enabled = enabled;

        if (!string.IsNullOrWhiteSpace(mod.ReceiptPath) && File.Exists(mod.ReceiptPath))
        {
            try
            {
                var receipt = JsonSerializer.Deserialize<InstallationReceipt>(await File.ReadAllTextAsync(mod.ReceiptPath, cancellationToken), _jsonOptions);
                if (receipt is not null)
                {
                    receipt.Files = receipt.Files.Select(path => changed.TryGetValue(NormalizeRelativePath(path), out var replacement)
                        ? replacement
                        : NormalizeRelativePath(path)).ToList();
                    await SaveReceiptAsync(receipt, cancellationToken);
                }
            }
            catch
            {
                // The module was already toggled successfully; a broken legacy receipt must not undo it.
            }
        }
    }

    public async Task UninstallAsync(
        InstalledMod mod,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        EnsureGameDirectory(gameDirectory);
        ArgumentNullException.ThrowIfNull(mod);

        var files = mod.AllFiles
            .Concat(mod.MainFiles)
            .Select(NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException("Für den ausgewählten Mod sind keine lokalen Dateien bekannt.");
        }

        var backupRoot = Path.Combine(
            AppPaths.BackupDirectory,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
            SafeIdentifier(mod.Id));

        foreach (var relative in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in new[] { relative, relative + ".disabled" }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var absolute = Absolute(gameDirectory, candidate);
                if (!File.Exists(absolute))
                {
                    continue;
                }

                var backup = SafeCombine(backupRoot, candidate);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(absolute, backup, true);
                File.Delete(absolute);
                RemoveEmptyParents(Path.GetDirectoryName(absolute), gameDirectory);
            }
        }

        if (!string.IsNullOrWhiteSpace(mod.ReceiptPath) && File.Exists(mod.ReceiptPath))
        {
            File.Delete(mod.ReceiptPath);
        }

        await Task.CompletedTask;
    }

    private static (string Id, string Name, string Version, string Author, string Description, string Type) ReadManifest(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return (
                Property(root, "id"),
                Property(root, "name"),
                Property(root, "version"),
                Property(root, "author"),
                Property(root, "description"),
                Property(root, "type"));
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private static string Property(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static IEnumerable<string> FindManifestModuleCandidates(string gameDirectory, string manifestPath, string id)
    {
        var mods = Path.Combine(gameDirectory, "Mods");
        var folderName = Path.GetFileName(Path.GetDirectoryName(manifestPath));
        var names = new[] { id, folderName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            foreach (var suffix in new[] { ".dll", ".dll.disabled" })
            {
                var path = Path.Combine(mods, name + suffix);
                if (File.Exists(path))
                {
                    yield return NormalizeRelativePath(Path.GetRelativePath(gameDirectory, path));
                }
            }
        }
    }

    private static bool FileExistsEnabledOrDisabled(string gameDirectory, string relative)
    {
        var absolute = Absolute(gameDirectory, relative);
        if (File.Exists(absolute))
        {
            return true;
        }

        return !relative.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && File.Exists(absolute + ".disabled");
    }

    private static bool IsModuleFile(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);

    private static string Absolute(string gameDirectory, string relative) => SafeCombine(gameDirectory, relative);

    internal static string SafeCombine(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var relativeSystem = NormalizeRelativePath(relative).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativeSystem));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsicherer relativer Pfad: {relative}");
        }

        return full;
    }

    internal static string NormalizeRelativePath(string path) => path
        .Replace('\\', '/')
        .TrimStart('/')
        .Trim();

    private static string CanonicalRelative(string path)
    {
        var normalized = NormalizeRelativePath(path);
        return normalized.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^".disabled".Length]
            : normalized;
    }

    private static string SafeIdentifier(string value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, "[^a-zA-Z0-9._-]+", "_").Trim('_', '.');
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned.Length <= 100 ? cleaned : cleaned[..100];
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))[..20];
    }

    private static void RemoveEmptyParents(string? directory, string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var gameRoot = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var current = Path.GetFullPath(directory);
        while (current.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), gameRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    break;
                }
                Directory.Delete(current);
                current = Directory.GetParent(current)?.FullName ?? gameRoot;
            }
            catch
            {
                break;
            }
        }
    }

    private static void EnsureGameDirectory(string gameDirectory)
    {
        if (!GameLocator.IsGameDirectory(gameDirectory))
        {
            throw new DirectoryNotFoundException("Der konfigurierte Sons-of-the-Forest-Spielordner ist ungültig.");
        }
    }
}

public sealed class SafeArchiveInstaller
{
    private const int MaximumEntries = 4096;
    private const long MaximumEntryBytes = 512L * 1024L * 1024L;
    private const long MaximumTotalBytes = 1024L * 1024L * 1024L;
    private static readonly HashSet<string> AllowedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mods", "Libs", "UserData"
    };
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".vbs", ".vbe",
        ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar", ".lnk", ".url", ".reg"
    };
    private static readonly HashSet<string> RootModExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".pdb", ".json", ".cfg", ".ini", ".bundle", ".assetbundle", ".dat"
    };
    private static readonly HashSet<string> IgnoredDocumentationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".rtf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf"
    };

    public async Task<IReadOnlyList<string>> InstallPackageAsync(
        string packagePath,
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Das heruntergeladene Mod-Paket wurde nicht gefunden.", packagePath);
        }
        if (!GameLocator.IsGameDirectory(gameDirectory))
        {
            throw new DirectoryNotFoundException("Der Sons-of-the-Forest-Spielordner ist ungültig.");
        }

        var extension = Path.GetExtension(packagePath);
        if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.Combine(gameDirectory, "Mods"));
            var relative = LocalModIndex.NormalizeRelativePath(Path.Combine("Mods", Path.GetFileName(packagePath)));
            var destination = LocalModIndex.SafeCombine(gameDirectory, relative);
            File.Copy(packagePath, destination, true);
            return [relative];
        }

        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Nicht unterstütztes Mod-Paket: {extension}. Unterstützt werden ZIP und DLL.");
        }

        progress?.Report("Prüfe ZIP-Struktur und Dateitypen …");
        var staging = Path.Combine(Path.GetTempPath(), "CrazyBatto-RedManager", "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var plan = CreatePlan(archive);
            if (plan.Count == 0)
            {
                throw new InvalidDataException("Das ZIP enthält keine installierbaren RedLoader-Dateien.");
            }

            foreach (var item in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = LocalModIndex.SafeCombine(staging, item.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await using var source = item.Entry.Open();
                await using var destination = File.Create(staged);
                await source.CopyToAsync(destination, cancellationToken);
            }

            var installed = new List<string>();
            foreach (var item in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = LocalModIndex.SafeCombine(staging, item.RelativePath);
                var destination = LocalModIndex.SafeCombine(gameDirectory, item.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                BackupExisting(destination, gameDirectory, item.RelativePath);
                File.Copy(staged, destination, true);
                installed.Add(item.RelativePath);
            }

            return installed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
            catch
            {
                // Temporary cleanup should not hide the real installation result.
            }
        }
    }

    internal static IReadOnlyList<ArchivePlanItem> CreatePlan(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException($"Das ZIP enthält mehr als {MaximumEntries} Einträge.");
        }

        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        var wrapper = DetectWrapperFolder(files);
        long total = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ArchivePlanItem>();

        foreach (var entry in files)
        {
            ValidateEntry(entry);
            total = checked(total + entry.Length);
            if (total > MaximumTotalBytes)
            {
                throw new InvalidDataException("Das entpackte Mod-Paket überschreitet 1 GiB.");
            }

            var normalized = NormalizeArchivePath(entry.FullName);
            if (!string.IsNullOrWhiteSpace(wrapper) && normalized.StartsWith(wrapper + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[(wrapper.Length + 1)..];
            }

            var destination = RouteModEntry(normalized);
            if (destination is null)
            {
                continue;
            }

            destination = LocalModIndex.NormalizeRelativePath(destination);
            if (!destinations.Add(destination))
            {
                throw new InvalidDataException($"Mehrere ZIP-Einträge würden dieselbe Datei überschreiben: {destination}");
            }

            result.Add(new ArchivePlanItem(entry, destination));
        }

        return result;
    }

    private static string? DetectWrapperFolder(IEnumerable<ZipArchiveEntry> entries)
    {
        var firstSegments = entries
            .Select(entry => NormalizeArchivePath(entry.FullName))
            .Where(path => path.Contains('/'))
            .Select(path => path.Split('/', 2)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (firstSegments.Count != 1)
        {
            return null;
        }

        var candidate = firstSegments[0];
        return AllowedRoots.Contains(candidate) ? null : candidate;
    }

    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        var normalized = NormalizeArchivePath(entry.FullName);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.StartsWith('/') ||
            Path.IsPathRooted(normalized) ||
            Regex.IsMatch(normalized, "^[a-zA-Z]:"))
        {
            throw new InvalidDataException($"Absoluter oder ungültiger ZIP-Pfad: {entry.FullName}");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains('\0')))
        {
            throw new InvalidDataException($"Pfad-Traversal im ZIP erkannt: {entry.FullName}");
        }

        if (entry.Length > MaximumEntryBytes)
        {
            throw new InvalidDataException($"ZIP-Eintrag ist größer als 512 MiB: {entry.FullName}");
        }

        if (entry.CompressedLength > 0 && entry.Length > 10L * 1024L * 1024L && entry.Length / entry.CompressedLength > 500)
        {
            throw new InvalidDataException($"Verdächtiges Kompressionsverhältnis im ZIP: {entry.FullName}");
        }

        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixMode == 0xA000)
        {
            throw new InvalidDataException($"Symbolische Links sind in Mod-Paketen nicht erlaubt: {entry.FullName}");
        }

        var extension = Path.GetExtension(entry.Name);
        if (BlockedExtensions.Contains(extension))
        {
            throw new InvalidDataException($"Ausführbarer Dateityp im Mod-Paket blockiert: {entry.FullName}");
        }
    }

    private static string? RouteModEntry(string normalized)
    {
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (AllowedRoots.Contains(segments[0]))
        {
            return normalized;
        }

        var extension = Path.GetExtension(segments[^1]);
        if (segments.Length == 1)
        {
            if (IgnoredDocumentationExtensions.Contains(extension))
            {
                return null;
            }
            return RootModExtensions.Contains(extension) ? $"Mods/{segments[0]}" : null;
        }

        if (IgnoredDocumentationExtensions.Contains(extension))
        {
            return null;
        }

        if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase))
        {
            return $"Mods/{segments[^1]}";
        }

        return $"Mods/{normalized}";
    }

    private static string NormalizeArchivePath(string path) => path.Replace('\\', '/').Trim('/');

    private static void BackupExisting(string destination, string gameDirectory, string relative)
    {
        if (!File.Exists(destination))
        {
            return;
        }

        var backupRoot = Path.Combine(AppPaths.BackupDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), "install-update");
        var backup = LocalModIndex.SafeCombine(backupRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(destination, backup, true);
    }

    public sealed record ArchivePlanItem(ZipArchiveEntry Entry, string RelativePath);
}

public sealed class ModManagerService
{
    private readonly SotfModsApiClient _apiClient;
    private readonly SafeArchiveInstaller _archiveInstaller;
    private readonly LocalModIndex _localIndex;

    public ModManagerService(
        SotfModsApiClient apiClient,
        SafeArchiveInstaller archiveInstaller,
        LocalModIndex localIndex)
    {
        _apiClient = apiClient;
        _archiveInstaller = archiveInstaller;
        _localIndex = localIndex;
    }

    public async Task InstallAsync(
        OnlineMod selectedMod,
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedMod);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallRecursiveAsync(selectedMod.ModId, gameDirectory, completed, stack, progress, cancellationToken);
    }

    private async Task InstallRecursiveAsync(
        string modId,
        string gameDirectory,
        HashSet<string> completed,
        HashSet<string> stack,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (completed.Contains(modId))
        {
            return;
        }
        if (!stack.Add(modId))
        {
            progress?.Report($"Zyklische Abhängigkeit erkannt und übersprungen: {modId}");
            return;
        }

        try
        {
            var mod = await _apiClient.ResolveModAsync(modId, cancellationToken);
            foreach (var dependency in mod.Dependencies)
            {
                if (string.Equals(dependency, mod.ModId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                progress?.Report($"Prüfe Abhängigkeit {dependency} für {mod.Name} …");
                await InstallRecursiveAsync(dependency, gameDirectory, completed, stack, progress, cancellationToken);
            }

            var release = VersionSelector.SelectLatest(mod.Versions, mod.LatestVersion)
                          ?? throw new InvalidOperationException($"Für '{mod.Name}' ist keine installierbare Version veröffentlicht.");
            var packagePath = await _apiClient.DownloadAsync(mod, release, progress, cancellationToken);
            try
            {
                var files = await _archiveInstaller.InstallPackageAsync(packagePath, gameDirectory, progress, cancellationToken);
                var sourceUrl = !string.IsNullOrWhiteSpace(mod.User?.Slug) && !string.IsNullOrWhiteSpace(mod.Slug)
                    ? $"https://sotf-mods.com/mods/{mod.User.Slug}/{mod.Slug}"
                    : "https://sotf-mods.com/mods";
                await _localIndex.SaveReceiptAsync(new InstallationReceipt
                {
                    ModId = mod.ModId,
                    Name = mod.Name,
                    Version = release.Version,
                    Author = mod.DisplayAuthor,
                    SourceUrl = sourceUrl,
                    InstalledAt = DateTimeOffset.UtcNow,
                    Files = files.ToList(),
                    Dependencies = mod.Dependencies.ToList()
                }, cancellationToken);
                completed.Add(mod.ModId);
                progress?.Report($"Installiert: {mod.Name} {release.Version}");
            }
            finally
            {
                TryDeleteDownload(packagePath);
            }
        }
        finally
        {
            stack.Remove(modId);
        }
    }

    private static void TryDeleteDownload(string packagePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(packagePath);
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch
        {
            // A temporary download can be cleaned by the OS later.
        }
    }
}

public sealed class RedLoaderService : IDisposable
{
    private const long MaximumReleaseBytes = 1024L * 1024L * 1024L;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RedLoaderService(HttpMessageHandler? handler = null)
    {
        _httpClient = new HttpClient(handler ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        })
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CrazyBatto-RedManager", "2.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public bool IsInstalled(string gameDirectory) =>
        Directory.Exists(Path.Combine(gameDirectory, "_RedLoader")) &&
        Directory.Exists(Path.Combine(gameDirectory, "Mods"));

    public async Task<string> InstallOrUpdateAsync(
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!GameLocator.IsGameDirectory(gameDirectory))
        {
            throw new DirectoryNotFoundException("Der Sons-of-the-Forest-Spielordner ist ungültig.");
        }

        progress?.Report("Rufe das aktuelle offizielle RedLoader-Release ab …");
        using var response = await _httpClient.GetAsync(
            "https://api.github.com/repos/ToniMacaroni/RedLoader/releases/latest",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GithubRelease>(stream, _jsonOptions, cancellationToken)
                      ?? throw new InvalidDataException("Das RedLoader-Release konnte nicht gelesen werden.");
        var asset = release.Assets
            .Where(item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ScoreAsset)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Das aktuelle RedLoader-Release enthält kein Windows-ZIP-Archiv.");

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var assetUri) ||
            !string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Die RedLoader-Downloadadresse ist ungültig oder nicht HTTPS.");
        }

        progress?.Report($"Lade RedLoader {release.TagName} herunter …");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "CrazyBatto-RedManager", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var packagePath = Path.Combine(temporaryDirectory, Path.GetFileName(asset.Name));
        try
        {
            using var download = await _httpClient.GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            download.EnsureSuccessStatusCode();
            if (download.Content.Headers.ContentLength is > MaximumReleaseBytes)
            {
                throw new InvalidDataException("Das RedLoader-Archiv überschreitet 1 GiB.");
            }

            await using (var source = await download.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = File.Create(packagePath))
            {
                await CopyLimitedAsync(source, destination, MaximumReleaseBytes, cancellationToken);
            }

            progress?.Report("Prüfe und installiere RedLoader …");
            await ExtractLoaderArchiveAsync(packagePath, gameDirectory, cancellationToken);
            return release.TagName;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
            catch
            {
                // Temporary cleanup is best effort.
            }
        }
    }

    private static async Task ExtractLoaderArchiveAsync(string packagePath, string gameDirectory, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > 8192)
        {
            throw new InvalidDataException("Das RedLoader-Archiv enthält unerwartet viele Dateien.");
        }

        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();
        var wrapper = DetectWrapper(files);
        long total = 0;
        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = entry.FullName.Replace('\\', '/').Trim('/');
            if (!string.IsNullOrWhiteSpace(wrapper) && normalized.StartsWith(wrapper + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[(wrapper.Length + 1)..];
            }

            ValidateTrustedLoaderEntry(entry, normalized);
            total = checked(total + entry.Length);
            if (total > MaximumReleaseBytes)
            {
                throw new InvalidDataException("Das entpackte RedLoader-Archiv überschreitet 1 GiB.");
            }

            var destination = LocalModIndex.SafeCombine(gameDirectory, normalized);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = File.Create(destination);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static void ValidateTrustedLoaderEntry(ZipArchiveEntry entry, string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized) || Regex.IsMatch(normalized, "^[a-zA-Z]:"))
        {
            throw new InvalidDataException($"Ungültiger Pfad im RedLoader-Archiv: {entry.FullName}");
        }
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.Contains('\0')))
        {
            throw new InvalidDataException($"Pfad-Traversal im RedLoader-Archiv: {entry.FullName}");
        }
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixMode == 0xA000)
        {
            throw new InvalidDataException($"Symbolischer Link im RedLoader-Archiv: {entry.FullName}");
        }
        if (entry.Length > MaximumReleaseBytes)
        {
            throw new InvalidDataException($"Ungewöhnlich große RedLoader-Datei: {entry.FullName}");
        }
    }

    private static string? DetectWrapper(IEnumerable<ZipArchiveEntry> entries)
    {
        var segments = entries
            .Select(entry => entry.FullName.Replace('\\', '/').Trim('/'))
            .Where(path => path.Contains('/'))
            .Select(path => path.Split('/', 2)[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (segments.Count != 1)
        {
            return null;
        }

        var first = segments[0];
        return first is "_RedLoader" or "Mods" or "Libs" or "UserData" ? null : first;
    }

    private static int ScoreAsset(GithubAsset asset)
    {
        var name = asset.Name.ToLowerInvariant();
        var score = 0;
        if (name.Contains("redloader")) score += 100;
        if (name.Contains("windows")) score += 60;
        if (Regex.IsMatch(name, "(^|[-_.])win($|[-_.])")) score += 50;
        if (name.Contains("x64") || name.Contains("amd64")) score += 30;
        if (name.Contains("source")) score -= 200;
        return score;
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Download überschreitet die erlaubte Maximalgröße.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "Unbekannt";

        [JsonPropertyName("assets")]
        public List<GithubAsset> Assets { get; set; } = [];
    }

    private sealed class GithubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
