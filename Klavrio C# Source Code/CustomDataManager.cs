using System.Diagnostics;
using System.Text;

namespace Klavrio;

internal static class CustomDataManager
{
    internal static readonly string DirectoryPath = Path.Combine(AppSettings.DirectoryPath, "CustomData");

    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>
    {
        ["README.txt"] = """
            Klavrio custom dictionaries (UTF-8)

            Add one word per line to the language files. Blank lines and lines beginning with # are ignored.
            Klavrio merges these files with the bundled package when the app starts.

            hebrew_key_pairs.tsv format (TAB-separated):
            typed_english_layout<TAB>hebrew_result
            Example: eurt<TAB>קורא

            phonetic.tsv format (TAB-separated):
            latin_spelling<TAB>hebrew_word
            Example: kore<TAB>קורה

            Manual mappings override bundled mappings. Save, then use Reload dictionaries in Settings.
            Keep a backup of this folder before making large changes.
            """,
        ["english.txt"] = "# One English word per line\n",
        ["hebrew.txt"] = "# מילה אחת בעברית בכל שורה\n",
        ["arabic.txt"] = "# كلمة عربية واحدة في كل سطر\n",
        ["russian.txt"] = "# Одно русское слово в строке\n",
        ["german.txt"] = "# Ein deutsches Wort pro Zeile\n",
        ["french.txt"] = "# Un mot français par ligne\n",
        ["hebrew_key_pairs.tsv"] = "typed_english_layout\thebrew_result\n# eurt\tקורא\n",
        ["phonetic.tsv"] = "latin_spelling\thebrew_word\n# kore\tקורה\n# ma\tמה\n"
    };

    internal static void EnsureFiles()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            foreach ((string filename, string content) in Templates)
            {
                string path = Path.Combine(DirectoryPath, filename);
                if (File.Exists(path))
                    continue;

                File.WriteAllText(path, content.ReplaceLineEndings(), new UTF8Encoding(true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CrashLogger.Record("Create custom dictionary templates", exception);
        }
    }

    internal static void OpenFolder()
    {
        EnsureFiles();
        Process.Start(new ProcessStartInfo(DirectoryPath) { UseShellExecute = true });
    }
}
