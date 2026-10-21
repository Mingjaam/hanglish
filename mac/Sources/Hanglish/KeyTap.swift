import AppKit
import Carbon.HIToolbox
import HanglishCore

enum FlipKey: String { case rightCommand, rightOption }

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
    var flipKey: FlipKey = .rightCommand
    var excludedApps: Set<String> = []
    var currentApp: String? = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
    var onKeyHandled: (() -> Void)?

    private var tap: CFMachPort?
    private var flipArmed = false
    private var flipDownAt = Date.distantPast
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
            flagsChanged(event)
            return Unmanaged.passUnretained(event)
        case .keyDown:
            return keyDown(event) ? nil : Unmanaged.passUnretained(event)
        default:
            return Unmanaged.passUnretained(event)
        }
    }

    /// A lone tap of the flip key (right ⌘ by default) flips the word.
    private func flagsChanged(_ event: CGEvent) {
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        let (key, flag): (Int64, CGEventFlags) = flipKey == .rightCommand
            ? (Self.rightCommand, .maskCommand) : (Self.rightOption, .maskAlternate)
        guard code == key else { flipArmed = false; return }
        if event.flags.contains(flag) {
            flipArmed = true
            flipDownAt = Date()
        } else if flipArmed {
            flipArmed = false
            if Date().timeIntervalSince(flipDownAt) < 0.5 { apply(engine.flip(), original: nil) }
        }
    }

    /// Returns whether the original key is swallowed.
    private func keyDown(_ event: CGEvent) -> Bool {
        flipArmed = false
        let code = event.getIntegerValueField(.keyboardEventKeycode)
        let flags = event.flags
        if !flags.intersection([.maskCommand, .maskControl, .maskAlternate]).isEmpty || paused() {
            engine.reset()
            return false
        }
        if let letter = Self.letters[code] {
            if engine.composing.isEmpty { InputSource.selectLatin() }
            let upper = flags.contains(.maskShift) != flags.contains(.maskAlphaShift)
            return apply(engine.letter(upper ? Character(letter.uppercased()) : letter), original: event)
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
