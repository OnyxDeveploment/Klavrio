using System.Text.Json;

namespace Klavrio;

internal sealed class AppSettings
{
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
    public string GitHubUrl { get; set; } = "https://github.com/";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string UpdateManifestUrl { get; set; } = "";
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public bool CheckDictionaryUpdatesOnStartup { get; set; } = true;
    public string DictionaryManifestUrl { get; set; } = "";
    public string InstalledDictionaryVersion { get; set; } = "";
    public DateTimeOffset? LastDictionaryUpdateCheckUtc { get; set; }
    public HotkeyBinding SelectionHotkey { get; set; } = HotkeyBinding.SelectionDefault;
    public HotkeyBinding ToggleHotkey { get; set; } = HotkeyBinding.ToggleDefault;

    internal static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashLogger.Record("Load settings", exception);
            return new AppSettings();
        }
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
