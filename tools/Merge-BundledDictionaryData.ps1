[CmdletBinding()]
param(
    [string]$DataPath = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ([string]::IsNullOrWhiteSpace($DataPath)) { $DataPath = Join-Path $PSScriptRoot '..\Data' }
$DataPath = [IO.Path]::GetFullPath($DataPath)
$utf8Strict = [Text.UTF8Encoding]::new($false, $true)
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$comparison = [StringComparer]::OrdinalIgnoreCase
$wordLimit = 128

function New-WordSet {
    return ,([Collections.Generic.HashSet[string]]::new($comparison))
}

function Add-WordsFromLines {
    param(
        [Collections.Generic.HashSet[string]]$Words,
        [Collections.Generic.IEnumerable[string]]$Lines,
        [string]$Source
    )

    $lineNumber = 0
    foreach ($line in $Lines) {
        $lineNumber++
        $word = $line.Trim().Normalize([Text.NormalizationForm]::FormC).ToLowerInvariant()
        if ($word.Length -eq 0 -or $word.StartsWith('#')) { continue }
        if ($word.Length -gt $wordLimit -or $word.Contains("`t") -or $word.IndexOf([char]0) -ge 0) {
            throw "Invalid dictionary entry in $Source at line $lineNumber"
        }
        [void]$Words.Add($word)
    }
}

function Add-WordsFromFile {
    param(
        [Collections.Generic.HashSet[string]]$Words,
        [string]$Name
    )

    $path = Join-Path $DataPath $Name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Add-WordsFromLines -Words $Words -Lines ([IO.File]::ReadLines($path, $utf8Strict)) -Source $Name
    }
}

