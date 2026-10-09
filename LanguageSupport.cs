using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Klavrio;

public enum SupportedLanguage
{
    English,
    Hebrew,
    Arabic,
    Russian,
    German,
    French
}

public sealed record LanguageDefinition(
    SupportedLanguage Id,
    string Code,
    string EnglishName,
    string NativeName,
    string Keys);

public static class LanguageCatalog
{
    private const string PhysicalKeys = "qwertyuiopasdfghjklzxcvbnm,.;/[]'";
    private static readonly IReadOnlyDictionary<SupportedLanguage, LanguageDefinition> Languages =
        new Dictionary<SupportedLanguage, LanguageDefinition>
        {
            [SupportedLanguage.English] = new(SupportedLanguage.English, "en", "English", "English", PhysicalKeys),
            [SupportedLanguage.Hebrew] = new(SupportedLanguage.Hebrew, "he", "Hebrew", "עברית", "/'קראטוןםפשדגכעיחלךזסבהנמצתץף.][,"),
            [SupportedLanguage.Arabic] = new(SupportedLanguage.Arabic, "ar", "Arabic", "العربية", "ضصثقفغعهخحشسيبلاتنمئءؤرلاىوزكظجدط"),
            [SupportedLanguage.Russian] = new(SupportedLanguage.Russian, "ru", "Russian", "Русский", "йцукенгшщзфывапролдячсмитьбюж.хъэ"),
            [SupportedLanguage.German] = new(SupportedLanguage.German, "de", "German", "Deutsch", "qwertzuiopasdfghjklyxcvbnm,.ö-ü+ä"),
            [SupportedLanguage.French] = new(SupportedLanguage.French, "fr", "French", "Français", "azertyuiopqsdfghjklwxcvbn,;:m!^$ù")
        };

    static LanguageCatalog()
    {
        foreach (LanguageDefinition language in Languages.Values)
        {
            if (language.Keys.Length != PhysicalKeys.Length)
                throw new InvalidOperationException($"Keyboard map length mismatch for {language.EnglishName}: {language.Keys.Length} != {PhysicalKeys.Length}");
        }
    }

    public static IReadOnlyCollection<LanguageDefinition> All { get; } = Languages.Values.ToArray();
    public static LanguageDefinition Get(SupportedLanguage id) => Languages[id];

    public static SupportedLanguage? DetectScript(string value)
    {
        SupportedLanguage? found = null;
        foreach (char c in value)
        {
            if (!char.IsLetter(c))
                continue;

            SupportedLanguage current = c switch
            {
                >= '\u05d0' and <= '\u05ea' => SupportedLanguage.Hebrew,
                _ when IsArabic(c) => SupportedLanguage.Arabic,
                _ when IsCyrillic(c) => SupportedLanguage.Russian,
                _ => SupportedLanguage.English
            };
            if (found is not null && found != current)
                return null;

            found = current;
        }
        return found;
    }

    public static string Convert(string input, SupportedLanguage source, SupportedLanguage target)
    {
        if (source == target || input.Length == 0)
            return input;

        LanguageDefinition from = Get(source);
        LanguageDefinition to = Get(target);
        var positions = new Dictionary<char, int>();
        for (int i = 0; i < from.Keys.Length; i++)
            positions.TryAdd(from.Keys[i], i);

        var output = new StringBuilder(input.Length);
        foreach (char original in input)
        {
            if (!positions.TryGetValue(char.ToLowerInvariant(original), out int position))
            {
                output.Append(original);
                continue;
            }

            char converted = to.Keys[position];
            output.Append(char.IsUpper(original) && IsLatin(converted) ? char.ToUpperInvariant(converted) : converted);
        }
        return output.ToString();
    }

    public static string ConvertTo(string input, SupportedLanguage target)
    {
        var output = new StringBuilder(input.Length);
        foreach (char character in input)
        {
            SupportedLanguage source = DetectScript(character.ToString()) ?? SupportedLanguage.English;
            if (source == target && target is not (SupportedLanguage.German or SupportedLanguage.French))
                output.Append(character);
            else
                output.Append(Convert(character.ToString(), source, target));
        }
        return output.ToString();
    }

    private static bool IsLatin(char c)
    {
        UnicodeCategory category = char.GetUnicodeCategory(c);
        return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' ||
               c is >= '\u00c0' and <= '\u024f' && category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter;
    }
    private static bool IsArabic(char c) => c is >= '\u0600' and <= '\u06ff' or >= '\u0750' and <= '\u077f' or >= '\u08a0' and <= '\u08ff';
    private static bool IsCyrillic(char c) => c is >= '\u0400' and <= '\u052f';
}

internal sealed class DictionaryRepository
{
    private readonly Dictionary<SupportedLanguage, HashSet<string>> words = new();
    private readonly Dictionary<string, string> exactHebrewPairs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> phoneticHebrew = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> hebrewPriority = new(StringComparer.OrdinalIgnoreCase);

    internal DictionaryRepository(IEnumerable<string> dataDirectories)
    {
        foreach (LanguageDefinition language in LanguageCatalog.All)
            words[language.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] directories = dataDirectories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (int index = 0; index < directories.Length; index++)
        {
            string directory = directories[index];
            bool overrideMappings = index > 0;
            LoadLooseDirectory(directory, overrideMappings);
            foreach (string zipPath in Directory.EnumerateFiles(directory, "*.zip", SearchOption.TopDirectoryOnly))
                LoadZipPackage(zipPath, overrideMappings);
        }

        if (words[SupportedLanguage.English].Count == 0 || words[SupportedLanguage.Hebrew].Count == 0)
            throw new InvalidDataException("Klavrio requires non-empty English and Hebrew dictionaries.");

        CrashLogger.Info("Dictionaries loaded | " + string.Join(" | ", LanguageCatalog.All.Select(x => $"{x.Code}={words[x.Id].Count}")) +
            $" | exactHe={exactHebrewPairs.Count} | phoneticHe={phoneticHebrew.Count} | priorities={hebrewPriority.Count}");
    }

