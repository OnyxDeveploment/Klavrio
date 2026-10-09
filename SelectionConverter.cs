using System.Text;
using System.Text.RegularExpressions;

namespace Klavrio;

public enum SelectionDirection
{
    Auto = 0,
    ToHebrew = 1,
    ToEnglish = 2,
    ToArabic = 3,
    ToRussian = 4,
    ToGerman = 5,
    ToFrench = 6
}

public static class SelectionConverter
{
    private static readonly Lazy<CorrectionEngine> FallbackEngine = new(() => new CorrectionEngine(Path.Combine(AppContext.BaseDirectory, "Data")));
    private static CorrectionEngine? configuredEngine;
    private static CorrectionEngine Engine => configuredEngine ?? FallbackEngine.Value;
    private static readonly Dictionary<string, string> Phonetic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ma"] = "מה",
        ["kore"] = "קורה",
        ["kara"] = "קרה",
        ["ata"] = "אתה",
        ["at"] = "את",
        ["ani"] = "אני",
        ["anu"] = "אנו",
        ["anachnu"] = "אנחנו",
        ["aih"] = "איך",
        ["eich"] = "איך",
        ["ech"] = "איך",
        ["lama"] = "למה",
        ["shalom"] = "שלום",
        ["toda"] = "תודה",
        ["tov"] = "טוב",
        ["ken"] = "כן",
        ["lo"] = "לא",
        ["ze"] = "זה",
        ["zot"] = "זאת",
        ["yom"] = "יום",
        ["hayom"] = "היום",
        ["achshav"] = "עכשיו",
        ["sababa"] = "סבבה",
        ["beseder"] = "בסדר",
        ["rotze"] = "רוצה",
        ["roze"] = "רוצה",
        ["eifo"] = "איפה",
        ["mi"] = "מי",
        ["achla"] = "אחלה",
        ["bye"] = "ביי"
    };
    private static readonly Regex Token = new(@"[\p{L}]+(?:[,.;/'\[\]][\p{L}]+)*[,.;/'\[\]]*|[^\p{L}]+", RegexOptions.Compiled);

    public static void Configure(CorrectionEngine engine) => configuredEngine = engine;

    public static string Convert(string text, SelectionDirection mode)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (mode != SelectionDirection.Auto) return LanguageCatalog.ConvertTo(text, TargetFor(mode));

        SupportedLanguage? preferredTarget = DetectPreferredTarget(text);
        var output = new StringBuilder(text.Length);
        foreach (Match token in Token.Matches(text))
        {
            string part = token.Value;
            if (!part.Any(char.IsLetter)) { output.Append(part); continue; }

            string lower = part.ToLowerInvariant();
            if (Engine.TryExactHebrew(lower, out string exact)) { output.Append(exact); continue; }
            string? suggestion = Engine.Suggest(part, preferredTarget);
            if (suggestion is not null) { output.Append(suggestion); continue; }
            if (Engine.TryPhoneticHebrew(lower, out string? phonetic) || Phonetic.TryGetValue(lower, out phonetic))
            { output.Append(phonetic); continue; }

            int end = part.Length;
            while (end > 0 && ",.;/[]'".Contains(part[end - 1])) end--;
            if (end > 0 && end < part.Length)
            {
                string stem = part[..end];
                if (Engine.TryExactHebrew(stem, out exact)) { output.Append(exact).Append(part.AsSpan(end)); continue; }
                suggestion = Engine.Suggest(stem, preferredTarget);
                if (suggestion is not null) { output.Append(suggestion).Append(part.AsSpan(end)); continue; }
            }
            output.Append(part);
        }
        return output.ToString();
    }

    private static SupportedLanguage? DetectPreferredTarget(string text)
    {
        var counts = new Dictionary<SupportedLanguage, int>();
        foreach (char character in text)
        {
            if (!char.IsLetter(character)) continue;
            SupportedLanguage? language = LanguageCatalog.DetectScript(character.ToString());
            if (language is null or SupportedLanguage.English) continue;
            counts[language.Value] = counts.GetValueOrDefault(language.Value) + 1;
        }

        if (counts.Count == 0) return null;
        int highest = counts.Values.Max();
        SupportedLanguage[] leaders = counts.Where(pair => pair.Value == highest).Select(pair => pair.Key).ToArray();
        return leaders.Length == 1 ? leaders[0] : null;
    }

    private static SupportedLanguage TargetFor(SelectionDirection direction) => direction switch
    {
        SelectionDirection.ToHebrew => SupportedLanguage.Hebrew,
        SelectionDirection.ToEnglish => SupportedLanguage.English,
        SelectionDirection.ToArabic => SupportedLanguage.Arabic,
        SelectionDirection.ToRussian => SupportedLanguage.Russian,
        SelectionDirection.ToGerman => SupportedLanguage.German,
        SelectionDirection.ToFrench => SupportedLanguage.French,
        _ => SupportedLanguage.English
    };
}
