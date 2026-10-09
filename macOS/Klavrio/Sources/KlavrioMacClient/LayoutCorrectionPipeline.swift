import Foundation

final class LayoutCorrectionPipeline {
    private var repository: LexiconStore
    private let directories: [URL]
    private let queue = DispatchQueue(label: "dev.OnyxDeveploment.klavrio.dictionary", attributes: .concurrent)
    var minimumLength = 2

    init(directories: [URL]) throws {
        self.directories = directories
        repository = try LexiconStore(directories: directories)
    }

    func reload() throws {
        let replacement = try LexiconStore(directories: directories)
        queue.sync(flags: .barrier) { repository = replacement }
    }

    func translate(_ input: String) -> String? {
        guard !input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        switch KeyboardLayoutCatalog.detectScript(input) {
        case .hebrew?: return KeyboardLayoutCatalog.convert(input, from: .hebrew, to: .english)
        case .english?: return KeyboardLayoutCatalog.convert(input, from: .english, to: .hebrew)
        default: return nil
        }
    }

    func suggest(_ input: String) -> String? {
        queue.sync {
            let normalized = input.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            guard normalized.count >= minimumLength, normalized.count <= 48 else { return nil }
            if let exact = repository.exactHebrew(normalized, automatic: true), exact != input { return exact }
            guard let source = KeyboardLayoutCatalog.detectScript(input) else { return nil }
            let alreadyKnown = repository.contains(source, normalized)
            let candidates = Set(SupportedLanguage.allCases.compactMap { target -> String? in
                guard target != source else { return nil }
                let candidate = KeyboardLayoutCatalog.convert(input, from: source, to: target)
                return candidate != input && repository.containsForAutomatic(target, candidate) ? candidate : nil
            })
            return !alreadyKnown && candidates.count == 1 ? candidates.first : nil
        }
    }

    func isKnown(_ language: SupportedLanguage, _ value: String) -> Bool { queue.sync { repository.contains(language, value) } }
    func exactHebrew(_ value: String) -> String? { queue.sync { repository.exactHebrew(value) } }
    func phoneticHebrew(_ value: String) -> String? { queue.sync { repository.phoneticHebrew(value) } }
}
