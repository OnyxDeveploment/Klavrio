import Foundation

final class LexiconStore {
    private(set) var words: [SupportedLanguage: Set<String>] = [:]
    private var exactHebrewPairs: [String: String] = [:]
    private var phoneticHebrew: [String: String] = [:]
    private var hebrewPriority: [String: Int] = [:]

    init(directories: [URL]) throws {
        for language in SupportedLanguage.allCases { words[language] = [] }
        for (index, directory) in directories.enumerated() {
            try loadLooseDirectory(directory, overrideMappings: index > 0)
            for archive in (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []
                where archive.pathExtension.lowercased() == "zip" {
                try loadZipPackage(archive, overrideMappings: index > 0)
            }
        }
        guard !(words[.english]?.isEmpty ?? true), !(words[.hebrew]?.isEmpty ?? true) else {
            throw KlavrioError.invalidData("Klavrio requires non-empty English and Hebrew dictionaries.")
        }
        let counts = SupportedLanguage.allCases.map { "\($0.code)=\(words[$0]?.count ?? 0)" }.joined(separator: " | ")
        LocalDiagnostics.info("Dictionaries loaded | \(counts) | exactHe=\(exactHebrewPairs.count) | phoneticHe=\(phoneticHebrew.count) | priorities=\(hebrewPriority.count)")
    }

    func contains(_ language: SupportedLanguage, _ value: String) -> Bool {
        words[language]?.contains(normalize(value)) == true
    }

    func containsForAutomatic(_ language: SupportedLanguage, _ value: String) -> Bool {
        let normalized = normalize(value)
        guard words[language]?.contains(normalized) == true else { return false }
        return language != .hebrew || (hebrewPriority[normalized] ?? 100) >= 30
    }

    func exactHebrew(_ value: String, automatic: Bool = false) -> String? {
        guard let result = exactHebrewPairs[normalize(value)] else { return nil }
        return !automatic || (hebrewPriority[normalize(result)] ?? 100) >= 30 ? result : nil
    }

    func phoneticHebrew(_ value: String) -> String? { phoneticHebrew[normalize(value)] }

    private func loadLooseDirectory(_ directory: URL, overrideMappings: Bool) throws {
        guard FileManager.default.fileExists(atPath: directory.path) else { return }
        for file in try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) {
            let name = file.lastPathComponent.lowercased()
            if name == "hebrew_key_pairs.tsv" { try loadPairs(file, overrideMappings: overrideMappings) }
            else if name == "phonetic_manual.tsv" || name == "phonetic.tsv" { try loadPhonetic(file) }
            else if name == "hebrew_priority.tsv" { try loadPriorities(file) }
            else if let language = languageForFile(name) { try loadWords(file, language: language) }
        }
    }

    private func loadWords(_ url: URL, language: SupportedLanguage) throws {
        loadWords(try lines(url), language: language)
    }

    private func loadWords(_ source: [String], language: SupportedLanguage) {
        for line in source {
            let value = normalize(line)
            if !value.isEmpty, !value.hasPrefix("#") { words[language, default: []].insert(value) }
        }
    }

    private func loadPairs(_ url: URL, overrideMappings: Bool) throws {
        loadPairs(try lines(url), overrideMappings: overrideMappings)
    }

    private func loadPairs(_ source: [String], overrideMappings: Bool) {
        for line in source where !line.hasPrefix("#") && !line.lowercased().hasPrefix("typed_") {
            let fields = line.split(separator: "\t", omittingEmptySubsequences: false)
            guard fields.count >= 2 else { continue }
            let key = normalize(String(fields[0])), value = normalize(String(fields[1]))
            if !key.isEmpty, !value.isEmpty, overrideMappings || exactHebrewPairs[key] == nil { exactHebrewPairs[key] = value }
        }
    }

    private func loadPhonetic(_ url: URL) throws {
        loadPhonetic(try lines(url))
    }

    private func loadPhonetic(_ source: [String]) {
        for line in source where !line.hasPrefix("#") && !line.lowercased().hasPrefix("latin_") {
            let fields = line.split(separator: "\t", omittingEmptySubsequences: false)
            if fields.count >= 2 { phoneticHebrew[normalize(String(fields[0]))] = normalize(String(fields[1])) }
        }
    }

    private func loadPriorities(_ url: URL) throws {
        loadPriorities(try lines(url))
    }

    private func loadPriorities(_ source: [String]) {
        for line in source where !line.hasPrefix("#") && !line.lowercased().hasPrefix("hebrew_") {
            let fields = line.split(separator: "\t", omittingEmptySubsequences: false)
            if fields.count >= 2, let value = Int(fields[1]) { hebrewPriority[normalize(String(fields[0]))] = min(100, max(0, value)) }
        }
    }

    private func languageForFile(_ name: String) -> SupportedLanguage? {
        let base = URL(fileURLWithPath: name).deletingPathExtension().lastPathComponent.lowercased()
        if base.contains("phrases") || base.contains("priority") || base.contains("key_pairs") || base.contains("generated_unverified") { return nil }
        return SupportedLanguage.allCases.first {
            base == $0.code || base == $0.rawValue || base.hasPrefix("\($0.rawValue)_") || base == "words_\($0.code)"
        }
    }

    private func loadZipPackage(_ archive: URL, overrideMappings: Bool) throws {
        let entries = try runUnzip(["-Z1", archive.path]).text.components(separatedBy: .newlines).filter { !$0.isEmpty }
        var expandedBytes = 0
        for entry in entries {
            guard !entry.contains("/"), !entry.contains("\\") else { continue }
            let data = try runUnzip(["-p", archive.path, entry]).data
            guard !data.isEmpty, data.count <= 20 * 1024 * 1024 else { continue }
            expandedBytes += data.count
            guard expandedBytes <= 40 * 1024 * 1024 else { throw KlavrioError.invalidData("Expanded dictionary package is larger than 40 MB.") }
            guard let content = String(data: data, encoding: .utf8) else { throw KlavrioError.invalidData("Dictionary package contains invalid UTF-8: \(entry)") }
            let source = content.components(separatedBy: .newlines)
            let lower = entry.lowercased()
            if lower == "hebrew_key_pairs.tsv" { loadPairs(source, overrideMappings: overrideMappings) }
            else if lower == "phonetic_manual.tsv" { loadPhonetic(source) }
            else if lower == "hebrew_priority.tsv" { loadPriorities(source) }
            else if let language = languageForFile(lower) { loadWords(source, language: language) }
        }
        LocalDiagnostics.info("Dictionary package loaded | file=\(archive.lastPathComponent) | entries=\(entries.count)")
    }

    private func runUnzip(_ arguments: [String]) throws -> (data: Data, text: String) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        process.arguments = arguments
        let output = Pipe(), errors = Pipe()
        process.standardOutput = output
        process.standardError = errors
        try process.run()
        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            let message = String(data: errors.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? "unzip failed"
            throw KlavrioError.invalidData(message)
        }
        return (data, String(data: data, encoding: .utf8) ?? "")
    }

    private func lines(_ url: URL) throws -> [String] {
        try String(contentsOf: url, encoding: .utf8).components(separatedBy: .newlines)
    }

    private func normalize(_ value: String) -> String {
        value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    }
}
