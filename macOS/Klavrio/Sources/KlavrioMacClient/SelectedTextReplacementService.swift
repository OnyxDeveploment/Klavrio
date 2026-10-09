import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

final class SelectedTextReplacementService {
    private let converter: SelectedTextTransformer
    init(converter: SelectedTextTransformer) { self.converter = converter }

    func convertSelection(mode: SelectionDirection, completion: @escaping (Result<Bool, Error>) -> Void) {
        if convertUsingAccessibility(mode: mode, completion: completion) { return }
        convertUsingClipboard(mode: mode, completion: completion)
    }

    private func convertUsingAccessibility(mode: SelectionDirection, completion: @escaping (Result<Bool, Error>) -> Void) -> Bool {
        guard !SystemPermissionCenter.focusedElementIsSecure() else {
            completion(.failure(KlavrioError.permission("Secure input detected — conversion skipped.")))
            return true
        }
        let system = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let focused else { return false }
        let element = focused as! AXUIElement
        var selectedValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextAttribute as CFString, &selectedValue) == .success,
              let selected = selectedValue as? String, !selected.isEmpty else { return false }
        guard selected.count <= 12_000 else {
            completion(.failure(KlavrioError.operation("Selection must contain 1–12,000 characters.")))
            return true
        }
        let corrected = converter.convert(selected, mode: mode)
        guard corrected != selected else { completion(.success(false)); return true }
        let result = AXUIElementSetAttributeValue(element, kAXSelectedTextAttribute as CFString, corrected as CFString)
        if result == .success {
            LocalDiagnostics.success("Selected text converted with Accessibility | characters=\(selected.count)")
            completion(.success(true))
            return true
        }
        return false
    }

    private func convertUsingClipboard(mode: SelectionDirection, completion: @escaping (Result<Bool, Error>) -> Void) {
        let pasteboard = NSPasteboard.general
        let snapshot = ClipboardSnapshot(pasteboard: pasteboard)
        let previousChangeCount = pasteboard.changeCount
        postCommandKey(8) // C
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.16) { [converter] in
            guard pasteboard.changeCount != previousChangeCount,
                  let selected = pasteboard.string(forType: .string), !selected.isEmpty else {
                snapshot.restore(to: pasteboard)
                completion(.failure(KlavrioError.operation("No text selection was found.")))
                return
            }
            guard selected.count <= 12_000 else {
                snapshot.restore(to: pasteboard)
                completion(.failure(KlavrioError.operation("Selection must contain 1–12,000 characters.")))
                return
            }
            let corrected = converter.convert(selected, mode: mode)
            guard corrected != selected else { snapshot.restore(to: pasteboard); completion(.success(false)); return }
            pasteboard.clearContents()
            pasteboard.setString(corrected, forType: .string)
            self.postCommandKey(9) // V
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
                snapshot.restore(to: pasteboard)
                LocalDiagnostics.success("Selected text converted with clipboard fallback | characters=\(selected.count)")
                completion(.success(true))
            }
        }
    }

    private func postCommandKey(_ keyCode: CGKeyCode) {
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: keyCode, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: keyCode, keyDown: false) else { return }
        down.flags = .maskCommand
        up.flags = .maskCommand
        down.setIntegerValueField(.eventSourceUserData, value: klavrioEventSignature)
        up.setIntegerValueField(.eventSourceUserData, value: klavrioEventSignature)
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
    }
}

private struct ClipboardSnapshot {
    let values: [[NSPasteboard.PasteboardType: Data]]

    init(pasteboard: NSPasteboard) {
        values = (pasteboard.pasteboardItems ?? []).map { item in
            Dictionary(uniqueKeysWithValues: item.types.compactMap { type in item.data(forType: type).map { (type, $0) } })
        }
    }

    func restore(to pasteboard: NSPasteboard) {
        pasteboard.clearContents()
        let items = values.map { value -> NSPasteboardItem in
            let item = NSPasteboardItem()
            for (type, data) in value { item.setData(data, forType: type) }
            return item
        }
        if !items.isEmpty { pasteboard.writeObjects(items) }
    }
}
