using System.Reflection;

namespace Klavrio;

internal static class BundledNoticesManager
{
    private const string ResourcePrefix = "Klavrio.Legal.";

    private static readonly string[] RootFiles =
    {
        "LICENSE",
        "THIRD_PARTY_NOTICES.txt"
    };

    private static readonly string[] ThirdPartyFiles =
    {
        "APACHE-2.0.txt",
        "DOTNET_RUNTIME_LICENSE.txt",
        "DOTNET_RUNTIME_THIRD_PARTY_NOTICES.txt",
        "PICTOGRAMMERS_LICENSE.txt",
        "WPF_LICENSE.txt",
        "WPF_THIRD_PARTY_NOTICES.txt"
    };

    internal static string Ensure()
    {
        string besideExecutable = AppContext.BaseDirectory;
        if (IsComplete(besideExecutable)) return besideExecutable;

        try
        {
            ExtractTo(besideExecutable);
            CrashLogger.Success($"Legal notices restored beside executable | files={RootFiles.Length + ThirdPartyFiles.Length}");
            return besideExecutable;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CrashLogger.Warning($"Could not restore legal notices beside executable; using local application data | type={exception.GetType().Name}");
        }

        string localCopy = Path.Combine(AppSettings.DirectoryPath, "Legal");
        ExtractTo(localCopy);
        CrashLogger.Success($"Legal notices restored in local application data | files={RootFiles.Length + ThirdPartyFiles.Length}");
        return localCopy;
    }

    internal static void ExtractTo(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        string thirdPartyDirectory = Path.Combine(rootDirectory, "ThirdParty");
        Directory.CreateDirectory(thirdPartyDirectory);

        foreach (string fileName in RootFiles)
            Extract(ResourcePrefix + fileName, Path.Combine(rootDirectory, fileName));

        foreach (string fileName in ThirdPartyFiles)
            Extract(ResourcePrefix + "ThirdParty." + fileName, Path.Combine(thirdPartyDirectory, fileName));

        if (!IsComplete(rootDirectory))
            throw new InvalidDataException($"The restored Klavrio legal notices are incomplete: {rootDirectory}");
    }

    internal static bool IsComplete(string rootDirectory) =>
        RootFiles.All(fileName => IsUsable(Path.Combine(rootDirectory, fileName))) &&
        ThirdPartyFiles.All(fileName => IsUsable(Path.Combine(rootDirectory, "ThirdParty", fileName)));

    private static void Extract(string resourceName, string targetPath)
    {
        if (IsUsable(targetPath)) return;

        using Stream resource = typeof(BundledNoticesManager).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The embedded legal resource '{resourceName}' is missing.");

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

    private static bool IsUsable(string path) => File.Exists(path) && new FileInfo(path).Length > 0;
}
