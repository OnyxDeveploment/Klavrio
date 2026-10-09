namespace Klavrio;

public sealed class CorrectionEngine
{
    private readonly string[] dataDirectories;
    private volatile DictionaryRepository dictionaries;
    public int MinimumLength { get; set; } = 2;

    public CorrectionEngine(params string[] dataDirectories)
    {
        this.dataDirectories = dataDirectories.ToArray();
        dictionaries = new DictionaryRepository(this.dataDirectories);
    }

    public void Reload() => dictionaries = new DictionaryRepository(dataDirectories);

    public static bool IsWordLetter(char ch) => char.IsLetter(ch) || ch is ',' or '.' or ';' or '/' or '[' or ']' or '\'';

    public string? Translate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        SupportedLanguage? source = LanguageCatalog.DetectScript(input);
        if (source == SupportedLanguage.Hebrew)
            return LanguageCatalog.Convert(input, SupportedLanguage.Hebrew, SupportedLanguage.English);
        if (source == SupportedLanguage.English)
            return LanguageCatalog.Convert(input, SupportedLanguage.English, SupportedLanguage.Hebrew);
        return null;
    }

    public string Translate(string input, SupportedLanguage target) => LanguageCatalog.ConvertTo(input, target);

    public string? Suggest(string input, SupportedLanguage? preferredTarget = null)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length < MinimumLength || input.Length > 48) return null;
        string normalized = input.Trim().ToLowerInvariant();

        if (dictionaries.TryExactHebrew(normalized, out string exact, automatic: true) && !string.Equals(exact, input, StringComparison.Ordinal))
            return exact;

        SupportedLanguage? detected = LanguageCatalog.DetectScript(input);
        if (detected is null) return null;
        SupportedLanguage source = detected.Value;
        bool alreadyKnown = dictionaries.Contains(source, normalized);

        if (!alreadyKnown && preferredTarget is SupportedLanguage preferred && preferred != source)
        {
            string preferredCandidate = LanguageCatalog.Convert(input, source, preferred);
            if (!string.Equals(preferredCandidate, input, StringComparison.Ordinal) &&
                dictionaries.Contains(preferred, preferredCandidate))
                return preferredCandidate;
        }

        var candidates = new List<string>();
        foreach (LanguageDefinition target in LanguageCatalog.All)
        {
            if (target.Id == source) continue;
            string candidate = LanguageCatalog.Convert(input, source, target.Id);
            if (string.Equals(candidate, input, StringComparison.Ordinal) || !dictionaries.ContainsForAutomatic(target.Id, candidate)) continue;
            candidates.Add(candidate);
        }

        return !alreadyKnown && candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() == 1
            ? candidates[0]
            : null;
    }

    public bool IsKnownWord(SupportedLanguage language, string value) => dictionaries.Contains(language, value);
    public int GetWordCount(SupportedLanguage language) => dictionaries.Count(language);
    public bool TryExactHebrew(string value, out string converted) => dictionaries.TryExactHebrew(value, out converted);
    public bool TryPhoneticHebrew(string value, out string converted) => dictionaries.TryPhoneticHebrew(value, out converted);
}
