using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace Klavrio;

internal static class UpdateInstaller
{
    internal const long MaximumDownloadBytes = 300L * 1024 * 1024;

    private static readonly string UpdateRoot = Path.Combine(AppSettings.DirectoryPath, "Updates");
    private static readonly HttpClient Client = CreateClient();

    internal static void CleanupStagedUpdates()
    {
        if (!Directory.Exists(UpdateRoot)) return;

        foreach (string directory in Directory.EnumerateDirectories(UpdateRoot))
        {
            if (Path.GetFileName(directory).Equals("Backup", StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(directory, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static async Task<string> DownloadAsync(
        UpdateCheckResult update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (update.State != UpdateState.Available || update.DownloadUrl is null || update.Sha256 is null ||
            update.DownloadSize is <= 0 or > MaximumDownloadBytes)
        {
            throw new InvalidDataException("The update metadata is incomplete.");
        }

        string versionDirectory = Path.Combine(UpdateRoot, update.LatestVersion.ToString());
        Directory.CreateDirectory(versionDirectory);
        string destination = Path.Combine(versionDirectory, "Klavrio.exe");
        string temporaryPath = destination + $".download-{Environment.ProcessId}-{Guid.NewGuid():N}";

        try
        {
            using HttpResponseMessage response = await Client.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != update.DownloadSize)
                throw new InvalidDataException("The downloaded update size does not match the update manifest.");

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true))
            {
                byte[] buffer = new byte[81_920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > update.DownloadSize || total > MaximumDownloadBytes)
                        throw new InvalidDataException("The downloaded update exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(total / (double)update.DownloadSize);
                }
                await output.FlushAsync(cancellationToken);
                if (total != update.DownloadSize)
                    throw new InvalidDataException("The downloaded update is incomplete.");
            }

            VerifyExecutable(temporaryPath, update.Sha256, update.DownloadSize);
            VerifyVersion(temporaryPath, update.LatestVersion);
            File.Move(temporaryPath, destination, true);
            CrashLogger.Success($"Application update downloaded and verified | version={update.LatestVersion} | bytes={update.DownloadSize}");
            return destination;
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
            throw;
        }
    }

    internal static void LaunchUpdater(string stagedExecutable, UpdateCheckResult update)
    {
        string targetExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Klavrio could not determine its executable path.");
        if (!Path.GetFileName(targetExecutable).Equals("Klavrio.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic replacement is available only from the packaged Klavrio.exe.");

        string stagedFullPath = Path.GetFullPath(stagedExecutable);
        string updateRootWithSeparator = Path.GetFullPath(UpdateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!stagedFullPath.StartsWith(updateRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged update is outside Klavrio's protected update folder.");

        VerifyExecutable(stagedFullPath, update.Sha256!, update.DownloadSize);
        VerifyVersion(stagedFullPath, update.LatestVersion);

        var startInfo = new ProcessStartInfo(stagedFullPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(stagedFullPath)!
        };
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(targetExecutable);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--sha256");
        startInfo.ArgumentList.Add(update.Sha256!);
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(update.LatestVersion.ToString());

        if (!CanWriteToDirectory(Path.GetDirectoryName(targetExecutable)!))
            startInfo.Verb = "runas";

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Windows could not start the Klavrio update helper.");
    }

    internal static bool TryRunUpdateHelper(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!args.Contains("--apply-update", StringComparer.OrdinalIgnoreCase)) return false;

        try
        {
            string target = RequiredArgument(args, "--target");
            string expectedHash = RequiredArgument(args, "--sha256").ToLowerInvariant();
            string version = RequiredArgument(args, "--version");
            if (!int.TryParse(RequiredArgument(args, "--wait-pid"), out int processId) || processId <= 0)
                throw new InvalidDataException("The update helper received an invalid process identifier.");

            ApplyUpdate(target, processId, expectedHash, version);
        }
        catch (Exception exception)
        {
            exitCode = -1;
            try
            {
                System.Windows.MessageBox.Show(
                    $"Klavrio could not install the update.\n\n{exception.Message}",
                    "Klavrio update", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch { }
        }

        return true;
    }

    private static void ApplyUpdate(string target, int processId, string expectedHash, string version)
    {
        string source = Environment.ProcessPath
            ?? throw new InvalidOperationException("The update helper could not determine its path.");
        string sourceFullPath = Path.GetFullPath(source);
        string updateRootWithSeparator = Path.GetFullPath(UpdateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!sourceFullPath.StartsWith(updateRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update helper is not running from Klavrio's update folder.");

        string targetFullPath = Path.GetFullPath(target);
        if (!Path.GetFileName(targetFullPath).Equals("Klavrio.exe", StringComparison.OrdinalIgnoreCase) ||
            targetFullPath.Equals(sourceFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update target is invalid.");
        }

        long sourceSize = new FileInfo(sourceFullPath).Length;
        VerifyExecutable(sourceFullPath, expectedHash, sourceSize);
        if (!Version.TryParse(version, out Version? expectedVersion))
            throw new InvalidDataException("The update helper received an invalid version.");
        VerifyVersion(sourceFullPath, expectedVersion);
        WaitForProcess(processId);

        string targetDirectory = Path.GetDirectoryName(targetFullPath)!;
        Directory.CreateDirectory(targetDirectory);
        string replacement = Path.Combine(targetDirectory, $".Klavrio.update-{Guid.NewGuid():N}.exe");
        string backupDirectory = Path.Combine(UpdateRoot, "Backup");
        Directory.CreateDirectory(backupDirectory);
        string backup = Path.Combine(backupDirectory, "Klavrio.previous.exe");

        try
        {
            if (File.Exists(targetFullPath)) File.Copy(targetFullPath, backup, true);
            File.Copy(sourceFullPath, replacement, true);
            File.Move(replacement, targetFullPath, true);

            var startInfo = new ProcessStartInfo(targetFullPath) { UseShellExecute = true, WorkingDirectory = targetDirectory };
            startInfo.ArgumentList.Add("--updated-from");
            startInfo.ArgumentList.Add(version);
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("The updated Klavrio executable could not be restarted.");
        }
        catch
        {
            if (File.Exists(backup)) File.Copy(backup, targetFullPath, true);
            throw;
        }
        finally
        {
            try { if (File.Exists(replacement)) File.Delete(replacement); }
            catch { }
        }
    }

    private static void VerifyExecutable(string path, string expectedHash, long expectedSize)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedSize || info.Length is <= 0 or > MaximumDownloadBytes)
            throw new InvalidDataException("The Klavrio update has an invalid file size.");

        using (var stream = File.OpenRead(path))
        {
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
                throw new InvalidDataException("The downloaded update is not a Windows executable.");
        }

        using FileStream hashStream = File.OpenRead(path);
        string actualHash = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
        {
            throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
        }
    }

    private static void VerifyVersion(string path, Version expectedVersion)
    {
        string? value = FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (!Version.TryParse((value ?? string.Empty).Split('-', '+')[0], out Version? actualVersion) ||
            NormalizeVersion(actualVersion) != NormalizeVersion(expectedVersion))
        {
            throw new InvalidDataException("The downloaded executable version does not match klavrio-update.json.");
        }
    }

    private static Version NormalizeVersion(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    private static void WaitForProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.WaitForExit(60_000))
                throw new TimeoutException("Klavrio did not close in time for the update.");
        }
        catch (ArgumentException)
        {
            // The previous process already exited.
        }
    }

    private static string RequiredArgument(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        throw new InvalidDataException($"The update helper is missing {name}.");
    }

    private static bool CanWriteToDirectory(string directory)
    {
        string probe = Path.Combine(directory, $".klavrio-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch { }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Klavrio-Updater/1.0");
        return client;
    }
}
