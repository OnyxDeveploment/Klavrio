[CmdletBinding()]
param(
    [string]$Version,
    [string]$ExecutablePath,
    [string]$DownloadUrl,
    [string]$ManifestSourcePath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$maximumDownloadBytes = 300MB

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $PSScriptRoot '..\Klavrio C# Source Code\bin\Release\net8.0-windows\Klavrio.exe'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\artifacts\application-update'
}
if ([string]::IsNullOrWhiteSpace($ManifestSourcePath)) {
    $ManifestSourcePath = Join-Path $PSScriptRoot '..\klavrio-update.json'
}

$executable = Get-Item -LiteralPath $ExecutablePath -ErrorAction Stop
if ($executable.PSIsContainer -or $executable.Name -ne 'Klavrio.exe') {
    throw 'ExecutablePath must point to a file named Klavrio.exe.'
}

$fileVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable.FullName).FileVersion
$fileVersion = $null
if ([string]::IsNullOrWhiteSpace($fileVersionText) -or
    -not [Version]::TryParse(($fileVersionText -split '[+-]')[0], [ref]$fileVersion)) {
    throw 'Klavrio.exe does not contain a readable numeric file version.'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "$($fileVersion.Major).$($fileVersion.Minor).$([Math]::Max($fileVersion.Build, 0)).$([Math]::Max($fileVersion.Revision, 0))"
}
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Version must use four numeric parts without padding, for example 1.0.0.1.'
}

$releaseVersion = [Version]$Version
$normalizedFileVersion = [Version]::new(
    $fileVersion.Major,
    $fileVersion.Minor,
    [Math]::Max($fileVersion.Build, 0),
    [Math]::Max($fileVersion.Revision, 0))
$normalizedReleaseVersion = [Version]::new(
    $releaseVersion.Major,
    $releaseVersion.Minor,
    [Math]::Max($releaseVersion.Build, 0),
    [Math]::Max($releaseVersion.Revision, 0))
if ($normalizedFileVersion -ne $normalizedReleaseVersion) {
    throw "Klavrio.exe version $fileVersion does not match requested release version $releaseVersion."
}

if ([string]::IsNullOrWhiteSpace($DownloadUrl)) {
    if (-not (Test-Path -LiteralPath $ManifestSourcePath -PathType Leaf)) {
        throw 'DownloadUrl was not provided and klavrio-update.json could not be found.'
    }
    $sourceManifest = Get-Content -LiteralPath $ManifestSourcePath -Raw | ConvertFrom-Json
    if ([string]$sourceManifest.version -ne $Version) {
        throw "klavrio-update.json version '$($sourceManifest.version)' does not match executable version '$Version'."
    }
    $DownloadUrl = [string]$sourceManifest.url
}
$parsedUrl = $null
if (-not [Uri]::TryCreate($DownloadUrl, [UriKind]::Absolute, [ref]$parsedUrl) -or
    $parsedUrl.Scheme -ne [Uri]::UriSchemeHttps) {
    throw 'The update link must be a complete HTTPS URL.'
}

$downloadUri = [Uri]$DownloadUrl
if ($downloadUri.Host -ne 'github.com' -or
    $downloadUri.AbsolutePath -notmatch '^/[^/]+/[^/]+/releases/download/[^/]+/[^/]+\.exe$') {
    throw 'DownloadUrl must point to a direct GitHub release executable.'
}

if ($executable.Length -le 0 -or $executable.Length -gt $maximumDownloadBytes) {
    throw 'Klavrio.exe must be between 1 byte and 300 MB.'
}

$stream = [IO.File]::OpenRead($executable.FullName)
try {
    if ($stream.ReadByte() -ne [byte][char]'M' -or $stream.ReadByte() -ne [byte][char]'Z') {
        throw 'Klavrio.exe does not have a valid Windows executable header.'
    }
}
finally {
    $stream.Dispose()
}

$sha256 = (Get-FileHash -LiteralPath $executable.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    version = $Version
    url = $DownloadUrl
}

$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null
$manifestPath = Join-Path $resolvedOutputDirectory 'klavrio-update.json'
$checksumPath = Join-Path $resolvedOutputDirectory 'Klavrio.exe.sha256'
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $checksumPath,
    "$sha256  Klavrio.exe$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

Write-Host "Created application update manifest for Klavrio $Version"
Write-Host "  Executable: $($executable.FullName)"
Write-Host "  Bytes:      $($executable.Length)"
Write-Host "  SHA-256:    $sha256"
Write-Host "  Manifest:   $manifestPath"
Write-Host "  Checksum:   $checksumPath"

