import Foundation

enum KlavrioStoragePaths {
    static let applicationSupport: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("Klavrio", isDirectory: true)
    }()

    static let settings = applicationSupport.appendingPathComponent("settings.json")
    static let customData = applicationSupport.appendingPathComponent("CustomData", isDirectory: true)
    static let communityData = applicationSupport.appendingPathComponent("CommunityData", isDirectory: true)
    static let activeCommunityData = communityData.appendingPathComponent("active", isDirectory: true)
    static let logs: URL = {
        let base = FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("Logs/Klavrio", isDirectory: true)
    }()
    static let logFile = logs.appendingPathComponent("klavrio.log")

    static func bundledDataDirectory() -> URL? {
        let manager = FileManager.default
        let candidates = [
            Bundle.main.resourceURL?.appendingPathComponent("Data", isDirectory: true),
            URL(fileURLWithPath: manager.currentDirectoryPath).appendingPathComponent("../../Data", isDirectory: true),
            URL(fileURLWithPath: manager.currentDirectoryPath).appendingPathComponent("Data", isDirectory: true)
        ].compactMap { $0 }
        return candidates.first { manager.fileExists(atPath: $0.path) }
    }

    static func ensureDirectories() throws {
        for url in [applicationSupport, customData, communityData, logs] {
            try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        }
    }
}
