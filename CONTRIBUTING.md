# Contributing words to Klavrio

Thank you for improving Klavrio. Dictionary pull requests are intentionally simple: edit the appropriate file in `Data`, add one valid word per line, and open a pull request.

Use one of these GitHub edit links to create a fork and pull request directly in the browser:

- [English](https://github.com/OnyxDeveploment/Klavrio/edit/main/Data/english.txt)
- [Hebrew](https://github.com/OnyxDeveploment/Klavrio/edit/main/Data/hebrew.txt)
- [Arabic](https://github.com/OnyxDeveploment/Klavrio/edit/main/Data/arabic.txt)
- [Russian](https://github.com/OnyxDeveploment/Klavrio/edit/main/Data/russian.txt)
- [German](https://github.com/OnyxDeveploment/Klavrio/edit/main/Data/german.txt)
- [French](https://github.com/OnyxDeveploment/Klavrio/edit/main/Data/french.txt)

GitHub will propose the change as a pull request. It does not reach Klavrio users until a maintainer reviews it, approves it, merges it into `main`, and the feed workflow succeeds.

## Word files

- Use UTF-8 and one word per line.
- Add comments only on lines beginning with `#`.
- Keep the natural spelling and accents: `français`, `über`, `привет`, `مرحبا`.
- Do not add definitions, translations, frequencies, numbers, or duplicate words.
- Add ordinary vocabulary to `french.txt`, `german.txt`, `arabic.txt`, `russian.txt`, `english.txt`, or `hebrew.txt`.
- Large reviewed imports must be merged into the matching consolidated language file with `tools/Merge-BundledDictionaryData.ps1` or the relevant reproducible importer, and must preserve their source and license notices.

For exact English-keyboard-to-Hebrew corrections, edit `Data/hebrew_key_pairs.tsv`. For phonetic Latin spellings such as `ma` → `מה`, edit `phonetic.tsv` when present. TSV fields must be separated by a real TAB, not spaces.

## Licensing and privacy

Only contribute words you wrote yourself, facts that are not copyrightable in your jurisdiction, or data whose license permits redistribution. State the source and license in the pull request. Never paste private messages, logs, passwords, customer data, or a dictionary dump with an unknown license.

By submitting original material for inclusion in Klavrio, you agree to license that contribution under the [Klavrio Noncommercial Share-Alike License 1.0](LICENSE). You retain copyright in your contribution. Third-party dictionary data is accepted only when its source and redistribution license are clearly documented; that data remains under its upstream license rather than being relicensed by Klavrio.

## Review and automatic delivery

Every pull request runs `.github/workflows/dictionary-feed.yml`. It rejects malformed UTF-8, duplicates, empty required languages, and invalid TSV rows. Configure the `main` branch to require this status check and at least one approving review.

After an approved pull request is merged into `main`, the workflow creates an immutable versioned ZIP, publishes a matching `.sha256` file, and updates the stable `dictionary-feed` manifest release. Klavrio compares the manifest's `dataVersion`, downloads the ZIP over HTTPS only when needed, verifies its SHA-256, checks its filenames and size, then installs it under `%LOCALAPPDATA%\Klavrio\CommunityData`. If validation or download fails, the previously installed local package stays active.

Klavrio uses this stable community-dictionary manifest:

```text
https://github.com/OnyxDeveploment/Klavrio/releases/download/dictionary-feed/dictionary-manifest.json
```

This URL is read-only in Klavrio. Do not replace it with a raw pull-request or branch URL. The stable release asset is updated only after validated changes are merged into `main`.

## Maintainer setup

1. In GitHub, create a branch-protection ruleset for `main`.
2. Require pull requests, one approval, and the `validate` status check.
3. Keep Actions allowed to create release assets (`contents: write` is scoped only to the publish job).
4. Merge reviewed dictionary pull requests. The feed publishes automatically after the validation job passes.
5. Run **Dictionary validation and feed** once with **Run workflow** after the workflow first reaches `main`; this creates the initial `dictionary-feed` release and makes the stable URL available.

Run the same validation locally with:

```powershell
./tools/Build-DictionaryPackage.ps1 -ValidateOnly
```

The large LibreOffice-derived lists can be regenerated from their pinned upstream commit with:

```powershell
./tools/Import-LibreOfficeDictionaries.ps1
```

## Application release feed

Application updates use the tracked [`klavrio-update.json`](klavrio-update.json) file at the repository root. Each release uses a four-part numeric version such as `1.0.0.1`. Installed clients use this stable repository URL, and Klavrio reads its file contents through GitHub's API:

```text
https://github.com/OnyxDeveploment/Klavrio/blob/main/klavrio-update.json
```

The manifest contains only `version` and `url`. The URL is authoritative: Klavrio does not construct the executable path or assume that its tag matches the version field. The URL must be a direct HTTPS GitHub release executable. When a newer four-part version is found, the Windows client derives the matching release API request from that URL, gets the asset size and SHA-256, asks for confirmation, verifies the download and embedded file version, retains a rollback copy, replaces the running version through the staged new executable, and restarts.

No GitHub Action is required. `Build_Release.bat 1.0.0.1` creates or refreshes these local files:

```text
klavrio-update.json
artifacts\application-update\klavrio-update.json
artifacts\application-update\Klavrio.exe.sha256
```

Edit the root JSON with the new version and exact executable URL, then build so the script validates that the JSON version matches the embedded executable version. Publish the executable at that URL, then commit and push `klavrio-update.json` to `main`. Do not create a separate `update-feed` release. Future versions only require a new normal release and a new two-field root JSON.

The generator reads the executable's embedded four-part version, verifies its Windows executable header, writes the two-field JSON, and also calculates a separate human-readable checksum. Do not rebuild or replace `Klavrio.exe` after generating the JSON. If the executable changes, regenerate the manifest and checksum together.

To regenerate it separately from the release build, run:

```powershell
./tools/Build-ApplicationUpdateManifest.ps1 -Version 1.0.0.1
```
