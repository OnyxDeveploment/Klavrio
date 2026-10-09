# Klavrio

**Fix text typed with the wrong keyboard layout-without retyping it.**

**English** · [עברית](README.he.md)

---

Klavrio is a privacy-focused, offline keyboard-layout correction utility for Windows 10/11, with a native macOS 13+ edition. It can correct recognized words automatically when Space is pressed, or convert text you select in another app with a global shortcut-even if the text was written before Klavrio was opened.

> **Windows:** The main edition uses WPF and Material Design 3. **macOS:** The native SwiftUI edition lives in [`macOS/Klavrio`](macOS/Klavrio); see its [build guide](macOS/Klavrio/README.md). Building the macOS edition requires a Mac with Xcode.

## Highlights

- Responsive Material Design 3 interface with a modern navigation rail, adaptive compact layout, dark/light themes, subtle motion, elevated surfaces, Material icons, tooltips, and a Windows tray experience
- Localized interface and keyboard-layout maps for English, Hebrew, Arabic, Russian, German, and French
- Custom global shortcuts for selected-text conversion and for toggling automatic correction
- Smart mixed-text mode plus explicit English, Hebrew, Arabic, Russian, German, and French target modes
- Desktop and Start Menu shortcuts created for the current user on first launch; Windows can include the new Start Menu entry under **Recently added** when that system option is enabled
- Optional startup registration through both the current-user Startup folder and Registry Run list (no administrator access needed)
- Configurable GitHub/project button
- Optional startup update checks driven by configurable HTTPS JSON manifests
- User-editable dictionaries stored separately under `%LOCALAPPDATA%\Klavrio\CustomData`
- Settings saved under `%LOCALAPPDATA%\Klavrio\settings.json`
- Local processing only-no account, telemetry, or cloud text API; optional update checks download metadata and dictionary data, never typed text
- Structured lifecycle, success, warning, and error logs with separate fatal crash reports
- Typed and selected text are never written to diagnostics

## Build

Install the .NET 8 SDK on Windows, then run:

```powershell
dotnet build Klavrio.csproj
```

For a self-contained x64 release, run [`Build_Release.bat`](Build_Release.bat). The script bundles the .NET runtime and native WPF libraries into the executable and deliberately places the finished app at:

```text
bin\Release\net8.0-windows\Klavrio.exe
```

The release remains a small folder rather than a lone file. The `Data` and `ThirdParty` folders, `LICENSE`, and `THIRD_PARTY_NOTICES.txt` are placed beside the self-contained executable. Keep the complete folder together when distributing Klavrio.

An ordinary Visual Studio **Build** also uses `bin\Release\net8.0-windows`, but produces a smaller framework-dependent executable plus DLL files. A default `dotnet publish -r win-x64` creates the standard `win-x64\publish` subtree instead. The supplied release script overrides that publish location so the current self-contained executable is always at the direct path above. Exit Klavrio from the notification area before building so Windows is not locking the EXE.

Open [`Klavrio.slnx`](Klavrio.slnx) in Visual Studio, or build [`Klavrio.csproj`](Klavrio.csproj) directly from the command line.

On launch, Klavrio creates or repairs current-user shortcuts on the desktop and in the Start Menu. It never writes to the all-users desktop or Start Menu, so this does not require administrator access. The **Start with Windows** setting keeps a `shell:startup` shortcut and the current-user Registry Run entry synchronized. The single-instance guard quietly ignores the duplicate launch that these two startup mechanisms can generate.

## Use

### Fix selected text

1. Highlight text in Notepad or another desktop app that supports normal copy and paste.
2. Press the configured selection shortcut (default: `Ctrl + Shift + Space`).
3. Klavrio temporarily copies the selection, converts it, types the result, and restores the previous clipboard data on a best-effort basis.

Modes:

- **Smart auto · mixed text** preserves recognized correct words and only applies a unique dictionary-backed or reviewed exact match.
- **Convert to…** modes map physical key positions to English, Hebrew, Arabic, Russian, German, or French.

This workflow also fixes text written before Klavrio was opened: launch Klavrio, select the existing text, and press the selected-text shortcut. Klavrio does not need to record previous keystrokes.

Click either shortcut button in the app and press a new key combination to customize it. Function keys F1–F24 may be used alone; other keys require Ctrl, Alt, or Shift. Windows rejects shortcuts already owned by another app, and Klavrio keeps the previous working shortcut in that case.

Use **Restore defaults** under Settings to reset language, theme, startup behavior, automatic correction, selection mode, shortcuts, project URL, and close-to-tray behavior. Klavrio asks for confirmation first.

### Automatic correction

