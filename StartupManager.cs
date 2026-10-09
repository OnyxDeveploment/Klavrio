using Microsoft.Win32;

namespace Klavrio;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Klavrio";

    internal static bool IsEnabled()
    {
        try
        {
            bool registryEnabled = RegistryEntryMatchesCurrentExecutable();
            bool startupShortcutEnabled = ShortcutManager.StartupShortcutMatchesCurrentExecutable();
            if (registryEnabled && startupShortcutEnabled)
                return true;

            // Repair an older or partially-created configuration so both requested
            // current-user startup mechanisms stay in sync.
            return (registryEnabled || startupShortcutEnabled) && SetEnabled(true);
        }
        catch (Exception exception)
        {
            CrashLogger.Record("Read startup setting", exception);
            return false;
        }
    }

    internal static bool SetEnabled(bool enabled)
    {
        if (enabled)
        {
            try
            {
                string executable = ShortcutManager.CurrentExecutablePath;
                ShortcutManager.SetStartupShortcut(true);
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true);
                key.SetValue(ValueName, BuildRunCommand(executable), RegistryValueKind.String);
                CrashLogger.Success("Start with Windows enabled | registry=true | startupFolder=true");
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    ShortcutManager.SetStartupShortcut(false);
                }
                catch
                {
                    // The original startup error is more useful than a cleanup error.
                }

                try
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true);
                    key.DeleteValue(ValueName, false);
                }
                catch
                {
                    // Best effort rollback; the next settings check will repair it.
                }

                CrashLogger.Record("Enable Windows startup", exception);
                return false;
            }
        }

        Exception? failure = null;
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key.DeleteValue(ValueName, false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            ShortcutManager.SetStartupShortcut(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
        {
            CrashLogger.Record("Disable Windows startup", failure);
            return false;
        }

        CrashLogger.Success("Start with Windows disabled | registry=false | startupFolder=false");
        return true;
    }

    private static bool RegistryEntryMatchesCurrentExecutable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value.Trim(), BuildRunCommand(ShortcutManager.CurrentExecutablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRunCommand(string executable) => $"\"{executable}\" --startup";
}
