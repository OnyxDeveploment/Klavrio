using Klavrio;

string smokeLogDirectory = Directory.CreateTempSubdirectory("KlavrioSmokeLogs-").FullName;
Environment.SetEnvironmentVariable("KLAVRIO_LOG_DIRECTORY", smokeLogDirectory);
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try { Directory.Delete(smokeLogDirectory, true); }
    catch { }
};

CrashLogger.StartSession();
AssertTrue(Directory.Exists(smokeLogDirectory), "Diagnostic directory creation");
AssertTrue(File.Exists(CrashLogger.LogPath), "Diagnostic log creation");
Directory.Delete(smokeLogDirectory, true);
CrashLogger.Info("Log directory recreation test");
AssertTrue(Directory.Exists(smokeLogDirectory), "Diagnostic directory recreation");
AssertTrue(File.Exists(CrashLogger.LogPath), "Diagnostic log recreation");

var defaultSettings = new AppSettings();
AssertEqual(AppSettings.ProjectUrl, defaultSettings.GitHubUrl, "Default GitHub project URL");
AssertEqual(AppSettings.DictionaryFeedUrl, defaultSettings.DictionaryManifestUrl, "Default dictionary feed URL");
AssertEqual("https://github.com/OnyxDeveploment/Klavrio/klavrio-update.json",
    AppSettings.UpdateFeedUrl, "Official application update feed URL");
AssertEqual("https://api.github.com/repos/OnyxDeveploment/Klavrio/contents/klavrio-update.json",
    UpdateChecker.ResolveManifestUrl(AppSettings.UpdateFeedUrl), "GitHub manifest alias resolution");
AssertEqual(AppSettings.UpdateFeedUrl, defaultSettings.UpdateManifestUrl, "Default application update feed URL");
var legacySettings = new AppSettings
{
    SchemaVersion = 0,
    GitHubUrl = "https://github.com/",
    DictionaryManifestUrl = "",
    UpdateManifestUrl = "https://github.com/legacy/klavrio-update.json"
};
AssertTrue(legacySettings.ApplyMigrations(), "Legacy settings migration applied");
AssertEqual(AppSettings.ProjectUrl, legacySettings.GitHubUrl, "Migrated GitHub project URL");
AssertEqual(AppSettings.DictionaryFeedUrl, legacySettings.DictionaryManifestUrl, "Migrated dictionary feed URL");
AssertEqual(AppSettings.UpdateFeedUrl, legacySettings.UpdateManifestUrl, "Migrated application update feed URL");
AssertTrue(legacySettings.SchemaVersion == 2, "Migrated settings schema version");

const string validUpdateManifest = """
{
  "version": "1.0.0.1",
  "url": "https://github.com/OnyxDeveploment/Klavrio/releases/download/v1.0.0.1/Klavrio.exe"
}
""";
UpdateCheckResult parsedUpdate = UpdateChecker.ParseManifest(validUpdateManifest, new Version(1, 0, 0, 0));
AssertTrue(parsedUpdate.State == UpdateState.Available, "Application update version detection");
const string alternateDownloadUrl = "https://github.com/AnotherOwner/AnotherRepository/releases/download/stable/KlavrioSetup.exe";
UpdateCheckResult alternateUpdate = UpdateChecker.ParseManifest(
    validUpdateManifest.Replace(
        "https://github.com/OnyxDeveploment/Klavrio/releases/download/v1.0.0.1/Klavrio.exe",
        alternateDownloadUrl),
    new Version(1, 0, 0, 0));
