import CryptoKit
import Foundation

struct AppUpdateResult {
    let available: Bool
    let currentVersion: String
    let latestVersion: String
    let downloadURL: URL?
    let releaseNotesURL: URL?
}

private struct AppUpdateManifest: Decodable {
    let version: String
    let downloadUrl: String?
    let releaseNotesUrl: String?
}

enum ReleaseUpdateService {
    static func check(manifestURL value: String) async throws -> AppUpdateResult {
        let url = try requireHTTPS(value, field: "update manifest")
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 8)
        request.setValue("Klavrio-UpdateChecker/1.0.0", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, 200..<300 ~= http.statusCode else {
            throw KlavrioError.operation("The update server returned an error.")
        }
        guard data.count <= 65_536 else { throw KlavrioError.invalidData("The update manifest is larger than 64 KB.") }
        let manifest = try JSONDecoder().decode(AppUpdateManifest.self, from: data)
        let current = LocalDiagnostics.appVersion
        let download = try optionalHTTPS(manifest.downloadUrl, field: "downloadUrl")
        let notes = try optionalHTTPS(manifest.releaseNotesUrl, field: "releaseNotesUrl")
        return .init(
            available: compareVersions(manifest.version, current) == .orderedDescending,
            currentVersion: current,
            latestVersion: manifest.version,
            downloadURL: download,
            releaseNotesURL: notes
        )
    }
}

struct DictionaryUpdateResult {
    let installed: Bool
    let version: String
    let files: Int
    let bytes: Int
}

private struct DictionaryManifest: Decodable {
    let schemaVersion: Int
    let dataVersion: String
    let packageUrl: String
    let sha256: String
    let minimumAppVersion: String?
}

enum CommunityLexiconUpdateService {
    private static let maximumPackageBytes = 40 * 1024 * 1024
    private static let maximumEntryBytes = 20 * 1024 * 1024
    private static let maximumEntries = 32

