using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Klavrio;

internal enum DictionaryUpdateState { UpToDate, Installed }

internal sealed record DictionaryUpdateResult(DictionaryUpdateState State, string DataVersion, int Files, long Bytes);

internal static partial class DictionaryUpdateService
{
    private const long MaximumPackageBytes = 40L * 1024 * 1024;
    private const long MaximumEntryBytes = 20L * 1024 * 1024;
    private const int MaximumEntries = 32;
    private const int MaximumLineLength = 512;
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<DictionaryUpdateResult> CheckAndInstallAsync(
        string manifestUrl,
        string installedVersion,
        CancellationToken cancellationToken = default)
    {
        Uri manifestUri = RequireHttps(manifestUrl, "dictionary manifest");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        DictionaryManifest manifest = await DownloadManifestAsync(manifestUri, timeout.Token);
        ValidateManifest(manifest);

        if (!string.IsNullOrWhiteSpace(manifest.MinimumAppVersion) &&
            ParseVersion(manifest.MinimumAppVersion, "minimumAppVersion") > CurrentAppVersion())
            throw new InvalidDataException($"Dictionary data {manifest.DataVersion} requires a newer Klavrio version.");

        if (CommunityDataManager.HasActivePackage &&
            string.Equals(installedVersion, manifest.DataVersion, StringComparison.OrdinalIgnoreCase))
            return new(DictionaryUpdateState.UpToDate, manifest.DataVersion!, 0, 0);

        Uri packageUri = RequireHttps(manifest.PackageUrl!, "dictionary package");
        CommunityDataManager.EnsureDirectory();
        string temporaryPath = Path.Combine(CommunityDataManager.DirectoryPath, $"download-{Guid.NewGuid():N}.tmp");
        try
        {
            long downloadedBytes = await DownloadPackageAsync(packageUri, temporaryPath, timeout.Token);
            string actualHash = await ComputeSha256Async(temporaryPath, timeout.Token);
            if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The dictionary package SHA-256 does not match its manifest.");

            int files = ValidatePackage(temporaryPath);
            CommunityDataManager.Activate(temporaryPath);
            CrashLogger.Success($"Community dictionaries installed | version={manifest.DataVersion} | files={files} | bytes={downloadedBytes}");
            return new(DictionaryUpdateState.Installed, manifest.DataVersion!, files, downloadedBytes);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<DictionaryManifest> DownloadManifestAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 65_536)
            throw new InvalidDataException("The dictionary manifest is larger than 64 KB.");
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > 65_536) throw new InvalidDataException("The dictionary manifest is larger than 64 KB.");
        return JsonSerializer.Deserialize<DictionaryManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The dictionary manifest is empty.");
    }

    private static async Task<long> DownloadPackageAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
            throw new InvalidDataException("The dictionary package is larger than 40 MB.");

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, true);
        byte[] buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumPackageBytes) throw new InvalidDataException("The dictionary package is larger than 40 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return total;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, true);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static int ValidatePackage(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count is 0 or > MaximumEntries)
            throw new InvalidDataException($"Dictionary package must contain 1-{MaximumEntries} files.");

        long expandedBytes = 0;
        int dataFiles = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName != entry.Name || entry.Name.Length == 0 || !names.Add(entry.Name))
                throw new InvalidDataException("Dictionary package contains a duplicate, nested, or invalid filename.");
            if (!IsAllowedName(entry.Name))
                throw new InvalidDataException($"Dictionary package contains an unsupported file: {entry.Name}");
            if (entry.Length > MaximumEntryBytes)
                throw new InvalidDataException($"Dictionary file is too large: {entry.Name}");
            expandedBytes += entry.Length;
            if (expandedBytes > MaximumPackageBytes)
                throw new InvalidDataException("Expanded dictionary package is larger than 40 MB.");

            if (!IsDataName(entry.Name)) continue;
            ValidateTextEntry(entry);
            dataFiles++;
        }
        if (dataFiles == 0) throw new InvalidDataException("Dictionary package contains no supported data files.");
        return dataFiles;
    }

    private static void ValidateTextEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length > MaximumLineLength || line.Contains('\0'))
                throw new InvalidDataException($"Invalid text at {entry.Name}:{lineNumber}.");
        }
    }

    private static bool IsAllowedName(string name) =>
        IsDataName(name) || name.Equals("README.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("THIRD_PARTY_LICENSE.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("COMMUNITY_DICTIONARY_LICENSES.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("dictionary-package.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsDataName(string name) => LanguageFileName().IsMatch(name) ||
        name.Equals("hebrew_key_pairs.tsv", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("phonetic.tsv", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("phonetic_manual.tsv", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("hebrew_priority.tsv", StringComparison.OrdinalIgnoreCase);

    private static void ValidateManifest(DictionaryManifest manifest)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported dictionary manifest schema.");
        if (string.IsNullOrWhiteSpace(manifest.DataVersion) || manifest.DataVersion.Length > 64)
            throw new InvalidDataException("Dictionary manifest has an invalid dataVersion.");
        _ = RequireHttps(manifest.PackageUrl!, "dictionary package");
        if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Sha256Value().IsMatch(manifest.Sha256))
            throw new InvalidDataException("Dictionary manifest has an invalid SHA-256 value.");
    }

    private static Uri RequireHttps(string value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"The {field} URL must use HTTPS.");
        return uri;
    }

    private static Version ParseVersion(string? value, string field)
    {
        string normalized = (value ?? "").Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(normalized, out Version? version))
            throw new InvalidDataException($"Dictionary manifest has an invalid {field}.");
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static Version CurrentAppVersion()
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Klavrio-DictionaryUpdater/1.0");
        return client;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Value();

    [GeneratedRegex("^(english|hebrew|arabic|russian|german|french)(_[a-z0-9_-]+)?\\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguageFileName();

    private sealed class DictionaryManifest
    {
        public int SchemaVersion { get; set; }
        public string? DataVersion { get; set; }
        public string? PackageUrl { get; set; }
        public string? Sha256 { get; set; }
        public string? MinimumAppVersion { get; set; }
    }
}
