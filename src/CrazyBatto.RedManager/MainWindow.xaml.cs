using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace CrazyBatto.RedManager;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SettingsService _settingsService = new();
    private readonly GameLocator _gameLocator = new();
    private readonly SafeArchiveInstaller _archiveInstaller = new();
    private readonly LocalModIndex _localIndex = new();
    private readonly RedLoaderService _redLoaderService = new();
    private readonly string _sessionLogPath;

    private AppSettings _settings = new();
    private SotfModsApiClient? _apiClient;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureCreated();
        _sessionLogPath = Path.Combine(AppPaths.LogsDirectory, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync("Initialisiere RedManager", async cancellationToken =>
        {
            _settings = await _settingsService.LoadAsync(cancellationToken);
            RecreateApiClient();

            var located = await _gameLocator.LocateAsync(_settings.GameDirectory, cancellationToken);
            if (!string.IsNullOrWhiteSpace(located))
            {
                _settings.GameDirectory = located;
                GamePathBox.Text = located;
                await _settingsService.SaveAsync(_settings, cancellationToken);
                AppendLog($"Sons of the Forest erkannt: {located}");
                UpdateRedLoaderStatus();
                await LoadInstalledAsync(cancellationToken);
            }
            else
            {
                GamePathBox.Text = "Nicht gefunden – bitte Spielordner wählen";
                RedLoaderStatusText.Text = "RedLoader: Spielordner fehlt";
                AppendLog("Sons of the Forest wurde nicht automatisch gefunden. Der Ordner kann oben manuell gewählt werden.");
            }

            try
            {
                await LoadOnlineAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                AppendLog($"Online-Mods konnten beim Start nicht geladen werden: {exception.Message}");
                StatusText.Text = "Lokaler Modmanager bereit; Online-API derzeit nicht verfügbar";
            }
        });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _lifetime.Cancel();
        _apiClient?.Dispose();
        _redLoaderService.Dispose();
        _lifetime.Dispose();
    }

    private async void ChooseGameFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Wähle den Ordner aus, in dem SonsOfTheForest.exe liegt.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = GameLocator.IsGameDirectory(_settings.GameDirectory) ? _settings.GameDirectory : string.Empty
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        if (!GameLocator.IsGameDirectory(dialog.SelectedPath))
        {
            MessageBox.Show(
                "Im ausgewählten Ordner wurde SonsOfTheForest.exe nicht gefunden.",
                "Ungültiger Spielordner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await ExecuteAsync("Speichere Spielordner", async cancellationToken =>
        {
            _settings.GameDirectory = Path.GetFullPath(dialog.SelectedPath);
            GamePathBox.Text = _settings.GameDirectory;
            await _settingsService.SaveAsync(_settings, cancellationToken);
            AppendLog($"Spielordner gesetzt: {_settings.GameDirectory}");
            UpdateRedLoaderStatus();
            await LoadInstalledAsync(cancellationToken);
        });
    }

    private async void SearchOnline_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync("Suche Online-Mods", LoadOnlineAsync);
    }

    private async void RefreshOnline_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync("Aktualisiere Online-Mods", LoadOnlineAsync);
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ExecuteAsync("Suche Online-Mods", LoadOnlineAsync);
    }

    private void OnlineGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (OnlineGrid.SelectedItem is not OnlineMod mod)
        {
            OnlineNameText.Text = "Kein Mod ausgewählt";
            OnlineMetaText.Text = string.Empty;
            OnlineDependencyText.Text = string.Empty;
            OnlineDescriptionText.Text = string.Empty;
            return;
        }

        OnlineNameText.Text = mod.Name;
        OnlineMetaText.Text = $"Autor: {mod.DisplayAuthor}  ·  Version: {mod.DisplayVersion}  ·  Typ: {mod.Type ?? "Mod"}\n" +
                              $"Mehrspieler: {mod.MultiplayerText}  ·  Downloads: {mod.Downloads:N0}";
        OnlineDependencyText.Text = mod.Dependencies.Count == 0
            ? "Keine veröffentlichten Abhängigkeiten"
            : "Abhängigkeiten: " + string.Join(", ", mod.Dependencies);
        OnlineDescriptionText.Text = StripUnsafeMarkup(mod.DisplayDescription);
    }

    private async void InstallSelectedMod_Click(object sender, RoutedEventArgs e)
    {
        if (OnlineGrid.SelectedItem is not OnlineMod selected)
        {
            MessageBox.Show("Bitte zuerst einen Mod auswählen.", "RedManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetGameDirectory(out var gameDirectory))
        {
            return;
        }

        await ExecuteAsync($"Installiere {selected.Name}", async cancellationToken =>
        {
            var api = _apiClient ?? throw new InvalidOperationException("Die Online-API ist nicht initialisiert.");
            var manager = new ModManagerService(api, _archiveInstaller, _localIndex);
            var progress = CreateProgress();
            await manager.InstallAsync(selected, gameDirectory, progress, cancellationToken);
            await LoadInstalledAsync(cancellationToken);
            MainTabs.SelectedIndex = 1;
            MessageBox.Show(
                $"{selected.Name} und seine benötigten Abhängigkeiten wurden installiert.",
                "Installation abgeschlossen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private async void RefreshInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetGameDirectory(out _))
        {
            return;
        }

        await ExecuteAsync("Aktualisiere lokalen Mod-Index", LoadInstalledAsync);
    }

    private async void ToggleInstalledMod_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledGrid.SelectedItem is not InstalledMod selected)
        {
            MessageBox.Show("Bitte zuerst einen installierten Mod auswählen.", "RedManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetGameDirectory(out var gameDirectory))
        {
            return;
        }

        var targetState = !selected.Enabled;
        await ExecuteAsync(targetState ? $"Aktiviere {selected.Name}" : $"Deaktiviere {selected.Name}", async cancellationToken =>
        {
            await _localIndex.SetEnabledAsync(selected, gameDirectory, targetState, cancellationToken);
            AppendLog($"{selected.Name}: {(targetState ? "aktiviert" : "deaktiviert")}");
            await LoadInstalledAsync(cancellationToken);
        });
    }

    private async void UninstallSelectedMod_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledGrid.SelectedItem is not InstalledMod selected)
        {
            MessageBox.Show("Bitte zuerst einen installierten Mod auswählen.", "RedManager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetGameDirectory(out var gameDirectory))
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Soll '{selected.Name}' wirklich entfernt werden?\n\nVor dem Löschen legt RedManager eine Sicherung unter %LOCALAPPDATA%\\Crazy_Batto\\RedManager\\backups an.",
            "Mod deinstallieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await ExecuteAsync($"Deinstalliere {selected.Name}", async cancellationToken =>
        {
            await _localIndex.UninstallAsync(selected, gameDirectory, cancellationToken);
            AppendLog($"Deinstalliert und gesichert: {selected.Name}");
            await LoadInstalledAsync(cancellationToken);
        });
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetGameDirectory(out var gameDirectory))
        {
            return;
        }

        var mods = Path.Combine(gameDirectory, "Mods");
        Directory.CreateDirectory(mods);
        Process.Start(new ProcessStartInfo
        {
            FileName = mods,
            UseShellExecute = true
        });
    }

    private async void InstallRedLoader_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetGameDirectory(out var gameDirectory))
        {
            return;
        }

        await ExecuteAsync("Installiere oder aktualisiere RedLoader", async cancellationToken =>
        {
            var version = await _redLoaderService.InstallOrUpdateAsync(gameDirectory, CreateProgress(), cancellationToken);
            AppendLog($"RedLoader {version} wurde installiert beziehungsweise aktualisiert.");
            UpdateRedLoaderStatus();
            await LoadInstalledAsync(cancellationToken);
            MessageBox.Show(
                $"RedLoader {version} wurde erfolgreich installiert beziehungsweise aktualisiert.",
                "RedLoader",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetGameDirectory(out var gameDirectory))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://rungameid/{GameLocator.SteamAppId}",
                UseShellExecute = true
            });
            AppendLog("Sons of the Forest wird über Steam gestartet.");
            StatusText.Text = "Spielstart an Steam übergeben";
        }
        catch (Exception steamError)
        {
            var executable = GameLocator.FindExecutable(gameDirectory);
            if (executable is null)
            {
                throw new InvalidOperationException("Weder Steam noch SonsOfTheForest.exe konnten gestartet werden.", steamError);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = gameDirectory,
                UseShellExecute = true
            });
            AppendLog("Steam-Protokoll nicht verfügbar; Spiel direkt gestartet.");
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        AppendLog("Sichtbares Protokoll geleert.");
    }

    private async Task LoadOnlineAsync(CancellationToken cancellationToken)
    {
        var api = _apiClient ?? throw new InvalidOperationException("Die Online-API ist nicht initialisiert.");
        var mods = await api.GetModsAsync(SearchBox.Text, _settings.IncludeNsfw, 1, 100, cancellationToken);
        OnlineGrid.ItemsSource = mods;
        if (mods.Count > 0)
        {
            OnlineGrid.SelectedIndex = 0;
        }
        else
        {
            OnlineGrid_SelectionChanged(this, new System.Windows.Controls.SelectionChangedEventArgs(
                System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                Array.Empty<object>(),
                Array.Empty<object>()));
        }

        AppendLog($"Online-Liste geladen: {mods.Count} Mods{(string.IsNullOrWhiteSpace(SearchBox.Text) ? string.Empty : $" für Suche '{SearchBox.Text.Trim()}'")}.");
        StatusText.Text = $"{mods.Count} Online-Mods geladen";
    }

    private async Task LoadInstalledAsync(CancellationToken cancellationToken)
    {
        if (!TryGetGameDirectory(out var gameDirectory, showMessage: false))
        {
            InstalledGrid.ItemsSource = Array.Empty<InstalledMod>();
            return;
        }

        var mods = await _localIndex.ScanAsync(gameDirectory, cancellationToken);
        InstalledGrid.ItemsSource = mods;
        AppendLog($"Lokaler Index geladen: {mods.Count} Einträge.");
        StatusText.Text = $"{mods.Count} installierte Mods und Bibliotheken erkannt";
        UpdateRedLoaderStatus();
    }

    private async Task ExecuteAsync(string operation, Func<CancellationToken, Task> action)
    {
        if (_isBusy)
        {
            StatusText.Text = "Ein anderer Vorgang läuft bereits";
            return;
        }

        _isBusy = true;
        BusyBar.Visibility = Visibility.Visible;
        StatusText.Text = operation + " …";
        AppendLog(operation + " …");
        try
        {
            await action(_lifetime.Token);
            if (!_lifetime.IsCancellationRequested)
            {
                StatusText.Text = operation + " abgeschlossen";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            StatusText.Text = "Vorgang abgebrochen";
        }
        catch (Exception exception)
        {
            AppendLog($"FEHLER: {exception}");
            StatusText.Text = "Fehler: " + exception.Message;
            MessageBox.Show(
                exception.Message,
                "Crazy_Batto RedManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BusyBar.Visibility = Visibility.Collapsed;
            _isBusy = false;
        }
    }

    private bool TryGetGameDirectory(out string gameDirectory, bool showMessage = true)
    {
        gameDirectory = _settings.GameDirectory ?? string.Empty;
        if (GameLocator.IsGameDirectory(gameDirectory))
        {
            return true;
        }

        if (showMessage)
        {
            MessageBox.Show(
                "Bitte zuerst den Sons-of-the-Forest-Spielordner auswählen.",
                "Spielordner fehlt",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        return false;
    }

    private void UpdateRedLoaderStatus()
    {
        if (!TryGetGameDirectory(out var gameDirectory, showMessage: false))
        {
            RedLoaderStatusText.Text = "RedLoader: Spielordner fehlt";
            return;
        }

        RedLoaderStatusText.Text = _redLoaderService.IsInstalled(gameDirectory)
            ? "RedLoader: installiert"
            : "RedLoader: nicht erkannt";
    }

    private void RecreateApiClient()
    {
        _apiClient?.Dispose();
        _apiClient = new SotfModsApiClient(_settings.ApiBaseUrl);
    }

    private IProgress<string> CreateProgress() => new Progress<string>(message =>
    {
        StatusText.Text = message;
        AppendLog(message);
    });

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
        try
        {
            File.AppendAllText(_sessionLogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging must never interrupt a mod operation.
        }
    }

    private static string StripUnsafeMarkup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Keine Beschreibung verfügbar.";
        }

        var withoutScripts = System.Text.RegularExpressions.Regex.Replace(
            value,
            "<script.*?</script>|<style.*?</style>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var withoutTags = System.Text.RegularExpressions.Regex.Replace(withoutScripts, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(withoutTags, "[ \\t]+", " "))
            .Replace("\r\n", "\n")
            .Trim();
    }
}
