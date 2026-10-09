import Foundation

enum KeyboardLayoutCatalog {
    static let physicalKeys = Array("qwertyuiopasdfghjklzxcvbnm,.;/[]'")

    static let definitions: [SupportedLanguage: LanguageDefinition] = [
        .english: .init(id: .english, englishName: "English", nativeName: "English", keys: physicalKeys),
        .hebrew: .init(id: .hebrew, englishName: "Hebrew", nativeName: "עברית", keys: Array("/'קראטוןםפשדגכעיחלךזסבהנמצתץף.][,")),
        .arabic: .init(id: .arabic, englishName: "Arabic", nativeName: "العربية", keys: Array("ضصثقفغعهخحشسيبلاتنمئءؤرلاىوزكظجدط")),
        .russian: .init(id: .russian, englishName: "Russian", nativeName: "Русский", keys: Array("йцукенгшщзфывапролдячсмитьбюж.хъэ")),
        .german: .init(id: .german, englishName: "German", nativeName: "Deutsch", keys: Array("qwertzuiopasdfghjklyxcvbnm,.ö-ü+ä")),
        .french: .init(id: .french, englishName: "French", nativeName: "Français", keys: Array("azertyuiopqsdfghjklwxcvbn,;:m!^$ù"))
    ]

    static func validateMaps() throws {
        for language in SupportedLanguage.allCases {
            guard definitions[language]?.keys.count == physicalKeys.count else {
                throw KlavrioError.invalidData("Keyboard map length mismatch for \(language.rawValue).")
            }
        }
    }

    static func detectScript(_ value: String) -> SupportedLanguage? {
        var found: SupportedLanguage?
        for scalar in value.unicodeScalars where CharacterSet.letters.contains(scalar) {
            let current: SupportedLanguage
            switch scalar.value {
            case 0x05D0...0x05EA: current = .hebrew
            case 0x0600...0x06FF, 0x0750...0x077F, 0x08A0...0x08FF: current = .arabic
            case 0x0400...0x052F: current = .russian
            default: current = .english
            }
            if let found, found != current { return nil }
            found = current
        }
        return found
    }

    static func convert(_ input: String, from source: SupportedLanguage, to target: SupportedLanguage) -> String {
        guard source != target,
              let from = definitions[source],
              let to = definitions[target] else { return input }

        var positions: [Character: Int] = [:]
        for (index, key) in from.keys.enumerated() where positions[key] == nil { positions[key] = index }

        return String(input.map { original in
            let lower = Character(String(original).lowercased())
            guard let position = positions[lower], position < to.keys.count else { return original }
            let converted = to.keys[position]
            if original.isUppercase, String(converted).rangeOfCharacter(from: .letters) != nil,
               target == .english || target == .german || target == .french {
                return Character(String(converted).uppercased())
            }
            return converted
        })
    }

    static func convertTo(_ input: String, target: SupportedLanguage) -> String {
        String(input.map { character in
            let source = detectScript(String(character)) ?? .english
            if source == target, target != .german, target != .french { return character }
            return Character(convert(String(character), from: source, to: target))
        })
    }
}

enum KlavrioError: LocalizedError {
    case invalidData(String)
    case permission(String)
    case operation(String)

    var errorDescription: String? {
        switch self {
        case .invalidData(let message), .permission(let message), .operation(let message): message
        }
    }
}

private extension Character {
    var isUppercase: Bool { String(self) == String(self).uppercased() && String(self) != String(self).lowercased() }
}
