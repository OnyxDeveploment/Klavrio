using System.Runtime.InteropServices;

namespace Klavrio;

internal static class ShortcutManager
{
    private const string ShortcutFileName = "Klavrio.lnk";
    private const string StartupArguments = "--startup";

    internal static string CurrentExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("The Klavrio executable path is unavailable.");

    internal static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutFileName);

    internal static void EnsureUserLaunchShortcuts()
    {
        string executable = CurrentExecutablePath;
        string[] shortcutPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutFileName)
        ];

        int changed = 0;
        foreach (string shortcutPath in shortcutPaths)
        {
            try
            {
                if (ShortcutMatches(shortcutPath, executable, "")) continue;
                SaveShortcut(shortcutPath, executable, "");
                changed++;
            }
            catch (Exception exception)
            {
                CrashLogger.Record($"Create user shortcut | path={shortcutPath}", exception);
            }
        }

        if (changed > 0)
            CrashLogger.Success($"User shortcuts ready | createdOrUpdated={changed} | desktop=true | startMenu=true");
    }

    internal static bool StartupShortcutMatchesCurrentExecutable() =>
        ShortcutMatches(StartupShortcutPath, CurrentExecutablePath, StartupArguments);

    internal static void SetStartupShortcut(bool enabled)
    {
        if (enabled) SaveShortcut(StartupShortcutPath, CurrentExecutablePath, StartupArguments);
        else if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
    }

    private static bool ShortcutMatches(string shortcutPath, string targetPath, string arguments)
    {
        if (!File.Exists(shortcutPath)) return false;

        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            shellObject = CreateShell();
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            string existingTarget = shortcut.TargetPath as string ?? "";
            string existingArguments = shortcut.Arguments as string ?? "";
            return PathsEqual(existingTarget, targetPath) &&
                   string.Equals(existingArguments.Trim(), arguments, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static void SaveShortcut(string shortcutPath, string targetPath, string arguments)
    {
        string? directory = Path.GetDirectoryName(shortcutPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The shortcut directory is unavailable.");

        Directory.CreateDirectory(directory);
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            shellObject = CreateShell();
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
            shortcut.Description = "Klavrio keyboard-layout correction";
            shortcut.IconLocation = $"{targetPath},0";
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static object CreateShell()
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false);
        return shellType is null
            ? throw new InvalidOperationException("Windows Script Host shortcut support is unavailable.")
            : Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows shortcut support could not be started.");
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Shortcut cleanup must not prevent the application from opening.
        }
    }
}