AssertEqual(alternateDownloadUrl, alternateUpdate.DownloadUrl, "Manifest-owned application download URL");
const string githubReleaseMetadata = """
{
  "html_url": "https://github.com/OnyxDeveploment/Klavrio/releases/tag/v1.0.0.1",
  "assets": [{
    "name": "Klavrio.exe",
    "browser_download_url": "https://github.com/OnyxDeveploment/Klavrio/releases/download/v1.0.0.1/Klavrio.exe",
    "digest": "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    "size": 78209183
  }]
}
""";
UpdateCheckResult resolvedUpdate = UpdateChecker.ApplyGitHubAssetMetadata(parsedUpdate, githubReleaseMetadata);
AssertEqual("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    resolvedUpdate.Sha256, "GitHub release SHA-256 metadata");
AssertTrue(resolvedUpdate.DownloadSize == 78209183, "GitHub release size metadata");
AssertThrows<InvalidDataException>(() => UpdateChecker.ParseManifest(
    validUpdateManifest.Replace("{", "{\n  \"platform\": \"macos-universal\","), new Version(1, 0, 0, 0)),
    "Application update platform rejection");

var engine = new CorrectionEngine(Path.Combine(AppContext.BaseDirectory, "Data"));

AssertEqual("שלום", engine.Translate("akuo"), "English-layout to Hebrew mapping");
AssertEqual("akuo", engine.Translate("שלום"), "Hebrew-layout to English mapping");
AssertEqual("שלום", engine.Suggest("akuo"), "Dictionary-backed automatic suggestion");
AssertEqual("מה קורה", SelectionConverter.Convert("nv eurv", SelectionDirection.ToHebrew), "Forced selected-text conversion");
AssertEqual("מה קורה hello", SelectionConverter.Convert("ma kore hello", SelectionDirection.Auto), "Smart mixed selection");
AssertEqual("hello", SelectionConverter.Convert("hello", SelectionDirection.Auto), "Known English preservation");
AssertEqual("unknownword", SelectionConverter.Convert("unknownword", SelectionDirection.Auto), "Unknown token preservation");
AssertTrue(typeof(CorrectionEngine).Assembly.GetManifestResourceNames().Contains("Klavrio.Logo.png"), "Embedded logo resource");
string extractedDataDirectory = Directory.CreateTempSubdirectory("KlavrioEmbeddedData-").FullName;
try
{
    BundledDataManager.ExtractTo(extractedDataDirectory);
    AssertTrue(BundledDataManager.IsComplete(extractedDataDirectory), "Embedded dictionary extraction");
    var extractedEngine = new CorrectionEngine(extractedDataDirectory);
    AssertEqual("שלום", extractedEngine.Suggest("akuo"), "Extracted dictionary loading");
}
finally { Directory.Delete(extractedDataDirectory, true); }
string extractedNoticesDirectory = Directory.CreateTempSubdirectory("KlavrioEmbeddedNotices-").FullName;
try
{
    BundledNoticesManager.ExtractTo(extractedNoticesDirectory);
    AssertTrue(BundledNoticesManager.IsComplete(extractedNoticesDirectory), "Embedded legal notice extraction");
    AssertTrue(Directory.GetFiles(Path.Combine(extractedNoticesDirectory, "ThirdParty")).Length == 6,
        "Embedded ThirdParty file count");
}
finally { Directory.Delete(extractedNoticesDirectory, true); }
AssertEqual("קורא", engine.Suggest("eurt"), "Reviewed TSV mapping priority");
AssertEqual("מה", engine.Suggest("nv"), "Two-letter exact automatic mapping");
AssertEqual("אתה", engine.Suggest("t,v"), "Punctuation-bearing automatic mapping");
AssertTrue(CorrectionEngine.IsWordLetter(','), "Keyboard-layout punctuation buffering");
AssertEqual("מה אתה קורא", SelectionConverter.Convert("nv t,v eurt", SelectionDirection.Auto), "Full mixed Hebrew phrase repair");
AssertEqual("سلام", SelectionConverter.Convert("sghl", SelectionDirection.ToArabic), "Arabic physical-key conversion");
AssertEqual("привет", SelectionConverter.Convert("ghbdtn", SelectionDirection.ToRussian), "Russian physical-key conversion");
AssertEqual("zeit", SelectionConverter.Convert("yeit", SelectionDirection.ToGerman), "German physical-key conversion");
AssertEqual("avec", SelectionConverter.Convert("qvec", SelectionDirection.ToFrench), "French physical-key conversion");
AssertEqual("מה קורא", SelectionConverter.Convert("מה eurt", SelectionDirection.ToHebrew), "Mixed forced conversion preserves target script");
AssertEqual("سلام", engine.Suggest("sghl"), "Arabic automatic dictionary match");
AssertEqual("привет", engine.Suggest("ghbdtn"), "Russian automatic dictionary match");
AssertTrue(engine.GetWordCount(SupportedLanguage.English) >= 97_523, "Consolidated English dictionary");
AssertTrue(engine.GetWordCount(SupportedLanguage.Hebrew) >= 3_569, "Consolidated Hebrew vocabulary and names");
AssertTrue(engine.GetWordCount(SupportedLanguage.Hebrew) > 2_477, "Hebrew name-pack additions");
AssertTrue(engine.GetWordCount(SupportedLanguage.Arabic) >= 294_179, "Consolidated Arabic dictionary");
AssertTrue(engine.GetWordCount(SupportedLanguage.German) >= 163_678, "Consolidated German dictionary");
AssertTrue(engine.GetWordCount(SupportedLanguage.French) >= 80_457, "Consolidated French dictionary");
AssertTrue(engine.GetWordCount(SupportedLanguage.Russian) >= 146_232, "Consolidated Russian dictionary");
AssertEqual("היי מה קורה", SelectionConverter.Convert("VHH ma kore", SelectionDirection.Auto), "Mixed layout and phonetic preview phrase");
AssertEqual("אנגלית", SelectionConverter.Convert("anglit", SelectionDirection.Auto), "ZIP phonetic mapping");
AssertEqual("מה זה אתה בא לפה הרבה ?", SelectionConverter.Convert("מה זה אתה ct kpv הרבה ?", SelectionDirection.Auto), "User mixed phrase with short Hebrew-layout words");
AssertEqual("איך קוראים לך אתה נועם נאור כוהן לוי נאור דוד פו פה בא הנה שועל דניאל המספר שלי הוא",
    SelectionConverter.Convert("איך קוראים לך אתה bugo btur fuvi kuh btur sus pu פה ct vbv augk sbhtk vnxpr שלי הוא", SelectionDirection.Auto),
    "User names and common vocabulary phrase");
AssertEqual("קוראים לך או שקוראים לי", SelectionConverter.Convert("קוראים לך tu aeurtho לי", SelectionDirection.Auto), "User mixed phrase with prefixed word");
AssertEqual("נועם", engine.Suggest("bugo"), "Common Israeli name automatic match");
AssertEqual("אביגיל", engine.Suggest("tchdhk"), "Consolidated Hebrew name-pack match");
AssertNull(engine.Suggest("ct"), "Ambiguous short token is manual-selection only");
AssertNull(engine.Suggest("sus"), "Known Latin slang is not changed automatically");
AssertNull(engine.Suggest("cnahnv"), "Low-priority generated word is not automatic");
AssertEqual("במשימה", SelectionConverter.Convert("cnahnv", SelectionDirection.Auto), "Low-priority word remains available for manual smart conversion");

string customDirectory = Directory.CreateTempSubdirectory("KlavrioSmoke-").FullName;
try
{
    File.WriteAllText(Path.Combine(customDirectory, "hebrew_key_pairs.tsv"), "typed_english_layout\thebrew_result\nzzq\tבדיקה\n");
    File.WriteAllText(Path.Combine(customDirectory, "phonetic.tsv"), "latin_spelling\thebrew_word\nmytest\tבדיקה\n");
    var customEngine = new CorrectionEngine(Path.Combine(AppContext.BaseDirectory, "Data"), customDirectory);
    AssertTrue(customEngine.TryExactHebrew("zzq", out string customExact) && customExact == "בדיקה", "Custom exact mapping override");
    AssertTrue(customEngine.TryPhoneticHebrew("mytest", out string customPhonetic) && customPhonetic == "בדיקה", "Custom phonetic mapping");
    File.AppendAllText(Path.Combine(customDirectory, "hebrew_key_pairs.tsv"), "zzw\tמחדש\n");
    customEngine.Reload();
    AssertTrue(customEngine.TryExactHebrew("zzw", out string reloaded) && reloaded == "מחדש", "Live dictionary reload");
}
finally { Directory.Delete(customDirectory, true); }

Console.WriteLine("Klavrio smoke tests passed (62/62).");
return;

static void AssertEqual(string expected, string? actual, string name)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{name} failed. Expected '{expected}', got '{actual ?? "<null>"}'.");
}

static void AssertTrue(bool value, string name)
{
    if (!value) throw new InvalidOperationException($"{name} failed.");
}

static void AssertNull(string? actual, string name)
{
    if (actual is not null) throw new InvalidOperationException($"{name} failed. Expected null, got '{actual}'.");
}

static void AssertThrows<TException>(Action action, string name) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"{name} failed. Expected {typeof(TException).Name}.");
}
