import AppKit
import Combine
import Foundation

@MainActor
final class KlavrioCoordinator: ObservableObject {
    static let shared = KlavrioCoordinator()

    @Published var settings: AppSettings
    @Published var status: StatusMessage
    @Published var previewInput = ""
    @Published var previewOutput = ""
    @Published var permissionsReady = false
    @Published var updateSummary = ""
    @Published var dictionarySummary = ""
    @Published var updateURL: URL?
    @Published var busy = false

    let localizer: Localizer
    private(set) var engine: LayoutCorrectionPipeline?
    private var converter: SelectedTextTransformer?
    private var selectionService: SelectedTextReplacementService?
    private var monitor: KeyboardEventPipeline?
    private var permissionTimer: Timer?

    private init() {
        LocalDiagnostics.start()
        let loadedSettings = LocalSettingsRepository.load()
        let loadedLocalizer = Localizer(language: loadedSettings.uiLanguage)
        settings = loadedSettings
        localizer = loadedLocalizer
        status = .init(text: loadedLocalizer["ready"], tone: .success)
        previewOutput = loadedLocalizer["previewEmpty"]
        ensureCustomTemplates()
        loadEngine()
        configureMonitor()
        permissionsReady = SystemPermissionCenter.allGranted
        if #available(macOS 13.0, *) { settings.startAtLogin = LoginStartupService.isEnabled }
        refreshSummaries()
    }

