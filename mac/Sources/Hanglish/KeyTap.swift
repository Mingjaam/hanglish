import AppKit
import Carbon.HIToolbox
import HanglishCore

/// Which keys can flip a word or toggle the preview, and their names for the settings window.
enum FlipKeys {
    static let capsLock: Int64 = 57
    /// Right ⌘: habit never taps it alone, unlike Caps Lock, which many use to switch input sources.
    static let defaultKey: Int64 = 54
    /// Presets shown in settings; any other allowed key can be recorded.
    static let presets: [Int64] = [54, 57, 61, 62]
    /// The preview toggle key: Caps Lock, the Mac's 한/영 key, which people already press to change what they see.
    static let previewDefault: Int64 = 57
    /// No key (the preview toggle can be turned off). Key code 0 is A, so this is negative.
    static let none: Int64 = -1
    static let previewPresets: [Int64] = [57, 61, 62, FlipKeys.none]

    /// Whether two settings would claim the same physical key.
    static func same(_ a: Int64, _ b: Int64) -> Bool { a != none && a == b }

    /// Modifier keys and the flag that shows them held down. Caps Lock toggles instead of holding.
    static let modifierFlags: [Int64: CGEventFlags] = [
        54: .maskCommand, 55: .maskCommand, 56: .maskShift, 60: .maskShift,
        58: .maskAlternate, 61: .maskAlternate, 59: .maskControl, 62: .maskControl, 63: .maskSecondaryFn,
    ]

    static func name(_ code: Int64) -> String {
        let names: [Int64: String] = [
            FlipKeys.none: "사용 안 함", 57: "Caps Lock (한/영)", 54: "오른쪽 ⌘", 55: "왼쪽 ⌘", 61: "오른쪽 ⌥", 58: "왼쪽 ⌥", 62: "오른쪽 Control",
            59: "왼쪽 Control", 60: "오른쪽 Shift", 56: "왼쪽 Shift", 63: "fn",
            122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8", 101: "F9",
            109: "F10", 103: "F11", 111: "F12", 105: "F13", 107: "F14", 113: "F15", 106: "F16", 64: "F17",
            79: "F18", 80: "F19", 90: "F20", 102: "영문 키", 104: "한글 키",
        ]
        return names[code] ?? "키 \(code)"
    }
}

/// Feeds global keystrokes to the Engine through a CGEvent tap and types its output with
/// posted events. Keys the engine owns are swallowed and re-posted, so their order relative
/// to our own text is preserved. Mirrors src/Hanglish/KeyboardHook.cs.
final class KeyTap {
    /// Marks events we post so the tap lets them through untouched.
    static let tag: Int64 = 0x484E5957

    // Physical ANSI positions, so the letters do not depend on the active layout.
    static let letters: [Int64: Character] = [
        0: "a", 1: "s", 2: "d", 3: "f", 4: "h", 5: "g", 6: "z", 7: "x", 8: "c", 9: "v", 11: "b", 12: "q",
        13: "w", 14: "e", 15: "r", 16: "y", 17: "t", 31: "o", 32: "u", 34: "i", 35: "p", 37: "l", 38: "j",
        40: "k", 45: "n", 46: "m",
    ]
    static let returnKey: Int64 = 36, tab: Int64 = 48, space: Int64 = 49, delete: Int64 = 51, escape: Int64 = 53
    static let keypadEnter: Int64 = 76, rightCommand: Int64 = 54, rightOption: Int64 = 61

    let engine: Engine
    var enabled = true
    /// Key code of the key that flips a word (right ⌘ by default).
    var flipKey: Int64 = FlipKeys.defaultKey
    /// Key code of the key that toggles the preview mode (Caps Lock by default), or FlipKeys.none.
    var previewKey: Int64 = FlipKeys.previewDefault
    /// Called after the preview toggle key switched the engine's preview mode.
    var onPreviewToggled: ((PreviewMode) -> Void)?
    /// Set by the settings window: reports the next allowed key pressed (nil on esc).
    var recordNext: ((Int64?) -> Void)?
    var excludedApps: Set<String> = []
    /// Leave password and verification-code fields alone.
    var skipSensitiveFields = true
    var currentApp: String? = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
    var onKeyHandled: (() -> Void)?

    private var tap: CFMachPort?
    private var flipArmed = false, previewArmed = false
    private var flipDownAt = Date.distantPast, previewDownAt = Date.distantPast
    private let source = CGEventSource(stateID: .hidSystemState)

