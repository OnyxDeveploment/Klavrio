# Windows → macOS parity map

The macOS edition is a native rewrite, not a wrapper around WPF and not a modification of the compiled example app.

| Windows implementation | macOS implementation | Behavior |
|---|---|---|
| `CorrectionEngine` / `LanguageCatalog` | `LayoutCorrectionPipeline.swift` / `KeyboardLayoutCatalog.swift` | Same six physical-key maps, script detection, dictionary confidence, exact pairs, priorities, and phonetic mappings |
| `KeyboardMonitor` low-level hook | `CGEventTap` in `KeyboardEventPipeline.swift` | Buffers only the current short word and corrects on Space; injected events are marked and ignored |
| `RegisterHotKey` | Event-tap shortcut matching | Global custom shortcuts for selected-text correction and automatic on/off |
| Clipboard/Win32 selected-text replacement | Accessibility selected-text replacement with clipboard fallback | Corrects text written before Klavrio starts |
| Password-field checks | AX secure-field check plus macOS Secure Input check | Skips protected input |
| WPF + Material Design | SwiftUI navigation, cards, motion, light/dark themes | Native responsive macOS interface with matching colors and information layout |
| `FlowDirection` RTL | SwiftUI `layoutDirection` | Full RTL layout for Hebrew and Arabic; URLs and shortcuts remain LTR |
| Windows startup registry | `SMAppService.mainApp` | Open Klavrio at login without a helper installer |
| Windows tray icon | `MenuBarExtra` | Klavrio keeps running after its window closes |
| `%LOCALAPPDATA%` | `~/Library/Application Support/Klavrio` | Settings, custom dictionaries, and downloaded community data |
| Windows diagnostic log | `~/Library/Logs/Klavrio/klavrio.log` | Readable event/error log that never contains typed or selected text |
| App/dictionary HTTPS manifests | `RemoteContentServices.swift` | Same opt-in manifest URLs, size limits, HTTPS requirement, SHA-256 verification, and validated activation |

macOS requires the user to grant Accessibility and Input Monitoring permission. This is an operating-system requirement and the only intentional onboarding difference from the Windows edition.
