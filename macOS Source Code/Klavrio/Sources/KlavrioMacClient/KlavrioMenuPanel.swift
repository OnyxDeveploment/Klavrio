import AppKit
import SwiftUI

struct KlavrioMenuPanel: View {
    @ObservedObject var controller: KlavrioCoordinator
    @Environment(\.openWindow) private var openWindow
    private var l: Localizer { controller.localizer }

    var body: some View {
        Toggle(isOn: Binding(get: { controller.settings.automaticCorrectionEnabled }, set: controller.setAutomatic)) {
            Label(l["automatic"], systemImage: "wand.and.stars")
        }
        Divider()
        Button { controller.fixSelectedText() } label: { Label(l["fixText"], systemImage: "character.cursor.ibeam") }
            .keyboardShortcut("f", modifiers: [.command, .shift])
        Button { openWindow(id: "main"); NSApp.activate(ignoringOtherApps: true) } label: { Label(l["settings"], systemImage: "gearshape") }
        Divider()
        Button(l["quit"]) { NSApp.terminate(nil) }
    }
}
