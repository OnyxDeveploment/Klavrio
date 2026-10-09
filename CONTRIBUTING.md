# Contributing words to Klavrio

Thank you for improving Klavrio. Dictionary pull requests are intentionally simple: edit the appropriate file in `Data`, add one valid word per line, and open a pull request.

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

After an approved pull request is merged into `main`, the workflow creates an immutable versioned ZIP and updates the stable `dictionary-feed` manifest release. Klavrio downloads the ZIP over HTTPS, verifies its SHA-256, checks its filenames and size, then installs it under `%LOCALAPPDATA%\Klavrio\CommunityData`. If validation or download fails, the previously installed local package stays active.

Once the repository is published, configure Klavrio with:

```text
https://github.com/OWNER/REPOSITORY/releases/download/dictionary-feed/dictionary-manifest.json
```

Replace `OWNER/REPOSITORY` with the real GitHub repository. Do not use a raw pull-request or branch URL as the client feed.

## Maintainer setup

1. In GitHub, create a branch-protection ruleset for `main`.
2. Require pull requests, one approval, and the `validate` status check.
3. Keep Actions allowed to create release assets (`contents: write` is scoped only to the publish job).
4. Merge reviewed dictionary pull requests. The feed publishes automatically after the validation job passes.

Run the same validation locally with:

```powershell
./tools/Build-DictionaryPackage.ps1 -ValidateOnly
```

The large LibreOffice-derived lists can be regenerated from their pinned upstream commit with:

```powershell
./tools/Import-LibreOfficeDictionaries.ps1
```
