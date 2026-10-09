import ServiceManagement

@available(macOS 13.0, *)
enum LoginStartupService {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func setEnabled(_ enabled: Bool) throws {
        if enabled, SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() }
        if !enabled, SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
        LocalDiagnostics.success("Open at login \(enabled ? "enabled" : "disabled")")
    }
}
