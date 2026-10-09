using Klavrio;

string smokeLogDirectory = Directory.CreateTempSubdirectory("KlavrioSmokeLogs-").FullName;
Environment.SetEnvironmentVariable("KLAVRIO_LOG_DIRECTORY", smokeLogDirectory);
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try { Directory.Delete(smokeLogDirectory, true); }
    catch { }
};

var engine = new CorrectionEngine(Path.Combine(AppContext.BaseDirectory, "Data"));

AssertEqual("שלום", engine.Translate("akuo"), "English-layout to Hebrew mapping");
AssertEqual("akuo", engine.Translate("שלום"), "Hebrew-layout to English mapping");
AssertEqual("שלום", engine.Suggest("akuo"), "Dictionary-backed automatic suggestion");
AssertEqual("מה קורה", SelectionConverter.Convert("nv eurv", SelectionDirection.ToHebrew), "Forced selected-text conversion");
AssertEqual("מה קורה hello", SelectionConverter.Convert("ma kore hello", SelectionDirection.Auto), "Smart mixed selection");
AssertEqual("hello", SelectionConverter.Convert("hello", SelectionDirection.Auto), "Known English preservation");
AssertEqual("unknownword", SelectionConverter.Convert("unknownword", SelectionDirection.Auto), "Unknown token preservation");
AssertTrue(typeof(CorrectionEngine).Assembly.GetManifestResourceNames().Contains("Klavrio.Logo.png"), "Embedded logo resource");
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

Console.WriteLine("Klavrio smoke tests passed (41/41).");
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