    func start() {
        permissionsReady = SystemPermissionCenter.allGranted
        if permissionsReady {
            _ = monitor?.start()
        } else {
            setStatus(localizer["permissionsMissing"], .warning)
        }

        permissionTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.refreshPermissions() }
        }
        if settings.checkDictionaryUpdatesOnStartup, !settings.dictionaryManifestURL.isEmpty {
            Task { await checkDictionaryUpdates(manual: false) }
        }
        if settings.checkUpdatesOnStartup, !settings.updateManifestURL.isEmpty {
            Task { await checkForUpdates(manual: false) }
        }
    }

    func stop() {
        permissionTimer?.invalidate()
        monitor?.stop()
        LocalDiagnostics.info("Session ended")
    }

    func setAutomatic(_ enabled: Bool) {
        settings.automaticCorrectionEnabled = enabled
        monitor?.automaticEnabled = enabled
        save()
        setStatus(localizer[enabled ? "statusOn" : "statusOff"], enabled ? .success : .neutral)
    }

    func setLanguage(_ code: String) {
        settings.uiLanguage = code
        localizer.setLanguage(code)
        previewOutput = localizer["previewEmpty"]
        refreshSummaries()
        save()
        setStatus(localizer["ready"], .success)
    }

    func setTheme(_ theme: AppTheme) {
        settings.theme = theme
        save()
    }

    func setStartAtLogin(_ enabled: Bool) {
        do {
            if #available(macOS 13.0, *) { try LoginStartupService.setEnabled(enabled) }
            settings.startAtLogin = enabled
            save()
        } catch {
            settings.startAtLogin = !enabled
            LocalDiagnostics.error("Change login item", error)
            setStatus(error.localizedDescription, .error)
        }
    }

    func setSelectionMode(_ mode: SelectionDirection) {
        settings.selectionMode = mode
        save()
    }

    func setCheckUpdates(_ value: Bool) {
        settings.checkUpdatesOnStartup = value
        save()
    }

    func setCheckDictionaryUpdates(_ value: Bool) {
        settings.checkDictionaryUpdatesOnStartup = value
        save()
    }

    func setGitHubURL(_ value: String) {
        settings.githubURL = value.trimmingCharacters(in: .whitespacesAndNewlines)
        save()
    }

    func setUpdateManifestURL(_ value: String) {
        settings.updateManifestURL = value.trimmingCharacters(in: .whitespacesAndNewlines)
        save()
        refreshSummaries()
    }

    func setDictionaryManifestURL(_ value: String) {
        settings.dictionaryManifestURL = value.trimmingCharacters(in: .whitespacesAndNewlines)
        save()
        refreshSummaries()
    }

    func setSelectionHotkey(_ hotkey: HotkeyBinding) {
        guard hotkey != settings.toggleHotkey else {
            setStatus(localizer["shortcutConflictToggle"], .warning)
            return
        }
        settings.selectionHotkey = hotkey
        monitor?.selectionHotkey = hotkey
        save()
        setStatus(String(format: localizer["shortcutSaved"], hotkey.displayText), .success)
    }

    func setToggleHotkey(_ hotkey: HotkeyBinding) {
        guard hotkey != settings.selectionHotkey else {
            setStatus(localizer["shortcutConflictSelection"], .warning)
            return
        }
        settings.toggleHotkey = hotkey
        monitor?.toggleHotkey = hotkey
        save()
        setStatus(String(format: localizer["shortcutSaved"], hotkey.displayText), .success)
    }

    func preview() {
        guard let converter else { return }
        let converted = converter.convert(previewInput, mode: settings.selectionMode)
        previewOutput = converted
        setStatus(converted == previewInput ? localizer["noConversion"] : localizer["selectedFixed"], converted == previewInput ? .warning : .success)
    }

    func fixSelectedText() {
        selectionService?.convertSelection(mode: settings.selectionMode) { [weak self] result in
            Task { @MainActor in
                guard let self else { return }
                switch result {
                case .success(true): self.setStatus(self.localizer["selectedFixed"], .success)
                case .success(false): self.setStatus(self.localizer["noConversion"], .warning)
                case .failure(let error): self.setStatus(error.localizedDescription, .warning); LocalDiagnostics.error("Convert selected text", error)
                }
            }
        }
    }

    func requestPermissions() {
        SystemPermissionCenter.openSystemSettings()
        refreshPermissions()
    }

    func openCustomData() {
        ensureCustomTemplates()
        NSWorkspace.shared.open(KlavrioStoragePaths.customData)
        setStatus(localizer["customOpened"], .success)
    }

    func reloadDictionaries() {
        do {
            try engine?.reload()
            setStatus(localizer["dictionariesReloaded"], .success)
            LocalDiagnostics.success("Dictionaries reloaded manually")
        } catch {
            LocalDiagnostics.error("Reload dictionaries", error)
            setStatus(error.localizedDescription, .error)
        }
    }

    func openLogs() {
        LocalDiagnostics.openFolder()
        setStatus(localizer["logsOpened"], .success)
    }

    func copyLogPath() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(KlavrioStoragePaths.logFile.path, forType: .string)
        setStatus(localizer["logPathCopied"], .success)
    }

    func openGitHub() {
        guard let url = safeWebURL(settings.githubURL) else {
            setStatus(localizer["invalidProjectURL"], .warning)
            return
        }
        NSWorkspace.shared.open(url)
    }

    func openAvailableUpdate() {
        if let updateURL {
            NSWorkspace.shared.open(updateURL)
        }
    }

    func restoreDefaults() {
        let language = settings.uiLanguage
        settings = AppSettings()
        settings.uiLanguage = language
        localizer.setLanguage(language)
        monitor?.selectionHotkey = settings.selectionHotkey
        monitor?.toggleHotkey = settings.toggleHotkey
        setAutomatic(false)
        if #available(macOS 13.0, *) { try? LoginStartupService.setEnabled(false) }
        refreshSummaries()
        previewOutput = localizer["previewEmpty"]
        save()
        setStatus(localizer["ready"], .success)
        LocalDiagnostics.success("Settings restored to defaults")
    }

    func checkForUpdates(manual: Bool = true) async {
        guard !settings.updateManifestURL.isEmpty else {
            if manual {
                setStatus(localizer["noUpdateUrl"], .neutral)
            }
            return
        }

        busy = true
        defer { busy = false }
        do {
            let result = try await ReleaseUpdateService.check(manifestURL: settings.updateManifestURL)
            settings.lastUpdateCheckUTC = Date()
            updateURL = result.downloadURL ?? result.releaseNotesURL
            updateSummary = result.available
                ? String(format: localizer["updateAvailable"], result.latestVersion)
                : localizer["upToDate"]
            save()
            if manual || result.available { setStatus(updateSummary, .success) }
        } catch {
            LocalDiagnostics.error("Check for updates", error)
            if manual {
                setStatus(error.localizedDescription, .warning)
            }
        }
    }

    func checkDictionaryUpdates(manual: Bool = true) async {
        guard !settings.dictionaryManifestURL.isEmpty else {
            if manual {
                setStatus(localizer["noDictionaryUrl"], .neutral)
            }
            return
        }

        busy = true
        defer { busy = false }
        do {
            let result = try await CommunityLexiconUpdateService.checkAndInstall(
                manifestURL: settings.dictionaryManifestURL,
                installedVersion: settings.installedDictionaryVersion
            )
            settings.lastDictionaryUpdateCheckUTC = Date()
            settings.installedDictionaryVersion = result.version
            if result.installed { try engine?.reload() }
            dictionarySummary = result.installed
                ? String(format: localizer["communityInstalled"], result.version)
                : localizer["communityUpToDate"]
            save()
            if manual || result.installed { setStatus(dictionarySummary, .success) }
        } catch {
            LocalDiagnostics.error("Update community dictionaries", error)
            if manual {
                setStatus(error.localizedDescription, .warning)
            }
        }
    }

    private func loadEngine() {
        do {
            guard let bundled = KlavrioStoragePaths.bundledDataDirectory() else {
                throw KlavrioError.invalidData("The bundled Data folder was not found.")
            }

            let directories = [bundled, KlavrioStoragePaths.activeCommunityData, KlavrioStoragePaths.customData]
            let engine = try LayoutCorrectionPipeline(directories: directories)
            let converter = SelectedTextTransformer(engine: engine)
            self.engine = engine
            self.converter = converter
            selectionService = SelectedTextReplacementService(converter: converter)
        } catch {
            LocalDiagnostics.error("Load correction engine", error)
            setStatus(error.localizedDescription, .error)
        }
    }

    private func configureMonitor() {
        guard let engine else { return }
        let monitor = KeyboardEventPipeline(engine: engine)
        monitor.automaticEnabled = settings.automaticCorrectionEnabled
        monitor.selectionHotkey = settings.selectionHotkey
        monitor.toggleHotkey = settings.toggleHotkey
        monitor.onSelectionHotkey = { [weak self] in self?.fixSelectedText() }
        monitor.onToggleHotkey = { [weak self] in
            guard let self else { return }
            self.setAutomatic(!self.settings.automaticCorrectionEnabled)
        }
        monitor.onCorrection = { [weak self] success in
            Task { @MainActor in
                guard let self else { return }
                self.setStatus(self.localizer[success ? "lastCorrected" : "correctionFailed"], success ? .success : .warning)
            }
        }
        self.monitor = monitor
    }

    private func refreshPermissions() {
        let wasReady = permissionsReady
        permissionsReady = SystemPermissionCenter.allGranted
        if permissionsReady, !wasReady {
            _ = monitor?.start()
            setStatus(localizer["permissionsReady"], .success)
        }
    }

    private func refreshSummaries() {
        updateSummary = localizer[settings.updateManifestURL.isEmpty ? "updateUrlNeeded" : "updateConfigured"]
        dictionarySummary = localizer[settings.dictionaryManifestURL.isEmpty ? "dictUrlNeeded" : "dictConfigured"]
    }

    private func ensureCustomTemplates() {
        do {
            try KlavrioStoragePaths.ensureDirectories()
            let templates: [String: String] = [
                "README.txt":"Klavrio custom dictionaries (UTF-8)\nAdd one word per line to language files. Use TAB-separated columns in TSV files.\n",
                "english.txt":"# one English word per line\n", "hebrew.txt":"# מילה אחת בכל שורה\n", "arabic.txt":"# كلمة واحدة في كل سطر\n",
                "russian.txt":"# одно слово в строке\n", "german.txt":"# ein Wort pro Zeile\n", "french.txt":"# un mot par ligne\n",
                "hebrew_key_pairs.tsv":"typed_english_layout\thebrew_result\n# eurt\tקורא\n",
                "phonetic.tsv":"latin_spelling\thebrew_word\n# ma\tמה\n# kore\tקורה\n"
            ]
            for (name, content) in templates {
                let path = KlavrioStoragePaths.customData.appendingPathComponent(name)
                if !FileManager.default.fileExists(atPath: path.path) { try content.write(to: path, atomically: true, encoding: .utf8) }
            }
        } catch {
            LocalDiagnostics.error("Create custom data templates", error)
        }
    }

    private func save() { LocalSettingsRepository.save(settings) }
    private func setStatus(_ text: String, _ tone: StatusTone) { status = .init(text: text, tone: tone) }
    private func safeWebURL(_ value: String) -> URL? {
        guard let url = URL(string: value),
              ["http", "https"].contains(url.scheme?.lowercased() ?? ""),
              url.host != nil else {
            return nil
        }

        return url
    }
}
