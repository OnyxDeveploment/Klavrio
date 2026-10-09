Klavrio consolidated dictionary sources, UTF-8

English: 97523 unique entries
Hebrew: 3569 unique entries, including reviewed common vocabulary and Hebrew names
Arabic: 294179 unique entries
German: 163678 unique entries
French: 80457 unique entries
Russian: 146232 unique entries

HOW KLAVRIO USES THIS FOLDER:
The app loads one consolidated <language>.txt file for each supported language.
The former base, _extended, 100K, and multilingual-name sources were normalized,
deduplicated, and merged. Their required notices remain in the license files.

Load order:
1. Bundled consolidated dictionaries
2. Approved community package in %LOCALAPPDATA%\Klavrio\CommunityData
3. User overrides in %LOCALAPPDATA%\Klavrio\CustomData

Use Settings > Custom dictionaries > Open custom data for normal manual additions.
Klavrio creates documented UTF-8 templates for all six languages, exact Hebrew
keyboard pairs, and phonetic mappings. Save, then click Reload dictionaries in
Settings; no restart is required.

The bundled TSV/ZIP data contains exact English-keyboard -> Hebrew pairs. Exact
mappings take priority over broad dictionaries. Do NOT paste TSV lines into a word list.
hebrew_priority.tsv protects automatic mode from low-priority generated candidates;
manual Smart Auto conversion can still use those candidates when explicitly requested.
These files are not a universal or frequency-ranked language model.
The English list includes uncommon SCOWL and CMU entries, which may increase false negatives for automatic correction.
Large dictionaries are best for suggestion lookup, with confidence thresholds or manual conversion.

Sample: eurt = קורא

phonetic_manual.tsv provides transliteration such as ma -> מה and kore -> קורה.
Reviewed ambiguous key mappings can be kept available for selected-text repair while
disabled for background correction through hebrew_priority.tsv. Keep these TSV files
separate: they are structured mappings, not ordinary word lists.

The 100K word pack contributed 21279 CMU English entries. The multilingual name pack's
Hebrew first/last-name lists contributed 944 additional unique Hebrew entries after
deduplication. Broad Latin and unrelated-script name lists were intentionally not added,
because treating every international name as a normal English word would weaken wrong-
layout detection. CMU and Faker notices are preserved in THIRD_PARTY_LICENSE.txt.

Arabic, Russian, German, and French are consolidated from the core and pinned
LibreOffice/Hunspell-derived sources. tools/Import-LibreOfficeDictionaries.ps1 now
updates these consolidated files without recreating _extended files. Source revisions
and licenses are recorded in COMMUNITY_DICTIONARY_LICENSES.txt. Do not remove attribution.
tools/Merge-BundledDictionaryData.ps1 reproducibly imports the two reviewed source ZIPs
when they are supplied again, consolidates the files, preserves notices, and removes
the redundant archives only after successful processing.
Explicit selected-text modes work from keyboard layout maps even when a word is absent.

English source: SCOWL word lists (Hunspell en_US package), Copyright 2000-2011 Kevin Atkinson and contributors.
Permission to use/copy/modify/distribute/sell the word lists with attribution and permission notice in supporting docs.
See THIRD_PARTY_LICENSE.txt.

ACKNOWLEDGEMENTS AND LICENSES:
Special thanks to Kevin Atkinson and the SCOWL contributors, Alan Beale, and
every upstream word-list author and maintainer credited in
THIRD_PARTY_LICENSE.txt. Their work provides the foundation for Klavrio's
English dictionary data.

Thank you also to the LibreOffice and Hunspell dictionary maintainers and the
Arabic, German, French, and Russian contributors credited in
COMMUNITY_DICTIONARY_LICENSES.txt.

These dictionary files are third-party data. They keep their original
copyright notices and licenses and are not relicensed under Klavrio's project
license. Keep both license files with redistributed dictionary data.