Turn on **Automatic correction** to check the bounded word immediately before Space. Klavrio only changes an exact dictionary-backed candidate. The default toggle shortcut is `Ctrl + Alt + Space`.

This feature is off on first launch. Your choice is remembered.

## Privacy and safety

Klavrio does not transmit or log typed words or selected text. If update checks are configured, it requests only the configured HTTPS manifests and dictionary ZIP. Diagnostics contain timestamps, anonymous session IDs, lifecycle events, operation outcome, app/runtime versions, and exception details.

Logs use short local timestamps, aligned severity labels, readable fields, and a clearly separated header for every application session. Exception stack traces are indented below their event instead of being compressed into one line. Logs are stored at:

```text
%LOCALAPPDATA%\Klavrio\Logs\klavrio.log
```

The main log rotates at 2 MB. When upgrading from the older dense format, Klavrio preserves it as `klavrio-legacy-*.log` and starts a clean readable log. Fatal errors also create a timestamped `crash-*.log`; crash reports older than 30 days are removed at startup.

Password detection is best-effort and only reliable for some native Windows controls. Browsers, terminals, elevated apps, games, remote desktops, and custom editors may block or behave differently with global hooks and injected input. Keep automatic correction off in sensitive fields, test with Notepad first, and use Ctrl+Z if an editor produces an unexpected result.

Klavrio ships one normalized, deduplicated word-list file per supported language. The reviewed 100K English/Hebrew pack and the useful Hebrew subset of the multilingual name pack have been consolidated into those files; unrelated broad name lists were deliberately excluded to avoid weakening detection. Exact Hebrew keyboard pairs and phonetic mappings remain separate and take priority, fixing `nv t,v eurt` → `מה אתה קורא` and `VHH ma kore` → `היי מה קורה`. Priority metadata prevents low-confidence generated Hebrew words from being applied by the background automatic mode.

For manual additions, open **Settings → Custom dictionaries → Open custom data**. These files live under `%LOCALAPPDATA%\Klavrio\CustomData`, are never replaced by publishing a new build, and override bundled mappings. Add one word per line or follow the TSV examples, save, then press **Reload dictionaries**-no restart is required.

## Community dictionary updates

French, German, Arabic, and Russian include consolidated LibreOffice/Hunspell-derived word lists. Their pinned source revision, attributions, and separate data licenses are recorded in `Data/COMMUNITY_DICTIONARY_LICENSES.txt`; the reproducible import and consolidation tools are `tools/Import-LibreOfficeDictionaries.ps1` and `tools/Merge-BundledDictionaryData.ps1`.

Contributors can add words through GitHub pull requests. The dictionary workflow validates UTF-8, duplicate entries, required language coverage, and TSV structure on every pull request. After you approve and merge a change into `main`, GitHub Actions automatically creates an immutable dictionary ZIP and refreshes a stable manifest. Configure this URL under **Settings → Community word updates**:

```text
https://github.com/OWNER/REPOSITORY/releases/download/dictionary-feed/dictionary-manifest.json
```

Klavrio verifies the manifest, HTTPS package URL, SHA-256 hash, expanded size, safe filenames, and UTF-8 content before atomically installing the package under `%LOCALAPPDATA%\Klavrio\CommunityData`. User files in `CustomData` still load last and override community mappings. A failed check or package never replaces the last working local data.

See [CONTRIBUTING.md](CONTRIBUTING.md) for contributor and maintainer setup, including the required protected-branch review/check configuration.

## Update manifest

The update checker is enabled by default but performs no network request until an HTTPS manifest URL is configured under Settings. It only checks metadata; Klavrio never downloads or installs an application update automatically. When an update is available, Klavrio can open the HTTPS download or release-notes URL in your browser. Expected JSON:

```json
{
  "version": "1.1.0",
  "downloadUrl": "https://example.com/Klavrio.exe",
  "releaseNotesUrl": "https://example.com/releases/1.1.0"
}
```

Responses are limited to 64 KB and requests time out after eight seconds. Download and release-note URLs must also use HTTPS.

## Project map

