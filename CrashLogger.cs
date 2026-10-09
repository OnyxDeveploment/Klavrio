using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Klavrio;

internal static class CrashLogger
{
    private const string LogFormatMarker = "# Klavrio diagnostic log v1";
    private const string Divider = "================================================================================";
    private static readonly object Gate = new();
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];
    private static readonly DateTimeOffset SessionStartedAt = DateTimeOffset.Now;
    private static bool storagePrepared;
    private static bool sessionHeaderWritten;
    public static readonly string DirectoryPath = ResolveDirectoryPath();
    public static readonly string LogPath = Path.Combine(DirectoryPath, "klavrio.log");

    private static string ResolveDirectoryPath()
    {
        string? testOverride = Environment.GetEnvironmentVariable("KLAVRIO_LOG_DIRECTORY");
        return !string.IsNullOrWhiteSpace(testOverride)
            ? Path.GetFullPath(testOverride)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Klavrio", "Logs");
    }

    public static void StartSession()
    {
        PruneOldCrashReports();
        try
        {
            lock (Gate)
            {
                PrepareStorage();
                RotateIfNeeded();
                WriteSessionHeader();
            }
        }
        catch { /* Diagnostics must never crash the application. */ }
    }

    public static void EndSession()
    {
        TimeSpan uptime = DateTimeOffset.Now - SessionStartedAt;
        Success($"Application exited normally | uptime={FormatDuration(uptime)}");
    }

    public static void Record(string area, Exception exception, bool fatal = false)
    {
        Write(fatal ? "FATAL" : "ERROR", area, exception.ToString());
        if (fatal) WriteCrashReport(area, exception);
    }

    public static void Info(string eventName) => Write("INFO", eventName);
    public static void Success(string eventName) => Write("SUCCESS", eventName);
    public static void Warning(string eventName) => Write("WARN", eventName);

    private static void Write(string level, string eventName, string? detail = null)
    {
        try
        {
            lock (Gate)
            {
                PrepareStorage();
                if (RotateIfNeeded()) WriteSessionHeader();
                else if (!sessionHeaderWritten) WriteSessionHeader();

                var entry = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append("  ")
                    .Append(DisplayLevel(level).PadRight(5))
                    .Append("  ")
                    .AppendLine(FormatEvent(eventName));
                if (!string.IsNullOrWhiteSpace(detail)) AppendDetail(entry, detail);
                File.AppendAllText(LogPath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch { /* Diagnostics must never crash the application. */ }
    }

    private static void PrepareStorage()
    {
        if (storagePrepared) return;
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(LogPath) && !UsesCurrentFormat(LogPath))
        {
            string legacyPath = Path.Combine(DirectoryPath,
                $"klavrio-legacy-{DateTime.Now:yyyyMMdd-HHmmss}-{SessionId}.log");
            File.Move(LogPath, legacyPath);
        }
        EnsureFormatPreamble();
        storagePrepared = true;
    }

    private static bool RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length <= 2_000_000) return false;
        string archive = Path.Combine(DirectoryPath, "klavrio.previous.log");
        File.Move(LogPath, archive, true);
        sessionHeaderWritten = false;
        EnsureFormatPreamble();
        return true;
    }

    private static void EnsureFormatPreamble()
    {
        if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 0) return;
        File.WriteAllText(LogPath,
            LogFormatMarker + Environment.NewLine +
            "# Typed and selected text are never recorded." + Environment.NewLine,
            Encoding.UTF8);
    }

    private static bool UsesCurrentFormat(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            return string.Equals(reader.ReadLine(), LogFormatMarker, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static void WriteSessionHeader()
    {
        if (sessionHeaderWritten) return;
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "unknown";
        var header = new StringBuilder()
            .AppendLine()
            .AppendLine(Divider)
            .AppendLine($"Klavrio {version} session started")
            .AppendLine($"Date:     {SessionStartedAt:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine($"Session:  {SessionId}    Process: {Environment.ProcessId}")
            .AppendLine($"System:   {RuntimeInformation.OSDescription}")
            .AppendLine($"Runtime:  {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})")
            .AppendLine(Divider);
        File.AppendAllText(LogPath, header.ToString(), Encoding.UTF8);
        sessionHeaderWritten = true;
    }

    private static string FormatEvent(string value)
    {
        string[] parts = OneLine(value).Split(" | ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return parts.FirstOrDefault() ?? "(empty event)";
        return parts[0] + "  |  " + string.Join("  |  ", parts.Skip(1).Select(FormatField));
    }

    private static string FormatField(string field)
    {
        int separator = field.IndexOf('=');
        if (separator <= 0) return field;
        string key = field[..separator];
        string value = field[(separator + 1)..];
        return key switch
        {
            "elapsedMs" => $"elapsed: {value} ms",
            "bytes" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) =>
                $"size: {FormatBytes(bytes)}",
            _ => $"{key}: {value}"
        };
    }

    private static void AppendDetail(StringBuilder builder, string detail)
    {
        foreach (string line in detail.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            builder.Append("                    ").AppendLine(line.TrimEnd());
    }

    private static string DisplayLevel(string level) => level == "SUCCESS" ? "OK" : level;

    private static string OneLine(string value) =>
        value.Replace("\r", " ").Replace("\n", " ").Replace('"', '\'').Trim();

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} B"
    };

    private static void WriteCrashReport(string area, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{SessionId}.log");
            var report = new StringBuilder()
                .AppendLine("Klavrio crash report")
                .AppendLine($"Time: {DateTimeOffset.Now:O}")
                .AppendLine($"Session: {SessionId}")
                .AppendLine($"Area: {area}")
                .AppendLine($"OS: {RuntimeInformation.OSDescription}")
                .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
                .AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}")
                .AppendLine($"Working set: {Environment.WorkingSet:N0} bytes")
                .AppendLine()
                .AppendLine(exception.ToString());
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    private static void PruneOldCrashReports()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath)) return;
            foreach (string path in Directory.EnumerateFiles(DirectoryPath, "crash-*.log"))
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-30)) File.Delete(path);
        }
        catch { }
    }

    public static void OpenFolder()
    {
        Directory.CreateDirectory(DirectoryPath);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DirectoryPath}\"") { UseShellExecute = true });
    }
}