function Add-WordsFromZipEntry {
    param(
        [Collections.Generic.HashSet[string]]$Words,
        [string]$ArchiveName,
        [string]$EntryName
    )

    $archivePath = Join-Path $DataPath $ArchiveName
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { return }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) { throw "$ArchiveName is missing $EntryName" }
        $reader = [IO.StreamReader]::new($entry.Open(), $utf8Strict, $true)
        try {
            $lines = [Collections.Generic.List[string]]::new()
            while (($line = $reader.ReadLine()) -ne $null) { $lines.Add($line) }
            Add-WordsFromLines -Words $Words -Lines $lines -Source "$ArchiveName/$EntryName"
        }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Read-ZipText {
    param([string]$ArchiveName, [string]$EntryName)
    $archivePath = Join-Path $DataPath $ArchiveName
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { return $null }
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) { return $null }
        $reader = [IO.StreamReader]::new($entry.Open(), $utf8Strict, $true)
        try { return $reader.ReadToEnd().Trim() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Write-ConsolidatedLanguage {
    param(
        [string]$Language,
        [string[]]$LooseFiles,
        [object[]]$ZipEntries,
        [string[]]$Header
    )

    $words = New-WordSet
    foreach ($file in $LooseFiles) { Add-WordsFromFile -Words $words -Name $file }
    foreach ($source in $ZipEntries) {
        Add-WordsFromZipEntry -Words $words -ArchiveName $source.Archive -EntryName $source.Entry
    }
    if ($words.Count -eq 0) { throw "No $Language dictionary entries were found." }

    $sorted = [Linq.Enumerable]::OrderBy([string[]]$words, [Func[string,string]]{ param($value) $value }, [StringComparer]::Ordinal)
    $outputPath = Join-Path $DataPath "$Language.txt"
    [IO.File]::WriteAllLines($outputPath, [string[]]($Header + $sorted), $utf8NoBom)
    Write-Host "$Language.txt: $($words.Count) unique entries"
}

if (-not (Test-Path -LiteralPath $DataPath -PathType Container)) { throw "Data folder not found: $DataPath" }

$wordPack = 'Klavrio_100K_Unique_Words.zip'
$namePack = 'Klavrio_Names_Multilingual_Deduplicated.zip'

Write-ConsolidatedLanguage -Language 'english' `
    -LooseFiles @('english.txt', 'english_extended.txt') `
    -ZipEntries @([pscustomobject]@{ Archive=$wordPack; Entry='english.txt' }) `
    -Header @(
        '# Klavrio consolidated English dictionary.',
        '# Sources include SCOWL and the CMU Pronouncing Dictionary; see THIRD_PARTY_LICENSE.txt.'
    )

Write-ConsolidatedLanguage -Language 'hebrew' `
    -LooseFiles @('hebrew.txt', 'hebrew_extended.txt', 'hebrew_common.txt') `
    -ZipEntries @(
        [pscustomobject]@{ Archive=$wordPack; Entry='hebrew.txt' },
        [pscustomobject]@{ Archive=$namePack; Entry='hebrew_first_names.txt' },
        [pscustomobject]@{ Archive=$namePack; Entry='hebrew_last_names.txt' }
    ) `
    -Header @(
        '# Klavrio consolidated Hebrew dictionary.',
        '# Includes reviewed common vocabulary and Hebrew first/last names; see THIRD_PARTY_LICENSE.txt.'
    )

$communitySources = @(
    [pscustomobject]@{ Language='arabic'; Loose=@('arabic.txt', 'arabic_extended.txt'); License='GPL-2.0-or-later OR LGPL-2.1-or-later OR MPL-1.1' },
    [pscustomobject]@{ Language='german'; Loose=@('german.txt', 'german_extended.txt'); License='GPL-2.0-or-later' },
    [pscustomobject]@{ Language='french'; Loose=@('french.txt', 'french_extended.txt'); License='MPL-2.0' },
    [pscustomobject]@{ Language='russian'; Loose=@('russian.txt', 'russian_extended.txt'); License='BSD-3-Clause-like' }
)
foreach ($source in $communitySources) {
    Write-ConsolidatedLanguage -Language $source.Language -LooseFiles $source.Loose -ZipEntries @() -Header @(
        "# Klavrio consolidated $($source.Language) dictionary.",
        "# LibreOffice/Hunspell-derived data: $($source.License); see COMMUNITY_DICTIONARY_LICENSES.txt."
    )
}

$noticePath = Join-Path $DataPath 'THIRD_PARTY_LICENSE.txt'
$notice = [IO.File]::ReadAllText($noticePath, $utf8Strict).TrimEnd()
$cmuLicense = Read-ZipText -ArchiveName $wordPack -EntryName 'CMU_DICTIONARY_LICENSE.txt'
if ($cmuLicense -and $notice.IndexOf('CMU PRONOUNCING DICTIONARY LICENSE', [StringComparison]::Ordinal) -lt 0) {
    $notice += "`n`n================================================================================`nCMU PRONOUNCING DICTIONARY LICENSE`n================================================================================`n$cmuLicense"
}
$fakerNotice = Read-ZipText -ArchiveName $namePack -EntryName 'SOURCE_LICENSE_NOTICE.txt'
if ($fakerNotice -and $notice.IndexOf('FAKER MULTILINGUAL NAME DATA NOTICE', [StringComparison]::Ordinal) -lt 0) {
    $notice += "`n`n================================================================================`nFAKER MULTILINGUAL NAME DATA NOTICE`n================================================================================`nThe bundled Hebrew name subset was derived from Faker Python 40.1.2 person-provider data.`n$fakerNotice"
}
[IO.File]::WriteAllText($noticePath, $notice + "`n", $utf8NoBom)

$redundantFiles = @(
    'english_extended.txt', 'hebrew_extended.txt', 'hebrew_common.txt',
    'arabic_extended.txt', 'german_extended.txt', 'french_extended.txt', 'russian_extended.txt',
    $wordPack, $namePack
)
foreach ($name in $redundantFiles) {
    $path = Join-Path $DataPath $name
    if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}

Write-Host 'Consolidation complete. Mapping TSV files and license notices remain separate by design.'