- [`App.xaml`](App.xaml) / [`App.xaml.cs`](App.xaml.cs) - WPF startup, Material Design theme resources, and crash handling
- [`MainWindow.xaml`](MainWindow.xaml) - Material Design 3 interface
- [`MainWindow.xaml.cs`](MainWindow.xaml.cs) - tray lifecycle, settings, shortcuts, and selection workflow
- [`AppSettings.cs`](AppSettings.cs) - local JSON settings persistence
- [`KeyboardMonitor.cs`](KeyboardMonitor.cs) - low-latency global keyboard hook
- [`CorrectionEngine.cs`](CorrectionEngine.cs) - automatic dictionary-backed correction
- [`SelectionConverter.cs`](SelectionConverter.cs) - selection conversion logic
- [`LanguageSupport.cs`](LanguageSupport.cs) - six keyboard layouts, dictionary merging, and exact-pair loading
- [`Localization.cs`](Localization.cs) - six-language interface strings
- [`AppearanceManager.cs`](AppearanceManager.cs) - live dark/light Material theme switching
- [`StartupManager.cs`](StartupManager.cs) - per-user Windows startup registration
- [`ShortcutManager.cs`](ShortcutManager.cs) - desktop, Start Menu, and Startup-folder shortcut management
- [`CustomDataManager.cs`](CustomDataManager.cs) - safe user-editable dictionary templates
- [`CommunityDataManager.cs`](CommunityDataManager.cs) - active/previous local community data packages
- [`DictionaryUpdateService.cs`](DictionaryUpdateService.cs) - HTTPS, SHA-256, ZIP, size, filename, and UTF-8 validation
- [`UpdateChecker.cs`](UpdateChecker.cs) - bounded HTTPS update-manifest checks
- [`SelectionHotkey.cs`](SelectionHotkey.cs) - global hotkey bindings
- [`CrashLogger.cs`](CrashLogger.cs) - structured rolling diagnostics and crash reports
- [`WinApi.cs`](WinApi.cs) - scoped Windows interop
- [`Assets`](Assets) - Klavrio logo PNG and multi-resolution Windows icon
- [`tools`](tools) / [`.github/workflows`](.github/workflows) - reproducible dictionary import, validation, packaging, and post-merge feed publishing

## Current limitations

- Klavrio repairs keyboard-layout mistakes; it is not a spelling or grammar checker.
- Smart mode is heuristic, not full natural-language understanding.
- Rich or delayed clipboard formats may not always restore perfectly.
- `SendInput` cannot write into a higher-integrity/elevated target from a normal process.
- Automatic correction evaluates individual words, not full sentence context. Explicit selected-text conversion is available when no dictionary match exists.

## Acknowledgements

Klavrio is possible thanks to the people and projects whose work it uses:

- Special thanks to **Kevin Atkinson and the SCOWL contributors**, **Alan Beale**, and every upstream word-list author and maintainer credited in [`Data/THIRD_PARTY_LICENSE.txt`](Data/THIRD_PARTY_LICENSE.txt). Their work provides the foundation for the English dictionary data.
- Thank you to the **LibreOffice and Hunspell dictionary maintainers** and the Arabic, German, French, and Russian dictionary contributors credited in [`Data/COMMUNITY_DICTIONARY_LICENSES.txt`](Data/COMMUNITY_DICTIONARY_LICENSES.txt).
- The Windows interface uses the **Material Design In XAML Toolkit** by James Willock, Mulholland Software, and its contributors, together with **Material Design Icons** from Pictogrammers.
- Windows builds use **.NET and WPF**, **Microsoft.Xaml.Behaviors.Wpf**, **Fody**, and **Costura.Fody**. Their copyright notices and full license texts are preserved in [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt) and the [`ThirdParty`](ThirdParty) folder.
- Development also relies on tools and services including the .NET SDK, C#, MSBuild, PowerShell, Swift, SwiftUI, Xcode, GitHub, and GitHub Actions. They remain the property of their respective rights holders; this acknowledgement does not imply endorsement.
- Thank you to everyone who reports problems, improves mappings, or contributes reviewed words to Klavrio.

Third-party software, tools, services, and data keep their original ownership and licenses; the Klavrio project license does not replace them. The macOS package currently declares no third-party Swift package dependencies, and the website uses no third-party runtime framework, external font, or analytics library.

## License

Klavrio's original code, documentation, and artwork are available under the [Klavrio Noncommercial Share-Alike License 1.0](LICENSE). You may use, study, modify, and redistribute them for noncommercial purposes, but distributed modified versions must remain under the same license and include their corresponding source. Commercial use requires a separate written license from the relevant copyright holder or holders.

This is a source-available license, not an OSI-approved open-source license. Bundled dictionaries, word lists, package dependencies, and other third-party materials keep their own licenses. See [`THIRD_PARTY_NOTICES.txt`](THIRD_PARTY_NOTICES.txt), [`Data/THIRD_PARTY_LICENSE.txt`](Data/THIRD_PARTY_LICENSE.txt), and [`Data/COMMUNITY_DICTIONARY_LICENSES.txt`](Data/COMMUNITY_DICTIONARY_LICENSES.txt) for the applicable notices.
