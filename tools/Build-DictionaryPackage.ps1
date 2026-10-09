[CmdletBinding()]
param(
    [string]$DataPath = '',
    [string]$OutputDirectory = '',
    [string]$DataVersion = (Get-Date).ToUniversalTime().ToString('yyyy.MM.dd.HHmm'),
    [string]$PackageUrl = 'https://example.invalid/klavrio-dictionaries.zip',
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ([string]::IsNullOrWhiteSpace($DataPath)) { $DataPath = Join-Path $PSScriptRoot '..\Data' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot '..\artifacts\dictionary-feed' }
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$languagePattern = '^(english|hebrew|arabic|russian|german|french)(_[a-z0-9_-]+)?\.txt$'
$mappingNames = @('hebrew_key_pairs.tsv', 'phonetic.tsv', 'phonetic_manual.tsv', 'hebrew_priority.tsv')
$requiredLanguages = @('english', 'hebrew', 'arabic', 'russian', 'german', 'french')
$counts = [ordered]@{}
$selectedFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

if (-not (Test-Path -LiteralPath $DataPath -PathType Container)) { throw "Data folder not found: $DataPath" }

foreach ($file in Get-ChildItem -LiteralPath $DataPath -File | Sort-Object Name) {
    $isLanguage = $file.Name -match $languagePattern -and $file.Name -notmatch 'generated_unverified'
    $isMapping = $mappingNames -contains $file.Name
    $isDocumentation = $file.Name -in @('README.txt', 'THIRD_PARTY_LICENSE.txt', 'COMMUNITY_DICTIONARY_LICENSES.txt')
    if (-not ($isLanguage -or $isMapping -or $isDocumentation)) { continue }

    try { $lines = [System.IO.File]::ReadAllLines($file.FullName, $utf8Strict) }
    catch { throw "$($file.Name) is not valid UTF-8: $($_.Exception.Message)" }

    if ($isLanguage) {
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $lineNumber = 0
        foreach ($line in $lines) {
            $lineNumber++
            $word = $line.Trim().Normalize([Text.NormalizationForm]::FormC)
            if ($word.Length -eq 0 -or $word.StartsWith('#')) { continue }
            if ($word.Length -gt 128 -or $word.Contains("`t") -or $word.IndexOf([char]0) -ge 0) {
                throw "Invalid dictionary entry at $($file.Name):$lineNumber"
            }
            if (-not $seen.Add($word)) { throw "Duplicate dictionary entry '$word' in $($file.Name):$lineNumber" }
        }
        $counts[$file.Name] = $seen.Count
    }
    elseif ($isMapping) {
        $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $lineNumber = 0
        foreach ($line in $lines) {
            $lineNumber++
            if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#') -or $lineNumber -eq 1) { continue }
            $fields = $line.Split("`t")
            if ($fields.Count -lt 2 -or [string]::IsNullOrWhiteSpace($fields[0]) -or [string]::IsNullOrWhiteSpace($fields[1])) {
                throw "Invalid TSV row at $($file.Name):$lineNumber"
            }
            if (-not $keys.Add($fields[0].Trim())) { throw "Duplicate TSV key '$($fields[0])' in $($file.Name):$lineNumber" }
        }
        $counts[$file.Name] = $keys.Count
    }
    $selectedFiles.Add($file)
}

foreach ($language in $requiredLanguages) {
    $total = 0
    foreach ($item in $counts.GetEnumerator()) { if ($item.Key -match "^$language(_|\.)") { $total += [int]$item.Value } }
    if ($total -eq 0) { throw "No usable $language words were found." }
}

Write-Host "Dictionary validation passed: $($selectedFiles.Count) files"
foreach ($item in $counts.GetEnumerator()) { Write-Host "  $($item.Key): $($item.Value) entries" }
if ($ValidateOnly) { return }

$parsedPackageUrl = $null
if (-not [Uri]::TryCreate($PackageUrl, [UriKind]::Absolute, [ref]$parsedPackageUrl) -or $parsedPackageUrl.Scheme -ne 'https') {
    throw 'PackageUrl must be an absolute HTTPS URL.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$safeVersion = $DataVersion -replace '[^A-Za-z0-9._-]', '-'
$packageName = "klavrio-dictionaries-$safeVersion.zip"
$packagePath = Join-Path $OutputDirectory $packageName
$staging = Join-Path ([IO.Path]::GetTempPath()) "Klavrio-package-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    foreach ($file in $selectedFiles) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name) }
    $packageMetadata = [ordered]@{
        schemaVersion = 1
        dataVersion = $DataVersion
        generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        counts = $counts
    } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $staging 'dictionary-package.json'), $packageMetadata, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $packagePath, [IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}

$sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 1
    dataVersion = $DataVersion
    minimumAppVersion = '1.0.0.0'
    packageUrl = $PackageUrl
    sha256 = $sha256
    files = $selectedFiles.Count
    counts = $counts
} | ConvertTo-Json -Depth 5
$manifestPath = Join-Path $OutputDirectory 'dictionary-manifest.json'
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
Write-Host "Package: $packagePath"
Write-Host "Manifest: $manifestPath"
Write-Host "SHA-256: $sha256"
