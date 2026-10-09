import AppKit
import Foundation

enum SupportedLanguage: String, CaseIterable, Codable, Identifiable {
    case english, hebrew, arabic, russian, german, french

    var id: String { rawValue }
    var code: String {
        switch self {
        case .english: "en"
        case .hebrew: "he"
        case .arabic: "ar"
        case .russian: "ru"
        case .german: "de"
        case .french: "fr"
        }
    }
}

enum SelectionDirection: Int, CaseIterable, Codable, Identifiable {
    case auto, toHebrew, toEnglish, toArabic, toRussian, toGerman, toFrench
    var id: Int { rawValue }
}

enum AppTheme: String, CaseIterable, Codable, Identifiable {
    case dark = "Dark"
    case light = "Light"
    var id: String { rawValue }
}

struct HotkeyBinding: Codable, Equatable {
    struct Modifiers: OptionSet, Codable, Equatable {
        let rawValue: UInt64
        static let command = Modifiers(rawValue: 1 << 0)
        static let control = Modifiers(rawValue: 1 << 1)
        static let option = Modifiers(rawValue: 1 << 2)
        static let shift = Modifiers(rawValue: 1 << 3)
    }

    var modifiers: Modifiers
    var keyCode: UInt16
    var keyName: String

    static let selectionDefault = HotkeyBinding(modifiers: [.command, .shift], keyCode: 49, keyName: "Space")
    static let toggleDefault = HotkeyBinding(modifiers: [.command, .option], keyCode: 49, keyName: "Space")

    var displayText: String {
        var parts: [String] = []
        if modifiers.contains(.control) { parts.append("⌃") }
        if modifiers.contains(.option) { parts.append("⌥") }
        if modifiers.contains(.shift) { parts.append("⇧") }
        if modifiers.contains(.command) { parts.append("⌘") }
        parts.append(keyName)
        return parts.joined(separator: " ")
    }

    var isValid: Bool { !modifiers.isEmpty || Self.functionKeyCodes.contains(keyCode) }

    private static let functionKeyCodes: Set<UInt16> = [
        122, 120, 99, 118, 96, 97, 98, 100, 101, 109,
        103, 111, 105, 107, 113, 106, 64, 79, 80, 90
    ]

    func matches(keyCode: UInt16, flags: CGEventFlags) -> Bool {
        guard self.keyCode == keyCode else { return false }
        var current: Modifiers = []
        if flags.contains(.maskCommand) { current.insert(.command) }
        if flags.contains(.maskControl) { current.insert(.control) }
        if flags.contains(.maskAlternate) { current.insert(.option) }
        if flags.contains(.maskShift) { current.insert(.shift) }
        return current == modifiers
    }
}

enum KlavrioLinks {
    static let project = "https://github.com/OnyxDeveploment/Klavrio"
    static let contribute = project + "/blob/main/CONTRIBUTING.md"
    static let dictionaryFeed = "https://github.com/OnyxDeveploment/Klavrio/releases/download/dictionary-feed/dictionary-manifest.json"
    static let updateFeed = "https://github.com/OnyxDeveploment/Klavrio/klavrio-update.json"
}

struct AppSettings: Codable, Equatable {
    private static let currentSchemaVersion = 2
    var schemaVersion = 2
    var automaticCorrectionEnabled = false
    var startAtLogin = false
    var selectionMode = SelectionDirection.auto
    var uiLanguage = "en"
    var theme = AppTheme.dark
    var githubURL = KlavrioLinks.project
    var checkUpdatesOnStartup = true
    var updateManifestURL = KlavrioLinks.updateFeed
    var lastUpdateCheckUTC: Date?
    var checkDictionaryUpdatesOnStartup = true
    var dictionaryManifestURL = KlavrioLinks.dictionaryFeed
    var installedDictionaryVersion = ""
    var lastDictionaryUpdateCheckUTC: Date?
    var selectionHotkey = HotkeyBinding.selectionDefault
    var toggleHotkey = HotkeyBinding.toggleDefault

    init() { }

    private enum CodingKeys: String, CodingKey {
        case schemaVersion, automaticCorrectionEnabled, startAtLogin, selectionMode, uiLanguage, theme, githubURL
        case checkUpdatesOnStartup, updateManifestURL, lastUpdateCheckUTC
        case checkDictionaryUpdatesOnStartup, dictionaryManifestURL, installedDictionaryVersion, lastDictionaryUpdateCheckUTC
        case selectionHotkey, toggleHotkey
    }

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        let storedSchemaVersion = try values.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 0
        schemaVersion = Self.currentSchemaVersion
        automaticCorrectionEnabled = try values.decodeIfPresent(Bool.self, forKey: .automaticCorrectionEnabled) ?? false
        startAtLogin = try values.decodeIfPresent(Bool.self, forKey: .startAtLogin) ?? false
        selectionMode = try values.decodeIfPresent(SelectionDirection.self, forKey: .selectionMode) ?? .auto
        uiLanguage = try values.decodeIfPresent(String.self, forKey: .uiLanguage) ?? "en"
        theme = try values.decodeIfPresent(AppTheme.self, forKey: .theme) ?? .dark
        githubURL = KlavrioLinks.project
        checkUpdatesOnStartup = try values.decodeIfPresent(Bool.self, forKey: .checkUpdatesOnStartup) ?? true
        updateManifestURL = KlavrioLinks.updateFeed
        lastUpdateCheckUTC = try values.decodeIfPresent(Date.self, forKey: .lastUpdateCheckUTC)
        checkDictionaryUpdatesOnStartup = try values.decodeIfPresent(Bool.self, forKey: .checkDictionaryUpdatesOnStartup) ?? true
        dictionaryManifestURL = KlavrioLinks.dictionaryFeed
        installedDictionaryVersion = try values.decodeIfPresent(String.self, forKey: .installedDictionaryVersion) ?? ""
        lastDictionaryUpdateCheckUTC = try values.decodeIfPresent(Date.self, forKey: .lastDictionaryUpdateCheckUTC)
        selectionHotkey = try values.decodeIfPresent(HotkeyBinding.self, forKey: .selectionHotkey) ?? .selectionDefault
        toggleHotkey = try values.decodeIfPresent(HotkeyBinding.self, forKey: .toggleHotkey) ?? .toggleDefault

        _ = storedSchemaVersion
    }
}

struct LanguageDefinition {
    let id: SupportedLanguage
    let englishName: String
    let nativeName: String
    let keys: [Character]
}

enum StatusTone { case neutral, success, warning, error }

struct StatusMessage {
    var text: String
    var tone: StatusTone
}
