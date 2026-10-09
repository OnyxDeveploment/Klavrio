# Klavrio for macOS

This is the native SwiftUI/macOS edition of the Windows Klavrio app. It uses the same keyboard maps, dictionary files, reviewed Hebrew mappings, phonetic mappings, confidence rules, UI languages, update-manifest model, custom-data workflow, and privacy model.

## Requirements

- macOS 13 Ventura or newer
- Xcode 15 or newer, including Command Line Tools
- Accessibility and Input Monitoring permission

Klavrio cannot be App-Sandboxed because global keyboard monitoring and selected-text replacement are core features. The app does not record typed or selected text in its log.

## Build and test

From this directory on a Mac:

```zsh
swift test
chmod +x build-release.sh
./build-release.sh
```

The release script creates:

- `build/Klavrio.app`
- `build/Klavrio-1.0.0-macOS-universal.dmg`

The default build uses ad-hoc signing for local testing. For a distributable build:

```zsh
export KLAVRIO_SIGNING_IDENTITY="Developer ID Application: YOUR NAME (TEAMID)"
./build-release.sh
```

Then notarize the DMG using your Apple Developer credentials before publishing it.

## First launch

1. Move `Klavrio.app` to `/Applications`.
2. Open it and choose **Open System Settings** in the Permissions card.
3. Enable Klavrio under **Privacy & Security → Accessibility** and **Input Monitoring**.
4. Relaunch Klavrio if macOS asks you to.

The menu-bar icon remains available when the main window is closed.

The macOS build has its own package/module identity (`KlavrioMacClient`) and uses the unique bundle identifier `dev.OnyxDeveploment.klavrio`. If an older test build used `app.klavrio`, macOS may ask for Accessibility and Input Monitoring permission again after this identity change.

## Data locations

- Settings: `~/Library/Application Support/Klavrio/settings.json`
- Custom dictionaries: `~/Library/Application Support/Klavrio/CustomData`
- Community dictionaries: `~/Library/Application Support/Klavrio/CommunityData/active`
- Readable log: `~/Library/Logs/Klavrio/klavrio.log`

Bundled dictionaries are copied from the repository's root `Data` folder during release builds. Approved community packages and custom dictionaries override bundled mappings in that order, matching the Windows edition.

## Update manifests

The community-dictionary and application-update feeds use fixed, read-only Klavrio GitHub release manifests. Each check can be disabled in Settings, but installed clients cannot be redirected to another feed there. Downloads are size-limited; dictionary packages require the SHA-256 declared in the manifest and are validated before activation. Application updates are metadata-only and open the release page instead of downloading or installing a binary.
