import Foundation

final class SelectedTextTransformer {
    private let engine: LayoutCorrectionPipeline
    private let phonetic: [String: String] = [
        "ma":"מה", "kore":"קורה", "kara":"קרה", "ata":"אתה", "at":"את", "ani":"אני",
        "anu":"אנו", "anachnu":"אנחנו", "aih":"איך", "eich":"איך", "ech":"איך", "lama":"למה",
        "shalom":"שלום", "toda":"תודה", "tov":"טוב", "ken":"כן", "lo":"לא", "ze":"זה", "zot":"זאת",
        "yom":"יום", "hayom":"היום", "achshav":"עכשיו", "sababa":"סבבה", "beseder":"בסדר",
        "rotze":"רוצה", "roze":"רוצה", "eifo":"איפה", "mi":"מי", "achla":"אחלה", "bye":"ביי"
    ]

    init(engine: LayoutCorrectionPipeline) { self.engine = engine }

    func convert(_ text: String, mode: SelectionDirection) -> String {
        guard !text.isEmpty else { return text }
        if let target = target(for: mode) { return KeyboardLayoutCatalog.convertTo(text, target: target) }

        var output = ""
        for token in tokenize(text) {
            guard token.rangeOfCharacter(from: .letters) != nil else { output += token; continue }
            let lower = token.lowercased()
            if let exact = engine.exactHebrew(lower) { output += exact; continue }
            if let suggestion = engine.suggest(token) { output += suggestion; continue }
            if let mapped = engine.phoneticHebrew(lower) ?? phonetic[lower] { output += mapped; continue }

            let punctuation = CharacterSet(charactersIn: ",.;/[]'")
            var stemText = token
            while let scalar = stemText.unicodeScalars.last, punctuation.contains(scalar) { stemText.removeLast() }
            let suffixCount = token.count - stemText.count
            if suffixCount > 0 {
                let suffix = String(token.suffix(suffixCount))
                if let exact = engine.exactHebrew(stemText) { output += exact + suffix; continue }
                if let suggestion = engine.suggest(stemText) { output += suggestion + suffix; continue }
            }
            output += token
        }
        return output
    }

    private func tokenize(_ text: String) -> [String] {
        guard let regex = try? NSRegularExpression(pattern: #"[\p{L}]+(?:[,.;/'\[\]][\p{L}]+)*[,.;/'\[\]]*|[^\p{L}]+"#) else { return [text] }
        let range = NSRange(text.startIndex..., in: text)
        return regex.matches(in: text, range: range).compactMap { Range($0.range, in: text).map { String(text[$0]) } }
    }

    private func target(for mode: SelectionDirection) -> SupportedLanguage? {
        switch mode {
        case .auto: nil
        case .toHebrew: .hebrew
        case .toEnglish: .english
        case .toArabic: .arabic
        case .toRussian: .russian
        case .toGerman: .german
        case .toFrench: .french
        }
    }
}
