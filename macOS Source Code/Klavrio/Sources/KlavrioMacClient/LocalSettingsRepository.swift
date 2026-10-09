import Foundation

enum LocalSettingsRepository {
    static func load() -> AppSettings {
        do {
            try KlavrioStoragePaths.ensureDirectories()
            guard FileManager.default.fileExists(atPath: KlavrioStoragePaths.settings.path) else {
                return AppSettings()
            }

            let decoder = JSONDecoder()
            decoder.dateDecodingStrategy = .iso8601
            return try decoder.decode(AppSettings.self, from: Data(contentsOf: KlavrioStoragePaths.settings))
        } catch {
            LocalDiagnostics.error("Load settings", error)
            return AppSettings()
        }
    }

    static func save(_ settings: AppSettings) {
        do {
            try KlavrioStoragePaths.ensureDirectories()
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            encoder.dateEncodingStrategy = .iso8601
            let data = try encoder.encode(settings)
            let temporary = KlavrioStoragePaths.settings.appendingPathExtension("tmp")
            try data.write(to: temporary, options: .atomic)
            if FileManager.default.fileExists(atPath: KlavrioStoragePaths.settings.path) {
                _ = try FileManager.default.replaceItemAt(KlavrioStoragePaths.settings, withItemAt: temporary)
            } else {
                try FileManager.default.moveItem(at: temporary, to: KlavrioStoragePaths.settings)
            }
        } catch {
            LocalDiagnostics.error("Save settings", error)
        }
    }
}
