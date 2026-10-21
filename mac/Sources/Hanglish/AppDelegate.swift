import AppKit
import ApplicationServices
import HanglishCore
import ServiceManagement

/// The menu bar item and its menu; owns the engine and the key tap. Mirrors src/Hanglish/TrayApp.cs.
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    static let help = """
        ABC 입력 소스 그대로 두벌식 자리를 치면 됩니다.
        띄어쓰기·리턴·문장부호를 누르는 순간 한글인지 영어인지 골라서 확정합니다.

        • Caps Lock(기본, 설정에서 바꿀 수 있음): 지금 치는 단어나 방금 확정된 단어를 반대 언어로 바꿉니다.
          바로 한 번 더 누르면 되돌립니다. 바꾼 결과는 같은 앞 단어 뒤에서만 기억하고,
          서로 다른 두 곳에서 같은 교정을 하면 어디서나 적용합니다.
        • 뒤 단어로 언어가 분명해지면 앞의 애매한 단어도 고칩니다 (내 good → so good).
        • esc: 치던 단어를 영문 그대로 확정합니다.
        • 커서 위 말풍선: 치는 동안 어떻게 확정될지 미리 보여 줍니다.

        판단은 이 Mac 안에서만 하며, 입력 내용은 어디에도 보내지 않습니다.
        """

    let defaults = UserDefaults.standard
    var statusItem: NSStatusItem!
    var engine: Engine!
    var tap: KeyTap!
    var corrections: CorrectionStore!
    let bubble = CaretBubble()
    let settingsWindow = SettingsWindow()
    var bubbleWork: DispatchWorkItem?
    var permissionTimer: Timer?

    var enabled: Bool {
        get { defaults.object(forKey: "enabled") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "enabled"); tap?.enabled = newValue; engine?.reset(); updateIcon() }
    }
    var caretPreview: Bool {
        get { defaults.object(forKey: "caretPreview") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "caretPreview"); if !newValue { bubble.hide() } }
    }
    var preview: PreviewMode {
        get { PreviewMode(rawValue: defaults.string(forKey: "preview") ?? "") ?? .hangul }
        set { defaults.set(newValue.rawValue, forKey: "preview"); engine.preview = newValue; engine.reset() }
    }
    var flipKey: Int64 {
        get { (defaults.object(forKey: "flipKeyCode") as? NSNumber)?.int64Value ?? FlipKeys.defaultKey }
        set { defaults.set(NSNumber(value: newValue), forKey: "flipKeyCode"); tap.flipKey = newValue }
    }
    var bubbleStyle: BubbleStyle {
        get { BubbleStyle(rawValue: defaults.string(forKey: "bubbleStyle") ?? "") ?? .white }
        set { defaults.set(newValue.rawValue, forKey: "bubbleStyle") }
    }
    var skipSensitiveFields: Bool {
        get { defaults.object(forKey: "skipSensitiveFields") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "skipSensitiveFields"); tap?.skipSensitiveFields = newValue }
    }
    var excludedApps: [String] {
        get { defaults.stringArray(forKey: "excludedApps") ?? [] }
        set { defaults.set(newValue, forKey: "excludedApps"); tap.excludedApps = Set(newValue) }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Hanglish")
        corrections = CorrectionStore(url: support.appendingPathComponent("corrections.json"))
        do { engine = Engine(decider: try Decider(), corrections: corrections) }
        catch {
            alert("판단 모델을 불러오지 못했습니다.\n\(error.localizedDescription)")
            NSApp.terminate(nil)
            return
        }
        engine.preview = preview
        tap = KeyTap(engine: engine)
        tap.enabled = enabled
        tap.flipKey = flipKey
        tap.excludedApps = Set(excludedApps)
        tap.skipSensitiveFields = skipSensitiveFields
        tap.onKeyHandled = { [weak self] in self?.scheduleBubble() }

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        updateIcon()
        startTap()
        // First launch: show where the settings live, next to the Accessibility prompt.
        if !defaults.bool(forKey: "shownWelcome") {
            defaults.set(true, forKey: "shownWelcome")
            settingsWindow.show(app: self)
        }
    }

    /// Opening the app again (Launchpad, Spotlight, Finder) while it runs opens the settings window.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        settingsWindow.show(app: self)
        return false
    }

    /// The tap needs the Accessibility permission; ask once, then wait for it.
    func startTap() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        if AXIsProcessTrustedWithOptions(options), tap.start() { return }
        permissionTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] timer in
            guard let self, AXIsProcessTrusted(), self.tap.start() else { return }
            timer.invalidate()
        }
    }

    /// Waits for the app to draw the injected text before reading the caret position.
    func scheduleBubble() {
        bubbleWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            self.bubble.present(self.enabled && self.caretPreview ? self.engine.guess() : nil, style: self.bubbleStyle)
        }
        bubbleWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.035, execute: work)
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        actions.removeAll()
        let open = item("설정 열기...", checked: false) { $0.settingsWindow.show(app: $0) }
        open.keyEquivalent = ","
        menu.addItem(open)
        menu.addItem(.separator())
        menu.addItem(item("한글리쉬 사용", checked: enabled) { $0.enabled.toggle() })
        menu.addItem(item("커서 위에 결과 미리 보기", checked: caretPreview) { $0.caretPreview.toggle() })
        if let app = NSWorkspace.shared.menuBarOwningApplication, let id = app.bundleIdentifier,
           id != Bundle.main.bundleIdentifier {
            let excluded = excludedApps.contains(id)
            menu.addItem(item("\(app.localizedName ?? id)에서는 쓰지 않기", checked: excluded) { me in
                me.excludedApps = excluded ? me.excludedApps.filter { $0 != id } : me.excludedApps + [id]
            })
        }
        menu.addItem(.separator())
        menu.addItem(item("사용법", checked: false) { $0.alert(Self.help) })
        menu.addItem(item("끝내기", checked: false) { _ in NSApp.terminate(nil) })
    }

    func updateIcon() {
        guard let button = statusItem?.button else { return }
        // The keycap face, as a template image so macOS tints it for light and dark menu bars.
        if let image = Bundle.main.image(forResource: "menubar") {
            image.isTemplate = true
            button.image = image
            button.title = ""
        } else {
            button.title = "한"
        }
        button.appearsDisabled = !enabled
        button.toolTip = enabled ? "한글리쉬: 켜짐" : "한글리쉬: 꺼짐"
    }

    // MARK: - Menu helpers

    private final class Action: NSObject {
        let run: (AppDelegate) -> Void
        weak var owner: AppDelegate?
        init(_ owner: AppDelegate, _ run: @escaping (AppDelegate) -> Void) { self.owner = owner; self.run = run }
        @objc func fire() { if let owner { run(owner) } }
    }
    private var actions: [Action] = []

    private func item(_ title: String, checked: Bool, _ run: @escaping (AppDelegate) -> Void) -> NSMenuItem {
        let action = Action(self, run)
        actions.append(action)
        let item = NSMenuItem(title: title, action: #selector(Action.fire), keyEquivalent: "")
        item.target = action
        item.state = checked ? .on : .off
        return item
    }

    func alert(_ text: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "한글리쉬"
        alert.informativeText = text
        alert.runModal()
    }
}