    static func checkAndInstall(manifestURL value: String, installedVersion: String) async throws -> DictionaryUpdateResult {
        let manifestURL = try requireHTTPS(value, field: "dictionary manifest")
        var request = URLRequest(url: manifestURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        request.setValue("Klavrio-DictionaryUpdater/1.0.0", forHTTPHeaderField: "User-Agent")
        let (manifestData, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, 200..<300 ~= http.statusCode else {
            throw KlavrioError.operation("The dictionary server returned an error.")
        }
        guard manifestData.count <= 65_536 else { throw KlavrioError.invalidData("The dictionary manifest is larger than 64 KB.") }
        let manifest = try JSONDecoder().decode(DictionaryManifest.self, from: manifestData)
        try validate(manifest)

        if let minimum = manifest.minimumAppVersion,
           compareVersions(minimum, LocalDiagnostics.appVersion) == .orderedDescending {
            throw KlavrioError.invalidData("Dictionary data \(manifest.dataVersion) requires a newer Klavrio version.")
        }
        if manifest.dataVersion.caseInsensitiveCompare(installedVersion) == .orderedSame,
           FileManager.default.fileExists(atPath: KlavrioStoragePaths.activeCommunityData.path) {
            return .init(installed: false, version: manifest.dataVersion, files: 0, bytes: 0)
        }

        let packageURL = try requireHTTPS(manifest.packageUrl, field: "dictionary package")
        var packageRequest = URLRequest(url: packageURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        packageRequest.setValue("Klavrio-DictionaryUpdater/1.0.0", forHTTPHeaderField: "User-Agent")
        let (package, packageResponse) = try await URLSession.shared.data(for: packageRequest)
        guard let packageHTTP = packageResponse as? HTTPURLResponse, 200..<300 ~= packageHTTP.statusCode else {
            throw KlavrioError.operation("The dictionary package could not be downloaded.")
        }
        guard package.count <= maximumPackageBytes else { throw KlavrioError.invalidData("The dictionary package is larger than 40 MB.") }
        let digest = SHA256.hash(data: package).map { String(format: "%02x", $0) }.joined()
        guard digest.caseInsensitiveCompare(manifest.sha256) == .orderedSame else {
            throw KlavrioError.invalidData("The dictionary package SHA-256 does not match its manifest.")
        }

        try KlavrioStoragePaths.ensureDirectories()
        let work = KlavrioStoragePaths.communityData.appendingPathComponent("install-\(UUID().uuidString)", isDirectory: true)
        let archive = work.appendingPathComponent("package.zip")
        let extracted = work.appendingPathComponent("extracted", isDirectory: true)
        try FileManager.default.createDirectory(at: extracted, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: work) }
        try package.write(to: archive, options: .atomic)

        let entries = try zipEntries(archive)
        try validateEntryNames(entries)
        try run("/usr/bin/ditto", ["-x", "-k", "--noqtn", archive.path, extracted.path])
        let files = try validateExtracted(extracted)
        try activate(extracted)
        LocalDiagnostics.success("Community dictionaries installed | version=\(manifest.dataVersion) | files=\(files) | bytes=\(package.count)")
        return .init(installed: true, version: manifest.dataVersion, files: files, bytes: package.count)
    }

    private static func validate(_ manifest: DictionaryManifest) throws {
        guard manifest.schemaVersion == 1 else { throw KlavrioError.invalidData("Unsupported dictionary manifest schema.") }
        guard !manifest.dataVersion.isEmpty, manifest.dataVersion.count <= 64 else { throw KlavrioError.invalidData("Invalid dictionary data version.") }
        _ = try requireHTTPS(manifest.packageUrl, field: "dictionary package")
        guard manifest.sha256.range(of: "^[0-9a-fA-F]{64}$", options: .regularExpression) != nil else {
            throw KlavrioError.invalidData("Invalid dictionary SHA-256 value.")
        }
    }

    private static func zipEntries(_ archive: URL) throws -> [String] {
        let output = try run("/usr/bin/unzip", ["-Z1", archive.path], captureOutput: true)
        return output.components(separatedBy: .newlines).filter { !$0.isEmpty }
    }

    private static func validateEntryNames(_ entries: [String]) throws {
        guard 1...maximumEntries ~= entries.count else { throw KlavrioError.invalidData("Dictionary package must contain 1–32 files.") }
        var seen = Set<String>()
        for name in entries {
            guard !name.hasPrefix("/"), !name.contains(".."), !name.contains("/"), !name.contains("\\"), seen.insert(name.lowercased()).inserted,
                  isAllowed(name) else { throw KlavrioError.invalidData("Dictionary package contains an invalid file: \(name)") }
        }
    }

    private static func validateExtracted(_ directory: URL) throws -> Int {
        let files = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.fileSizeKey, .isRegularFileKey, .isSymbolicLinkKey])
        var total = 0, dataFiles = 0
        for file in files {
            let values = try file.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey, .isSymbolicLinkKey])
            guard values.isRegularFile == true, values.isSymbolicLink != true else { throw KlavrioError.invalidData("Dictionary package contains a link or special file.") }
            let size = values.fileSize ?? 0
            guard size <= maximumEntryBytes else { throw KlavrioError.invalidData("Dictionary file is too large: \(file.lastPathComponent)") }
            total += size
            guard total <= maximumPackageBytes else { throw KlavrioError.invalidData("Expanded dictionary package is larger than 40 MB.") }
            if isData(file.lastPathComponent) {
                let content = try String(contentsOf: file, encoding: .utf8)
                guard !content.contains("\0"), content.split(separator: "\n", omittingEmptySubsequences: false).allSatisfy({ $0.count <= 512 }) else {
                    throw KlavrioError.invalidData("Invalid text in \(file.lastPathComponent).")
                }
                dataFiles += 1
            }
        }
        guard dataFiles > 0 else { throw KlavrioError.invalidData("Dictionary package contains no supported data files.") }
        return dataFiles
    }

    private static func activate(_ extracted: URL) throws {
        let manager = FileManager.default
        let backup = KlavrioStoragePaths.communityData.appendingPathComponent("previous", isDirectory: true)
        try? manager.removeItem(at: backup)
        if manager.fileExists(atPath: KlavrioStoragePaths.activeCommunityData.path) {
            try manager.moveItem(at: KlavrioStoragePaths.activeCommunityData, to: backup)
        }
        do {
            try manager.moveItem(at: extracted, to: KlavrioStoragePaths.activeCommunityData)
            try? manager.removeItem(at: backup)
        } catch {
            if manager.fileExists(atPath: backup.path) { try? manager.moveItem(at: backup, to: KlavrioStoragePaths.activeCommunityData) }
            throw error
        }
    }

    private static func isAllowed(_ name: String) -> Bool {
        isData(name) || ["readme.txt", "third_party_license.txt", "community_dictionary_licenses.txt", "dictionary-package.json"].contains(name.lowercased())
    }

    private static func isData(_ name: String) -> Bool {
        let lower = name.lowercased()
        if ["hebrew_key_pairs.tsv", "phonetic.tsv", "phonetic_manual.tsv", "hebrew_priority.tsv"].contains(lower) { return true }
        return lower.range(of: "^(english|hebrew|arabic|russian|german|french)(_[a-z0-9_-]+)?\\.txt$", options: .regularExpression) != nil
    }
}

private func requireHTTPS(_ value: String, field: String) throws -> URL {
    guard let url = URL(string: value), url.scheme?.lowercased() == "https", url.host != nil else {
        throw KlavrioError.invalidData("The \(field) URL must use HTTPS.")
    }
    return url
}

private func optionalHTTPS(_ value: String?, field: String) throws -> URL? {
    guard let value, !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
    return try requireHTTPS(value, field: field)
}

private func compareVersions(_ left: String, _ right: String) -> ComparisonResult {
    let lhs = left.trimmingCharacters(in: CharacterSet(charactersIn: "vV")).split(whereSeparator: { $0 == "-" || $0 == "+" }).first?.split(separator: ".").compactMap { Int($0) } ?? []
    let rhs = right.trimmingCharacters(in: CharacterSet(charactersIn: "vV")).split(whereSeparator: { $0 == "-" || $0 == "+" }).first?.split(separator: ".").compactMap { Int($0) } ?? []
    for index in 0..<max(lhs.count, rhs.count) {
        let a = index < lhs.count ? lhs[index] : 0, b = index < rhs.count ? rhs[index] : 0
        if a < b { return .orderedAscending }
        if a > b { return .orderedDescending }
    }
    return .orderedSame
}

@discardableResult
private func run(_ executable: String, _ arguments: [String], captureOutput: Bool = false) throws -> String {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: executable)
    process.arguments = arguments
    let output = Pipe(), errors = Pipe()
    if captureOutput { process.standardOutput = output }
    process.standardError = errors
    try process.run()
    process.waitUntilExit()
    guard process.terminationStatus == 0 else {
        let data = errors.fileHandleForReading.readDataToEndOfFile()
        throw KlavrioError.operation(String(data: data, encoding: .utf8) ?? "The archive tool failed.")
    }
    return captureOutput ? String(data: output.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? "" : ""
}
