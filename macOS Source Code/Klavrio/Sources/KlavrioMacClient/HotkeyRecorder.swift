import AppKit
import SwiftUI

struct HotkeyRecorder: View {
    @Environment(\.layoutDirection) private var interfaceDirection
    let binding: HotkeyBinding
    let prompt: String
    let onChange: (HotkeyBinding) -> Void
    @State private var capturing = false
    @State private var eventMonitor: Any?

    var body: some View {
        Button {
            capturing ? stopCapture() : startCapture()
        } label: {
            Label(capturing ? prompt : binding.displayText, systemImage: "keyboard")
                .frame(minWidth: 150)
                .environment(\.layoutDirection, capturing ? interfaceDirection : .leftToRight)
        }
        .buttonStyle(SecondaryButtonStyle())
        .onDisappear { stopCapture() }
    }

    private func startCapture() {
        capturing = true
        eventMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 53 { stopCapture(); return nil }
            let modifiers = modifiers(from: event.modifierFlags)
            let candidate = HotkeyBinding(modifiers: modifiers, keyCode: event.keyCode, keyName: keyName(event))
            if candidate.isValid { onChange(candidate); stopCapture() }
            return nil
        }
    }

    private func stopCapture() {
        if let eventMonitor { NSEvent.removeMonitor(eventMonitor) }
        eventMonitor = nil
        capturing = false
    }

    private func modifiers(from flags: NSEvent.ModifierFlags) -> HotkeyBinding.Modifiers {
        var result: HotkeyBinding.Modifiers = []
        if flags.contains(.command) { result.insert(.command) }
        if flags.contains(.control) { result.insert(.control) }
        if flags.contains(.option) { result.insert(.option) }
        if flags.contains(.shift) { result.insert(.shift) }
        return result
    }

    private func keyName(_ event: NSEvent) -> String {
        switch event.keyCode {
        case 49: "Space"
        case 36: "Return"
        case 48: "Tab"
        case 122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111, 105, 107, 113, 106, 64, 79, 80, 90:
            "F\(functionNumber(event.keyCode))"
        default: (event.charactersIgnoringModifiers ?? "Key \(event.keyCode)").uppercased()
        }
    }

    private func functionNumber(_ keyCode: UInt16) -> Int {
        let map: [UInt16: Int] = [122:1,120:2,99:3,118:4,96:5,97:6,98:7,100:8,101:9,109:10,103:11,111:12,105:13,107:14,113:15,106:16,64:17,79:18,80:19,90:20]
        return map[keyCode] ?? Int(keyCode)
    }
}
