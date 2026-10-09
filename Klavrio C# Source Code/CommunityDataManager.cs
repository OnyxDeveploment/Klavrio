namespace Klavrio;

internal static class CommunityDataManager
{
    internal static readonly string DirectoryPath = Path.Combine(AppSettings.DirectoryPath, "CommunityData");
    internal static readonly string ActivePackagePath = Path.Combine(DirectoryPath, "community-dictionaries.zip");
    internal static readonly string PreviousPackagePath = Path.Combine(DirectoryPath, "community-dictionaries.previous.zip");

    internal static bool HasActivePackage => File.Exists(ActivePackagePath);

    internal static void EnsureDirectory() => Directory.CreateDirectory(DirectoryPath);

    internal static void Activate(string validatedPackagePath)
    {
        EnsureDirectory();
        if (File.Exists(ActivePackagePath))
        {
            if (File.Exists(PreviousPackagePath)) File.Delete(PreviousPackagePath);
            File.Replace(validatedPackagePath, ActivePackagePath, PreviousPackagePath, true);
        }
        else
        {
            File.Move(validatedPackagePath, ActivePackagePath);
        }
    }

    internal static bool RestorePrevious()
    {
        if (!File.Exists(PreviousPackagePath)) return false;
        if (File.Exists(ActivePackagePath))
            File.Replace(PreviousPackagePath, ActivePackagePath, null, true);
        else
            File.Move(PreviousPackagePath, ActivePackagePath);
        return true;
    }
}
