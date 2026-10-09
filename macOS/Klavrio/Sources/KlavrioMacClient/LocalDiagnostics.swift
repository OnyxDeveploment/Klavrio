import AppKit
import Foundation

enum LocalDiagnostics {
    private static let queue = DispatchQueue(label: "dev.OnyxDeveploment.klavrio.log")
    private static let iso: ISO8601DateFormatter = {
        let value = ISO8601DateFormatter()
        value.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return value
    }()

    static func start() {
        do {
            try KlavrioStoragePaths.ensureDirectories()
            if let size = try? KlavrioStoragePaths.logFile.resourceValues(forKeys: [.fileSizeKey]).fileSize, size > 2_000_000 {
                let previous = KlavrioStoragePaths.logs.appendingPathComponent("klavrio.previous.log")
                try? FileManager.default.removeItem(at: previous)
                try FileManager.default.moveItem(at: KlavrioStoragePaths.logFile, to: previous)
            }
            info("Session started | version=\(appVersion) | macOS=\(ProcessInfo.processInfo.operatingSystemVersionString)")
        } catch { NSLog("Klavrio logging unavailable: %@", error.localizedDescription) }
    }

    static func info(_ message: String) { write("INFO", message) }
    static func success(_ message: String) { write("SUCCESS", message) }
    static func warning(_ message: String) { write("WARNING", message) }
    static func error(_ context: String, _ error: Error) { write("ERROR", "\(context) | \(error.localizedDescription)") }

    static func openFolder() { NSWorkspace.shared.activateFileViewerSelecting([KlavrioStoragePaths.logFile]) }

    private static func write(_ level: String, _ message: String) {
        queue.async {
            do {
                try KlavrioStoragePaths.ensureDirectories()
                let clean = message.replacingOccurrences(of: "\r", with: " ").replacingOccurrences(of: "\n", with: " ↳ ")
                let line = "[\(iso.string(from: Date()))] \(level.padding(toLength: 7, withPad: " ", startingAt: 0)) \(clean)\n"
                let data = Data(line.utf8)
                if !FileManager.default.fileExists(atPath: KlavrioStoragePaths.logFile.path) { try data.write(to: KlavrioStoragePaths.logFile, options: .atomic) }
                else {
                    let handle = try FileHandle(forWritingTo: KlavrioStoragePaths.logFile)
                    try handle.seekToEnd()
                    try handle.write(contentsOf: data)
                    try handle.close()
                }
            } catch { NSLog("Klavrio log write failed: %@", error.localizedDescription) }
        }
    }

    static var appVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0.0"
    }
}
