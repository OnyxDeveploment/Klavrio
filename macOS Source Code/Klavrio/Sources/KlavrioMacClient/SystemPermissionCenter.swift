import AppKit
import ApplicationServices
import CoreGraphics

enum SystemPermissionCenter {
    static var accessibilityGranted: Bool { AXIsProcessTrusted() }
    static var inputMonitoringGranted: Bool { CGPreflightListenEventAccess() }
    static var allGranted: Bool { accessibilityGranted && inputMonitoringGranted }

    static func request() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        _ = CGRequestListenEventAccess()
    }

    static func openSystemSettings() {
        request()
        if !accessibilityGranted {
            open("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
        } else if !inputMonitoringGranted {
            open("x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent")
        }
    }

    static func focusedElementIsSecure() -> Bool {
        let system = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let focused else { return false }
        let element = focused as! AXUIElement
        var subrole: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSubroleAttribute as CFString, &subrole) == .success else { return false }
        return (subrole as? String) == (kAXSecureTextFieldSubrole as String)
    }

    private static func open(_ value: String) {
        if let url = URL(string: value) { NSWorkspace.shared.open(url) }
    }
}