    init(engine: Engine) {
        self.engine = engine
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification,
                                                          object: nil, queue: .main) { [weak self] note in
            let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
            self?.currentApp = app?.bundleIdentifier
            self?.engine.reset()
            self?.onKeyHandled?()
        }
    }

    /// Installs the tap; fails until the user allows Accessibility for this app.
    func start() -> Bool {
        let mask: CGEventMask = [CGEventType.keyDown, .keyUp, .flagsChanged, .leftMouseDown, .rightMouseDown, .otherMouseDown]
            .reduce(0) { $0 | (1 << $1.rawValue) }
        let callback: CGEventTapCallBack = { _, type, event, info in
            let me = Unmanaged<KeyTap>.fromOpaque(info!).takeUnretainedValue()
            return me.handle(type, event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                          eventsOfInterest: mask, callback: callback,
                                          userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return false }
        self.tap = tap
        let loop = CFMachPortCreateRunLoopSource(nil, tap, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), loop, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        return true
    }

    private func handle(_ type: CGEventType, _ event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        }
        guard enabled, event.getIntegerValueField(.eventSourceUserData) != Self.tag else {
            return Unmanaged.passUnretained(event)
        }
        defer { onKeyHandled?() }
        switch type {
        case .leftMouseDown, .rightMouseDown, .otherMouseDown:
            engine.reset()
            return Unmanaged.passUnretained(event)
        case .flagsChanged:
            return flagsChanged(event) ? nil : Unmanaged.passUnretained(event)
        case .keyDown:
            return keyDown(event) ? nil : Unmanaged.passUnretained(event)
        default:
            return Unmanaged.passUnretained(event)
        }
    }

    /// A lone tap of the flip key (right ⌘ by default) flips the word; of the preview key, toggles the preview.
    /// Returns whether the event is swallowed.
    private func flagsChanged(_ event: CGEvent) -> Bool {
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        if let record = recordNext, code == FlipKeys.capsLock || FlipKeys.modifierFlags[code] != nil {
            recordNext = nil
            DispatchQueue.main.async { record(code) }
            return false
        }
        if paused() { flipArmed = false; previewArmed = false; return false }
        if code == flipKey {
            if tapped(code, event, &flipArmed, &flipDownAt) { apply(engine.flip(), original: nil) }
            return false
        }
        if code == previewKey {
            if tapped(code, event, &previewArmed, &previewDownAt) { togglePreview() }
            // Kept from the system, which would otherwise switch to a Korean input source.
            return code == FlipKeys.capsLock
        }
        flipArmed = false
        previewArmed = false
        return false
    }

    /// Whether this change of a modifier completes a lone tap (press and release within half a second).
    private func tapped(_ code: Int64, _ event: CGEvent, _ armed: inout Bool, _ downAt: inout Date) -> Bool {
        // Caps Lock reports each press as a toggle, so every change is one press.
        if code == FlipKeys.capsLock { return true }
        guard let flag = FlipKeys.modifierFlags[code] else { return false }
        if event.flags.contains(flag) {
            armed = true
            downAt = Date()
            return false
        }
        defer { armed = false }
        return armed && Date().timeIntervalSince(downAt) < 0.5
    }

    private func togglePreview() {
        apply(engine.togglePreview(), original: nil)
        onPreviewToggled?(engine.preview)
        // Caps Lock may still have switched to a Korean input source on its way through.
        guard previewKey == FlipKeys.capsLock else { return }
        for delay in [0.05, 0.3] { DispatchQueue.main.asyncAfter(deadline: .now() + delay) { InputSource.selectLatin() } }
    }

    /// Returns whether the original key is swallowed.
    private func keyDown(_ event: CGEvent) -> Bool {
        flipArmed = false
        previewArmed = false
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        let flags = event.flags
        if let record = recordNext {
            if code == Self.escape { recordNext = nil; DispatchQueue.main.async { record(nil) } }
            else if Self.letters[code] == nil && ![Self.returnKey, Self.tab, Self.space, Self.delete, Self.keypadEnter].contains(code)
                        && typed(event) == nil {
                recordNext = nil
                DispatchQueue.main.async { record(code) }
            }
            return true
        }
        if FlipKeys.modifierFlags[code] == nil && code != FlipKeys.capsLock && !paused() {
            // A dedicated key (F13, 영문 키...): act on press and keep it from doing anything else.
            if code == flipKey { apply(engine.flip(), original: nil); return true }
            if code == previewKey { togglePreview(); return true }
        }
        if !flags.intersection([.maskCommand, .maskControl, .maskAlternate]).isEmpty || paused() {
            engine.reset()
            return false
        }
        if let letter = Self.letters[code] {
            if engine.composing.isEmpty && skipSensitiveFields && SensitiveField.focused() {
                engine.reset()
                return false
            }
            if engine.composing.isEmpty { InputSource.selectLatin() }
            // When Caps Lock is one of our keys, its lit state says nothing about letter case.
            let capsOurs = flipKey == FlipKeys.capsLock || previewKey == FlipKeys.capsLock
            let capsOn = flags.contains(.maskAlphaShift) && !capsOurs
            let upper = flags.contains(.maskShift) != capsOn
            let char = upper ? Character(letter.uppercased()) : letter
            if capsOurs && flags.contains(.maskAlphaShift) {
                // A letter passed through as is would still come out in capitals.
                let units = Array(String(char).utf16)
                event.flags.remove(.maskAlphaShift)
                event.keyboardSetUnicodeString(stringLength: units.count, unicodeString: units)
            }
            return apply(engine.letter(char), original: event)
        }
        switch code {
        case Self.delete: return apply(engine.backspace(), original: event)
        case Self.escape: return apply(engine.escape(), original: event)
        case Self.returnKey, Self.tab, Self.keypadEnter: return apply(engine.boundary(nil), original: event)
        default: break
        }
        if let text = typed(event) { return apply(engine.boundary(text), original: event) }
        engine.reset()   // arrows, Home, forward delete, F-keys...: the caret may have moved
        return false
    }

    private func paused() -> Bool {
        if IsSecureEventInputEnabled() { return true }   // password fields
        return currentApp.map(excludedApps.contains) ?? false
    }

    /// The printable character a key types, or nil.
    private func typed(_ event: CGEvent) -> String? {
        var length = 0
        var chars = [UniChar](repeating: 0, count: 4)
        event.keyboardGetUnicodeString(maxStringLength: chars.count, actualStringLength: &length, unicodeString: &chars)
        guard length == 1 else { return nil }
        let unit = chars[0]
        // Control characters and the private-use codes macOS gives arrows and function keys.
        if unit < 0x20 || unit == 0x7F || (0xF700...0xF8FF).contains(unit) { return nil }
        return String(utf16CodeUnits: [unit], count: 1)
    }

    @discardableResult
    private func apply(_ action: KeyAction, original: CGEvent?) -> Bool {
        guard action.injects || action.replay else { return action.swallow }
        for _ in 0..<action.backspaces {
            post(key: CGKeyCode(Self.delete), down: true)
            post(key: CGKeyCode(Self.delete), down: false)
        }
        for c in action.text {
            post(text: String(c), down: true)
            post(text: String(c), down: false)
        }
        if action.replay, let copy = original?.copy() {
            // The physical key-up arrives later and passes through on its own.
            copy.setIntegerValueField(.eventSourceUserData, value: Self.tag)
            copy.post(tap: .cgSessionEventTap)
        }
        return action.swallow
    }

    private func post(key: CGKeyCode, down: Bool) {
        guard let e = CGEvent(keyboardEventSource: source, virtualKey: key, keyDown: down) else { return }
        e.flags = []
        e.setIntegerValueField(.eventSourceUserData, value: Self.tag)
        e.post(tap: .cgSessionEventTap)
    }

    private func post(text: String, down: Bool) {
        guard let e = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: down) else { return }
        let units = Array(text.utf16)
        e.flags = []
        e.keyboardSetUnicodeString(stringLength: units.count, unicodeString: units)
        e.setIntegerValueField(.eventSourceUserData, value: Self.tag)
        e.post(tap: .cgSessionEventTap)
    }
}

