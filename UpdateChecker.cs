using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klavrio;

internal enum UpdateState
{
    UpToDate,
    Available
}

internal sealed record UpdateCheckResult(
    UpdateState State,
    Version CurrentVersion,
    Version LatestVersion,
    string? DownloadUrl,
    string? ReleaseNotesUrl,
    string? Sha256,
    long DownloadSize);

internal static class UpdateChecker
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<UpdateCheckResult> CheckAsync(string manifestUrl, CancellationToken cancellationToken = default)
    {
        string resolvedManifestUrl = ResolveManifestUrl(manifestUrl);
        bool usesGitHubContentsApi = IsGitHubContentsApiUrl(resolvedManifestUrl);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using HttpResponseMessage response = await Client.GetAsync(
            resolvedManifestUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        long maximumResponseBytes = usesGitHubContentsApi ? 131_072 : 65_536;
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumResponseBytes)
            throw new InvalidDataException("The update manifest response is too large.");

        string responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
        if (responseJson.Length > maximumResponseBytes)
            throw new InvalidDataException("The update manifest response is too large.");
        string json = usesGitHubContentsApi ? DecodeGitHubFileContent(responseJson) : responseJson;
        if (json.Length > 65_536)
            throw new InvalidDataException("The update manifest is larger than 64 KB.");

        UpdateCheckResult result = ParseManifest(json);
        if (result.State == UpdateState.Available &&
            (result.Sha256 is null || result.DownloadSize is <= 0 or > UpdateInstaller.MaximumDownloadBytes))
        {
            result = await ResolveGitHubAssetMetadataAsync(result, timeout.Token);
        }

        return result;
    }

    internal static string ResolveManifestUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The update manifest must use a complete HTTPS URL.", nameof(value));

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            segments.Length == 3 &&
            segments[2].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string owner = Uri.EscapeDataString(Uri.UnescapeDataString(segments[0]));
            string repository = Uri.EscapeDataString(Uri.UnescapeDataString(segments[1]));
            string file = Uri.EscapeDataString(Uri.UnescapeDataString(segments[2]));
            return $"https://api.github.com/repos/{owner}/{repository}/contents/{file}";
        }

        return uri.AbsoluteUri;
    }

    private static bool IsGitHubContentsApiUrl(string value)
    {
        var uri = new Uri(value);
        return uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.Contains("/contents/", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeGitHubFileContent(string json)
    {
        GitHubFileContent file = JsonSerializer.Deserialize<GitHubFileContent>(json, JsonOptions)
            ?? throw new InvalidDataException("GitHub returned empty update-manifest metadata.");
        if (!string.Equals(file.Encoding, "base64", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(file.Content))
            throw new InvalidDataException("GitHub returned an unsupported update-manifest encoding.");

        try
        {
            byte[] bytes = Convert.FromBase64String(file.Content);
            return new System.Text.UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.DecoderFallbackException)
        {
            throw new InvalidDataException("GitHub returned invalid update-manifest content.", exception);
        }
    }

    internal static UpdateCheckResult ParseManifest(string json, Version? currentVersion = null)
    {
        UpdateManifest manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The update manifest is empty.");
        if (!string.IsNullOrWhiteSpace(manifest.Platform) &&
            !string.Equals(manifest.Platform, "windows-x64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update manifest is not intended for Windows x64.");
        if (!TryParseVersion(manifest.Version, out Version? latest))
            throw new InvalidDataException("The update manifest contains an invalid version.");

        Version current = ComparableVersion(currentVersion ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0));
        latest = ComparableVersion(latest!);
        string? downloadUrl = ValidateOptionalHttps(manifest.Url ?? manifest.DownloadUrl, "url");
        string? releaseNotesUrl = ValidateOptionalHttps(manifest.ReleaseNotesUrl, "releaseNotesUrl");
        string? sha256 = ValidateSha256(manifest.Sha256);
        long downloadSize = manifest.Size ?? 0;

        if (latest > current)
        {
            if (downloadUrl is null)
                throw new InvalidDataException("The available update is missing its download URL.");
            ParseGitHubReleaseAssetUrl(downloadUrl);
        }

        return new(latest > current ? UpdateState.Available : UpdateState.UpToDate,
            current, latest, downloadUrl, releaseNotesUrl, sha256, downloadSize);
    }

    private static async Task<UpdateCheckResult> ResolveGitHubAssetMetadataAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken)
    {
        GitHubReleaseAssetLocation location = ParseGitHubReleaseAssetUrl(update.DownloadUrl!);
        string apiUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(location.Owner)}/{Uri.EscapeDataString(location.Repository)}/releases/tags/{Uri.EscapeDataString(location.Tag)}";
        using HttpResponseMessage response = await Client.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 262_144)
            throw new InvalidDataException("The GitHub release metadata is larger than 256 KB.");

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > 262_144)
            throw new InvalidDataException("The GitHub release metadata is larger than 256 KB.");

        return ApplyGitHubAssetMetadata(update, json);
    }

    internal static UpdateCheckResult ApplyGitHubAssetMetadata(UpdateCheckResult update, string json)
    {
        GitHubRelease release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions)
            ?? throw new InvalidDataException("The GitHub release metadata is empty.");
        GitHubReleaseAsset? asset = release.Assets?.FirstOrDefault(candidate =>
            string.Equals(candidate.BrowserDownloadUrl, update.DownloadUrl, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            throw new InvalidDataException("The update URL from klavrio-update.json was not found in the GitHub release.");

        string digest = asset.Digest ?? string.Empty;
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub did not provide a SHA-256 digest for the update file.");
        string sha256 = ValidateSha256(digest[7..])
            ?? throw new InvalidDataException("GitHub returned an invalid SHA-256 digest for the update file.");
        if (asset.Size is <= 0 or > UpdateInstaller.MaximumDownloadBytes)
            throw new InvalidDataException("GitHub returned an invalid update file size.");

        string? releaseNotesUrl = update.ReleaseNotesUrl;
        if (releaseNotesUrl is null)
            releaseNotesUrl = ValidateOptionalHttps(release.HtmlUrl, "html_url");

        return update with { Sha256 = sha256, DownloadSize = asset.Size, ReleaseNotesUrl = releaseNotesUrl };
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Klavrio-UpdateChecker/{version}");
        return client;
    }

    private static bool TryParseVersion(string? value, out Version? version)
    {
        string normalized = (value ?? string.Empty).Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(normalized, out version);
    }

    private static Version ComparableVersion(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    private static string? ValidateOptionalHttps(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"The update manifest field '{field}' must use HTTPS.");

        return uri.AbsoluteUri;
    }

    private static string? ValidateSha256(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return null;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The update manifest contains an invalid SHA-256 value.");
        return normalized;
    }

    private static GitHubReleaseAssetLocation ParseGitHubReleaseAssetUrl(string value)
    {
        var uri = new Uri(value);
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            segments.Length != 6 ||
            !segments[2].Equals("releases", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("download", StringComparison.OrdinalIgnoreCase) ||
            !segments[5].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update URL must point to a direct HTTPS GitHub release executable.");
        }

        return new(
            Uri.UnescapeDataString(segments[0]),
            Uri.UnescapeDataString(segments[1]),
            Uri.UnescapeDataString(segments[4]));
    }

    private sealed class UpdateManifest
    {
        public string? Platform { get; set; }
        public string? Version { get; set; }
        public string? Url { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotesUrl { get; set; }
        public string? Sha256 { get; set; }
        public long? Size { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        public List<GitHubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GitHubFileContent
    {
        public string? Encoding { get; set; }
        public string? Content { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        public string? Digest { get; set; }
        public long Size { get; set; }
    }

    private sealed record GitHubReleaseAssetLocation(string Owner, string Repository, string Tag);
}
