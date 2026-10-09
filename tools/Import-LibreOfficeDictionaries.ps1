[CmdletBinding()]
param(
    [string]$DataPath = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($DataPath)) { $DataPath = Join-Path $PSScriptRoot '..\Data' }
$commit = '32b006a2c22a4ac7e8ed3f03346f7b3d85a970a4'
$rawRoot = "https://raw.githubusercontent.com/LibreOffice/dictionaries/$commit"
$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$sources = @(
    [pscustomobject]@{ Code='ar'; Name='Arabic';  Input='ar/ar.dic';                         Output='arabic.txt';  License='GPL-2.0-or-later OR LGPL-2.1-or-later OR MPL-1.1'; Encoding='UTF-8' },
    [pscustomobject]@{ Code='de'; Name='German';  Input='de/de_DE_frami.dic';                Output='german.txt';  License='GPL-2.0-or-later'; Encoding='ISO-8859-1' },
    [pscustomobject]@{ Code='fr'; Name='French';  Input='fr_FR/dictionaries/fr.dic';         Output='french.txt';  License='MPL-2.0'; Encoding='UTF-8' },
    [pscustomobject]@{ Code='ru'; Name='Russian'; Input='ru_RU/ru_RU.dic';                   Output='russian.txt'; License='BSD-3-Clause-like'; Encoding='UTF-8' }
)

function Get-FirstUnescapedSlash([string]$Value) {
    for ($index = 0; $index -lt $Value.Length; $index++) {
        if ($Value[$index] -eq '/' -and ($index -eq 0 -or $Value[$index - 1] -ne '\')) { return $index }
    }
    return -1
}

function Read-CoreWords([string]$Path) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path -LiteralPath $Path)) { return $set }
    foreach ($line in [System.IO.File]::ReadLines($Path, $utf8Strict)) {
        $word = $line.Trim().ToLowerInvariant().Normalize([Text.NormalizationForm]::FormC)
        if ($word.Length -gt 0 -and -not $word.StartsWith('#')) { [void]$set.Add($word) }
    }
    return ,$set
}

New-Item -ItemType Directory -Force -Path $DataPath | Out-Null
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "Klavrio-import-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

try {
    foreach ($source in $sources) {
        $downloadPath = Join-Path $temporaryDirectory "$($source.Code).dic"
        Invoke-WebRequest -Headers @{'User-Agent'='Klavrio-Dictionary-Importer'} -Uri "$rawRoot/$($source.Input)" -OutFile $downloadPath

        $corePath = Join-Path $DataPath "$($source.Name.ToLowerInvariant()).txt"
        $words = Read-CoreWords $corePath
        $sourceEncoding = if ($source.Encoding -eq 'ISO-8859-1') { [Text.Encoding]::GetEncoding(28591) } else { $utf8Strict }
        foreach ($line in [System.IO.File]::ReadLines($downloadPath, $sourceEncoding)) {
            $candidate = ($line -split "`t", 2)[0].Trim()
            if ($candidate.Length -eq 0 -or $candidate.StartsWith('#') -or $candidate -match '^\d+(\s+\d+)?$') { continue }
            $slash = Get-FirstUnescapedSlash $candidate
            if ($slash -ge 0) { $candidate = $candidate.Substring(0, $slash) }
            $candidate = $candidate.Replace('\/', '/').ToLowerInvariant().Normalize([Text.NormalizationForm]::FormC)
            if ($candidate.Length -lt 1 -or $candidate.Length -gt 80) { continue }
            if ($candidate -notmatch "^\p{L}[\p{L}\p{M}'\u2019\-]*$") { continue }
            [void]$words.Add($candidate)
        }

        $header = @(
            "# Klavrio consolidated $($source.Name) dictionary",
            "# Source: https://github.com/LibreOffice/dictionaries/tree/$commit",
            "# Source file: $($source.Input)",
            "# License: $($source.License); see COMMUNITY_DICTIONARY_LICENSES.txt",
            '# Generated and merged by tools/Import-LibreOfficeDictionaries.ps1; do not hand-sort this file.'
        )
        $sorted = [System.Linq.Enumerable]::OrderBy([string[]]$words, [Func[string,string]]{ param($value) $value }, [StringComparer]::Ordinal)
        [System.IO.File]::WriteAllLines((Join-Path $DataPath $source.Output), [string[]]($header + $sorted), $utf8NoBom)
        Write-Host "$($source.Name): $($words.Count) consolidated words"
    }

    $licenseSources = @(
        [pscustomobject]@{ Heading='ARABIC - attribution and tri-license notice'; Path='ar/COPYING.txt'; Encoding='UTF-8' },
        [pscustomobject]@{ Heading='ARABIC - upstream README'; Path='ar/README_ar.txt'; Encoding='UTF-8' },
        [pscustomobject]@{ Heading='GERMAN - upstream README and licensing statement'; Path='de/README_de_DE_frami.txt'; Encoding='ISO-8859-1' },
        [pscustomobject]@{ Heading='GERMAN - GNU General Public License version 2'; Path='de/COPYING_GPLv2'; Encoding='UTF-8' },
        [pscustomobject]@{ Heading='FRENCH - upstream README and MPL 2.0 notice'; Path='fr_FR/dictionaries/README_dict_fr.txt'; Encoding='UTF-8' },
        [pscustomobject]@{ Heading='RUSSIAN - attribution and redistribution terms'; Path='ru_RU/README_ru_RU.txt'; Encoding='UTF-8' }
    )
    $licenseText = [System.Text.StringBuilder]::new()
    [void]$licenseText.AppendLine('Klavrio community dictionary sources')
    [void]$licenseText.AppendLine("LibreOffice dictionaries commit: $commit")
    [void]$licenseText.AppendLine('Source: https://github.com/LibreOffice/dictionaries')
    [void]$licenseText.AppendLine('The following word-list files are separate data assets under their stated upstream licenses.')
    foreach ($item in $licenseSources) {
        $licensePath = Join-Path $temporaryDirectory "license-$([Guid]::NewGuid().ToString('N')).txt"
        Invoke-WebRequest -Headers @{'User-Agent'='Klavrio-Dictionary-Importer'} -Uri "$rawRoot/$($item.Path)" -OutFile $licensePath
        $licenseEncoding = if ($item.Encoding -eq 'ISO-8859-1') { [Text.Encoding]::GetEncoding(28591) } else { $utf8Strict }
        $content = $licenseEncoding.GetString([IO.File]::ReadAllBytes($licensePath))
        [void]$licenseText.AppendLine("`n================================================================================")
        [void]$licenseText.AppendLine($item.Heading)
        [void]$licenseText.AppendLine("Upstream file: $($item.Path)")
        [void]$licenseText.AppendLine('================================================================================')
        [void]$licenseText.AppendLine($content.Trim())
    }
    [System.IO.File]::WriteAllText((Join-Path $DataPath 'COMMUNITY_DICTIONARY_LICENSES.txt'), $licenseText.ToString(), $utf8NoBom)
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        try { Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Temporary import folder could not be removed yet: $temporaryDirectory" }
    }
}