/// Keeps an ASCII layout (ABC) active: Hanglish composes Hangul itself, so a Korean input
/// source would compose the same keys a second time.
enum InputSource {
    static func selectLatin() {
        guard let current = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              !isASCIICapable(current) else { return }
        let filter = [kTISPropertyInputSourceIsASCIICapable as String: true,
                      kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String] as CFDictionary
        guard let list = TISCreateInputSourceList(filter, false)?.takeRetainedValue() as? [TISInputSource] else { return }
        let preferred = list.first { id($0) == "com.apple.keylayout.ABC" } ?? list.first { id($0) == "com.apple.keylayout.US" } ?? list.first
        if let preferred { TISSelectInputSource(preferred) }
    }

    static func isASCIICapable(_ source: TISInputSource) -> Bool {
        guard let pointer = TISGetInputSourceProperty(source, kTISPropertyInputSourceIsASCIICapable) else { return false }
        return CFBooleanGetValue(Unmanaged<CFBoolean>.fromOpaque(pointer).takeUnretainedValue())
    }

    static func id(_ source: TISInputSource) -> String? {
        guard let pointer = TISGetInputSourceProperty(source, kTISPropertyInputSourceID) else { return nil }
        return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
    }
}

/// Recognizes verification-code fields by their label (password fields already turn on secure input).
enum SensitiveField {
    static let label = try! NSRegularExpression(
        pattern: #"비밀번호|암호|패스워드|인증|보안\s*문자|보안\s*코드|확인\s*코드|otp|one[-\s]?time|verif|passcode|password|passwd|\bpin\b|captcha|security\s*code|auth(entication)?\s*code"#,
        options: [.caseInsensitive])

    static func focused() -> Bool {
        let system = AXUIElementCreateSystemWide()
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &value) == .success,
              let value else { return false }
        let element = value as! AXUIElement
        if string(element, kAXSubroleAttribute) == (kAXSecureTextFieldSubrole as String) { return true }
        let texts = [kAXTitleAttribute, kAXDescriptionAttribute, kAXPlaceholderValueAttribute, kAXHelpAttribute, "AXIdentifier"]
            .compactMap { string(element, $0) }
        return texts.contains { label.firstMatch(in: $0, range: NSRange($0.startIndex..., in: $0)) != nil }
    }

    private static func string(_ element: AXUIElement, _ attribute: String) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success else { return nil }
        return value as? String
    }
}
