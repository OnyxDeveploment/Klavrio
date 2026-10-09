using System.Net.Http;
using System.Reflection;
using System.Text.Json;

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
    string? ReleaseNotesUrl);

internal static class UpdateChecker
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static async Task<UpdateCheckResult> CheckAsync(string manifestUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The update manifest must use a complete HTTPS URL.", nameof(manifestUrl));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using HttpResponseMessage response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 65_536)
            throw new InvalidDataException("The update manifest is larger than 64 KB.");

        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        if (json.Length > 65_536)
            throw new InvalidDataException("The update manifest is larger than 64 KB.");

        UpdateManifest manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The update manifest is empty.");
        if (!TryParseVersion(manifest.Version, out Version? latest))
            throw new InvalidDataException("The update manifest contains an invalid version.");

        Version current = ComparableVersion(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0));
        latest = ComparableVersion(latest!);
        string? downloadUrl = ValidateOptionalHttps(manifest.DownloadUrl, "downloadUrl");
        string? releaseNotesUrl = ValidateOptionalHttps(manifest.ReleaseNotesUrl, "releaseNotesUrl");
        return new(latest! > current ? UpdateState.Available : UpdateState.UpToDate, current, latest!, downloadUrl, releaseNotesUrl);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Klavrio-UpdateChecker/1.0.0.0");
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

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotesUrl { get; set; }
        public string? Sha256 { get; set; }
    }
}
