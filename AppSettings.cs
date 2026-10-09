using System.Text.Json;

namespace Klavrio;

internal sealed class AppSettings
{
    internal const string ProjectUrl = "https://github.com/OnyxDeveploment/Klavrio";
    internal const string ContributeUrl = ProjectUrl + "/blob/main/CONTRIBUTING.md";
    internal const string DictionaryFeedUrl = "https://github.com/OnyxDeveploment/Klavrio/releases/download/dictionary-feed/dictionary-manifest.json";
    internal const string UpdateFeedUrl = "https://github.com/OnyxDeveploment/Klavrio/klavrio-update.json";
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klavrio");
    internal static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public bool AutomaticCorrectionEnabled { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public int SelectionMode { get; set; }
    public string UiLanguage { get; set; } = "en";
    public string Theme { get; set; } = "Dark";
    public string GitHubUrl { get; set; } = ProjectUrl;
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string UpdateManifestUrl { get; set; } = UpdateFeedUrl;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public bool CheckDictionaryUpdatesOnStartup { get; set; } = true;
    public string DictionaryManifestUrl { get; set; } = DictionaryFeedUrl;
    public string InstalledDictionaryVersion { get; set; } = "";
    public DateTimeOffset? LastDictionaryUpdateCheckUtc { get; set; }
    public HotkeyBinding SelectionHotkey { get; set; } = HotkeyBinding.SelectionDefault;
    public HotkeyBinding ToggleHotkey { get; set; } = HotkeyBinding.ToggleDefault;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    internal static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            string json = File.ReadAllText(FilePath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            using (JsonDocument document = JsonDocument.Parse(json))
                if (!document.RootElement.TryGetProperty(nameof(SchemaVersion), out _)) settings.SchemaVersion = 0;
            if (settings.ApplyMigrations()) settings.Save();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLogger.Record("Load settings", exception);
            return new AppSettings();
        }
    }

    internal bool ApplyMigrations()
    {
        bool changed = false;

        if (!string.Equals(GitHubUrl, ProjectUrl, StringComparison.Ordinal))
        {
            GitHubUrl = ProjectUrl;
            changed = true;
        }

        if (!string.Equals(DictionaryManifestUrl, DictionaryFeedUrl, StringComparison.Ordinal))
        {
            DictionaryManifestUrl = DictionaryFeedUrl;
            changed = true;
        }

        if (!string.Equals(UpdateManifestUrl, UpdateFeedUrl, StringComparison.Ordinal))
        {
            UpdateManifestUrl = UpdateFeedUrl;
            changed = true;
        }

        if (SchemaVersion != CurrentSchemaVersion)
        {
            SchemaVersion = CurrentSchemaVersion;
            changed = true;
        }

        return changed;
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporaryPath, FilePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CrashLogger.Record("Save settings", exception);
        }
    }
}
