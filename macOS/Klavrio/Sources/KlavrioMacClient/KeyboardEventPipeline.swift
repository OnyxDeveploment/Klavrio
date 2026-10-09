import AppKit
import ApplicationServices
import Carbon.HIToolbox
import CoreGraphics
import Foundation

let klavrioEventSignature: Int64 = 0x4B_4C_41_56_52

private func klavrioEventCallback(
    proxy: CGEventTapProxy,
    type: CGEventType,
    event: CGEvent,
    userInfo: UnsafeMutableRawPointer?
) -> Unmanaged<CGEvent>? {
    guard let userInfo else { return Unmanaged.passUnretained(event) }
    let monitor = Unmanaged<KeyboardEventPipeline>.fromOpaque(userInfo).takeUnretainedValue()
    return monitor.handle(proxy: proxy, type: type, event: event) ? Unmanaged.passUnretained(event) : nil
}

final class KeyboardEventPipeline {
    private let engine: LayoutCorrectionPipeline
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var word = ""
    private var foregroundPID: pid_t = 0
    private var injecting = false
    private var generation = 0

    var automaticEnabled = false {
        didSet { word = ""; generation += 1; LocalDiagnostics.info(automaticEnabled ? "Monitoring enabled" : "Monitoring disabled") }
    }
    var selectionHotkey = HotkeyBinding.selectionDefault
    var toggleHotkey = HotkeyBinding.toggleDefault
    var onSelectionHotkey: (() -> Void)?
    var onToggleHotkey: (() -> Void)?
    var onCorrection: ((Bool) -> Void)?

    init(engine: LayoutCorrectionPipeline) { self.engine = engine }

    func start() -> Bool {
        guard eventTap == nil else { return true }
        let interested = (CGEventMask(1) << CGEventType.keyDown.rawValue) |
            (CGEventMask(1) << CGEventType.tapDisabledByTimeout.rawValue) |
            (CGEventMask(1) << CGEventType.tapDisabledByUserInput.rawValue)
        let pointer = Unmanaged.passUnretained(self).toOpaque()
        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: interested,
            callback: klavrioEventCallback,
            userInfo: pointer
        ) else {
            LocalDiagnostics.warning("CGEvent tap could not be created; permissions may be missing")
            return false
        }
        eventTap = tap
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        LocalDiagnostics.info("macOS keyboard event tap registered")
        return true
    }

    func stop() {
        if let eventTap { CGEvent.tapEnable(tap: eventTap, enable: false) }
        if let runLoopSource { CFRunLoopRemoveSource(CFRunLoopGetMain(), runLoopSource, .commonModes) }
        eventTap = nil
        runLoopSource = nil
        word = ""
    }

    fileprivate func handle(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Bool {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let eventTap { CGEvent.tapEnable(tap: eventTap, enable: true) }
            word = ""
            return true
        }
        guard type == .keyDown,
              event.getIntegerValueField(.eventSourceUserData) != klavrioEventSignature,
              !injecting else { return true }

        let keyCode = UInt16(event.getIntegerValueField(.keyboardEventKeycode))
        let flags = event.flags
        if selectionHotkey.matches(keyCode: keyCode, flags: flags) {
            DispatchQueue.main.async { [weak self] in self?.onSelectionHotkey?() }
            return false
        }
        if toggleHotkey.matches(keyCode: keyCode, flags: flags) {
            DispatchQueue.main.async { [weak self] in self?.onToggleHotkey?() }
            return false
        }
        guard automaticEnabled else { return true }

        if flags.contains(.maskCommand) || flags.contains(.maskControl) || flags.contains(.maskAlternate) {
            word = ""
            return true
        }
        let currentPID = NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0
        if currentPID != foregroundPID { foregroundPID = currentPID; word = ""; generation += 1 }

        if keyCode == 51 {
            if !word.isEmpty { word.removeLast() }
            return true
        }
        if keyCode == 49 {
            let original = word
            word = ""
            guard original.count >= engine.minimumLength,
                  let replacement = engine.suggest(original),
                  !SystemPermissionCenter.focusedElementIsSecure(),
                  !IsSecureEventInputEnabled() else { return true }
            let expectedGeneration = generation
            DispatchQueue.main.async { [weak self] in
                self?.replace(original: original, replacement: replacement, expectedGeneration: expectedGeneration)
            }
            return false
        }
        if [36, 48, 53, 123, 124, 125, 126, 117, 115, 119].contains(Int(keyCode)) {
            word = ""
            return true
        }

        guard let value = unicodeString(from: event), value.count == 1,
              let character = value.first, isWordCharacter(character), word.count < 32 else {
            word = ""
            return true
        }
        word.append(character)
        return true
    }

    private func replace(original: String, replacement: String, expectedGeneration: Int) {
        guard automaticEnabled, expectedGeneration == generation,
              !SystemPermissionCenter.focusedElementIsSecure(), !IsSecureEventInputEnabled() else { return }
        injecting = true
        defer { injecting = false }
        for _ in original { postKey(code: 51) }
        for character in replacement { postUnicode(String(character)) }
        postKey(code: 49)
        LocalDiagnostics.success("Automatic correction completed | source=keyboard-layout | characters=\(original.count)")
        onCorrection?(true)
    }

    private func postKey(code: CGKeyCode) {
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) else { return }
        for event in [down, up] {
            event.setIntegerValueField(.eventSourceUserData, value: klavrioEventSignature)
            event.post(tap: .cghidEventTap)
        }
    }

    private func postUnicode(_ value: String) {
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: 0, keyDown: false) else { return }
        let units = Array(value.utf16)
        units.withUnsafeBufferPointer { pointer in
            down.keyboardSetUnicodeString(stringLength: units.count, unicodeString: pointer.baseAddress!)
            up.keyboardSetUnicodeString(stringLength: units.count, unicodeString: pointer.baseAddress!)
        }
        for event in [down, up] {
            event.setIntegerValueField(.eventSourceUserData, value: klavrioEventSignature)
            event.post(tap: .cghidEventTap)
        }
    }

    private func unicodeString(from event: CGEvent) -> String? {
        var length = 0
        var units = [UniChar](repeating: 0, count: 8)
        event.keyboardGetUnicodeString(maxStringLength: units.count, actualStringLength: &length, unicodeString: &units)
        return length > 0 ? String(decoding: units.prefix(length), as: UTF16.self) : nil
    }

    private func isWordCharacter(_ value: Character) -> Bool {
        value.unicodeScalars.allSatisfy { CharacterSet.letters.contains($0) || ",.;/[]'".unicodeScalars.contains($0) }
    }
}
