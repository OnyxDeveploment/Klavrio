using System.Reflection;

namespace Klavrio;

internal static class BundledDataManager
{
    private const string ResourcePrefix = "Klavrio.BundledData.";

    private static readonly string[] Files =
    {
        "arabic.txt",
        "COMMUNITY_DICTIONARY_LICENSES.txt",
        "english.txt",
        "french.txt",
        "german.txt",
        "hebrew.txt",
        "hebrew_key_pairs.tsv",
        "hebrew_priority.tsv",
        "phonetic_manual.tsv",
        "README.txt",
        "russian.txt",
        "THIRD_PARTY_LICENSE.txt"
    };

    internal static string Resolve()
    {
        string besideExecutable = Path.Combine(AppContext.BaseDirectory, "Data");
        if (IsComplete(besideExecutable)) return besideExecutable;

        Exception? besideExecutableError = null;
        try
        {
            ExtractTo(besideExecutable);
            CrashLogger.Success($"Bundled Data folder restored beside executable | files={Files.Length}");
            return besideExecutable;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            besideExecutableError = exception;
            CrashLogger.Warning($"Could not restore Data beside executable; using local application data | type={exception.GetType().Name}");
        }

        string localCopy = Path.Combine(AppSettings.DirectoryPath, "BundledData");
        try
        {
            ExtractTo(localCopy);
            CrashLogger.Success($"Bundled Data folder restored in local application data | files={Files.Length}");
            return localCopy;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Klavrio could not restore its embedded dictionaries beside the executable or in {localCopy}.",
                new AggregateException(besideExecutableError ?? exception, exception));
        }
    }

    internal static void ExtractTo(string directory)
    {
        Directory.CreateDirectory(directory);
        Assembly assembly = typeof(BundledDataManager).Assembly;

        foreach (string fileName in Files)
        {
            string targetPath = Path.Combine(directory, fileName);
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0) continue;

            string resourceName = ResourcePrefix + fileName;
            using Stream resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"The embedded data resource '{resourceName}' is missing.");

            string temporaryPath = targetPath + $".tmp-{Environment.ProcessId}-{Guid.NewGuid():N}";
            try
            {
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    resource.CopyTo(output);
                    output.Flush(true);
                }

                File.Move(temporaryPath, targetPath, true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { }
            }
        }

        if (!IsComplete(directory))
            throw new InvalidDataException($"The restored Klavrio Data folder is incomplete: {directory}");
    }

    internal static bool IsComplete(string directory) => Files.All(fileName =>
    {
        string path = Path.Combine(directory, fileName);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    });
}
