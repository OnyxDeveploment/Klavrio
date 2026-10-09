import AppKit
import SwiftUI

final class KlavrioLifecycleDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        NSSetUncaughtExceptionHandler { exception in
            LocalDiagnostics.warning("Unhandled Objective-C exception | name=\(exception.name.rawValue) | reason=\(exception.reason ?? "unknown")")
        }
        KlavrioCoordinator.shared.start()
    }

    func applicationWillTerminate(_ notification: Notification) { KlavrioCoordinator.shared.stop() }
}

@main
struct KlavrioMacApplication: App {
    @NSApplicationDelegateAdaptor(KlavrioLifecycleDelegate.self) private var delegate
    @StateObject private var controller = KlavrioCoordinator.shared

    var body: some Scene {
        WindowGroup("Klavrio", id: "main") {
            KlavrioRootView(controller: controller)
        }
        .defaultSize(width: 1020, height: 780)
        .windowResizability(.contentMinSize)

        MenuBarExtra {
            KlavrioMenuPanel(controller: controller)
                .environment(\.layoutDirection, controller.localizer.layoutDirection)
        } label: {
            Image(systemName: controller.settings.automaticCorrectionEnabled ? "character.cursor.ibeam" : "character.cursor.ibeam")
        }
    }
}