    internal bool Contains(SupportedLanguage language, string value) => words[language].Contains(Normalize(value));
    internal bool ContainsForAutomatic(SupportedLanguage language, string value)
    {
        string normalized = Normalize(value);
        return words[language].Contains(normalized) &&
               (language != SupportedLanguage.Hebrew || !hebrewPriority.TryGetValue(normalized, out int priority) || priority >= 30);
    }
    internal int Count(SupportedLanguage language) => words[language].Count;
    internal bool TryExactHebrew(string value, out string converted, bool automatic = false)
    {
        if (!exactHebrewPairs.TryGetValue(Normalize(value), out converted!))
            return false;

        return !automatic || !hebrewPriority.TryGetValue(Normalize(converted), out int priority) || priority >= 30;
    }
    internal bool TryPhoneticHebrew(string value, out string converted) => phoneticHebrew.TryGetValue(Normalize(value), out converted!);

    private void LoadLooseDirectory(string directory, bool overrideMappings)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                string name = Path.GetFileName(path);
                if (name.Equals("hebrew_key_pairs.tsv", StringComparison.OrdinalIgnoreCase))
                    LoadPairs(File.ReadLines(path), overrideMappings);
                else if (name.Equals("phonetic_manual.tsv", StringComparison.OrdinalIgnoreCase) || name.Equals("phonetic.tsv", StringComparison.OrdinalIgnoreCase))
                    LoadPhonetic(File.ReadLines(path));
                else if (name.Equals("hebrew_priority.tsv", StringComparison.OrdinalIgnoreCase))
                    LoadPriorities(File.ReadLines(path));
                else if (TryGetLanguage(name, out SupportedLanguage language))
                    LoadWords(File.ReadLines(path), language);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CrashLogger.Record($"Load dictionary {Path.GetFileName(path)}", exception);
            }
        }
    }

    private void LoadZipPackage(string path, bool overrideMappings)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            long expandedBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                expandedBytes += entry.Length;
                if (expandedBytes > 40L * 1024 * 1024)
                    throw new InvalidDataException("Expanded dictionary package is larger than 40 MB.");
                if (entry.Length == 0 || entry.Length > 20L * 1024 * 1024 || entry.FullName.Contains('/') || entry.FullName.Contains('\\'))
                    continue;

                using Stream stream = entry.Open();
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
                IEnumerable<string> lines = ReadAllLines(reader);
                if (entry.Name.Equals("hebrew_key_pairs.tsv", StringComparison.OrdinalIgnoreCase))
                    LoadPairs(lines, overrideMappings);
                else if (entry.Name.Equals("phonetic_manual.tsv", StringComparison.OrdinalIgnoreCase))
                    LoadPhonetic(lines);
                else if (entry.Name.Equals("hebrew_priority.tsv", StringComparison.OrdinalIgnoreCase))
                    LoadPriorities(lines);
                else if (TryGetLanguage(entry.Name, out SupportedLanguage language))
                    LoadWords(lines, language);
            }
            CrashLogger.Info($"Dictionary package loaded | file={Path.GetFileName(path)} | entries={archive.Entries.Count}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            CrashLogger.Record($"Load dictionary package {Path.GetFileName(path)}", exception);
        }
    }

    private static IEnumerable<string> ReadAllLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }

    private void LoadWords(IEnumerable<string> lines, SupportedLanguage language)
    {
        foreach (string line in lines)
        {
            string word = Normalize(line);
            if (word.Length > 0 && !word.StartsWith('#'))
                words[language].Add(word);
        }
    }

    private void LoadPairs(IEnumerable<string> lines, bool manualOverride)
    {
        foreach (string line in lines)
        {
            if (line.StartsWith('#') || line.StartsWith("typed_", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length < 2)
                continue;

            string key = Normalize(fields[0]);
            string value = Normalize(fields[1]);
            if (key.Length == 0 || value.Length == 0)
                continue;

            if (manualOverride || !exactHebrewPairs.ContainsKey(key))
                exactHebrewPairs[key] = value;
        }
    }

    private void LoadPhonetic(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (line.StartsWith('#') || line.StartsWith("latin_", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length < 2)
                continue;

            string key = Normalize(fields[0]);
            string value = Normalize(fields[1]);
            if (key.Length > 0 && value.Length > 0)
                phoneticHebrew[key] = value;
        }
    }

    private void LoadPriorities(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (line.StartsWith('#') || line.StartsWith("hebrew_", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] fields = line.Split('\t');
            if (fields.Length >= 2 && int.TryParse(fields[1], out int priority))
                hebrewPriority[Normalize(fields[0])] = Math.Clamp(priority, 0, 100);
        }
    }

    private static bool TryGetLanguage(string filename, out SupportedLanguage language)
    {
        string name = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
        if (name.Contains("phrases") || name.Contains("priority") || name.Contains("key_pairs") || name.Contains("generated_unverified"))
        {
            language = default;
            return false;
        }

        foreach (LanguageDefinition candidate in LanguageCatalog.All)
        {
            string english = candidate.EnglishName.ToLowerInvariant();
            if (name == candidate.Code || name == english || name.StartsWith(english + "_") || name == $"words_{candidate.Code}")
            {
                language = candidate.Id;
                return true;
            }
        }

        language = default;
        return false;
    }

    private static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
