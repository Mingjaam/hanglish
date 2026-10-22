import AppKit
import HanglishCore
import ServiceManagement
import SwiftUI

/// Bridges the app's live settings into SwiftUI; every change applies immediately.
final class SettingsModel: ObservableObject {
    unowned let app: AppDelegate
    @Published var corrections: [Correction] = []

    init(app: AppDelegate) {
        self.app = app
        corrections = app.corrections.entries
    }

    var enabled: Bool { get { app.enabled } set { app.enabled = newValue; objectWillChange.send() } }
    var caretPreview: Bool { get { app.caretPreview } set { app.caretPreview = newValue; objectWillChange.send() } }
    var preview: PreviewMode { get { app.preview } set { app.preview = newValue; objectWillChange.send() } }
    var flipKey: Int64 {
        get { app.flipKey }
        set { if accept(newValue, other: app.previewKey, otherName: "미리보기 전환 키") { app.flipKey = newValue } }
    }
    var previewKey: Int64 {
        get { app.previewKey }
        set { if accept(newValue, other: app.flipKey, otherName: "뒤집기 키") { app.previewKey = newValue } }
    }

    enum KeySetting { case flip, preview }
    /// Which key setting waits for the next key press.
    @Published var recording: KeySetting?
    /// Why the last key choice was refused (the other setting uses it).
    @Published var keyProblem: String?

    /// The two key settings never share a key.
    private func accept(_ key: Int64, other: Int64, otherName: String) -> Bool {
        objectWillChange.send()
        keyProblem = FlipKeys.same(key, other) ? "\(FlipKeys.name(key))은(는) 이미 \(otherName)로 쓰고 있어요." : nil
        return keyProblem == nil
    }

    /// The next key pressed becomes the chosen key; esc cancels.
    func record(_ setting: KeySetting) {
        recording = setting
        app.tap.recordNext = { [weak self] code in
            guard let self else { return }
            if let code {
                switch setting {
                case .flip: self.flipKey = code
                case .preview: self.previewKey = code
                }
            }
            self.recording = nil
        }
    }
    var skipSensitiveFields: Bool { get { app.skipSensitiveFields } set { app.skipSensitiveFields = newValue; objectWillChange.send() } }
    var bubbleStyle: BubbleStyle { get { app.bubbleStyle } set { app.bubbleStyle = newValue; objectWillChange.send() } }
    var excludedApps: [String] { app.excludedApps }

    var launchAtLogin: Bool {
        get { SMAppService.mainApp.status == .enabled }
        set {
            try? newValue ? SMAppService.mainApp.register() : SMAppService.mainApp.unregister()
            objectWillChange.send()
        }
    }

    func include(_ id: String) { app.excludedApps.removeAll { $0 == id }; objectWillChange.send() }

    func remove(_ entry: Correction) { app.corrections.remove(entry); corrections = app.corrections.entries }

    func clearCorrections() { app.corrections.clear(); corrections = [] }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        TabView {
            general.tabItem { Label("일반", systemImage: "gearshape") }
            keys.tabItem { Label("키", systemImage: "keyboard") }
            bubble.tabItem { Label("말풍선", systemImage: "text.bubble") }
            apps.tabItem { Label("쓰지 않는 앱", systemImage: "nosign") }
            corrections.tabItem { Label("저장된 교정", systemImage: "arrow.left.arrow.right") }
            about.tabItem { Label("정보", systemImage: "info.circle") }
        }
        .frame(width: 520, height: 440)
    }

    var general: some View {
        Form {
            Toggle("한글리쉬 사용", isOn: $model.enabled)
            Toggle("로그인할 때 실행", isOn: $model.launchAtLogin)
            Toggle("비밀번호·인증번호 칸에서는 쉬기", isOn: $model.skipSensitiveFields)
            Picker("입력하는 동안", selection: $model.preview) {
                Text("한글로 미리 보기").tag(PreviewMode.hangul)
                Text("영문 그대로 보이기").tag(PreviewMode.raw)
            }
            .pickerStyle(.radioGroup)
            if model.previewKey != FlipKeys.none {
                Text("치는 중에도 \(FlipKeys.name(model.previewKey))로 바로 바꿀 수 있어요. 키는 '키' 탭에서 바꿀 수 있어요.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(24)
    }

    var keys: some View {
        Form {
            keyPicker("뒤집기 키", selection: $model.flipKey, presets: FlipKeys.presets, defaultKey: FlipKeys.defaultKey,
                      other: model.previewKey, otherName: "미리보기 전환 키", setting: .flip)
            Text("지금 치는 단어나 방금 확정된 단어를 반대 언어로 바꿔요. ⌘ · ⌥ 같은 키는 혼자 눌렀다 뗄 때만 동작해요.")
                .font(.caption).foregroundStyle(.secondary)
            keyPicker("미리보기 전환 키", selection: $model.previewKey, presets: FlipKeys.previewPresets,
                      defaultKey: FlipKeys.previewDefault, other: model.flipKey, otherName: "뒤집기 키", setting: .preview)
                .padding(.top, 8)
            Text("치는 동안 한글로 보일지 영문 그대로 보일지 바꿔요. 단어를 치는 중에 눌러도 바로 바뀌어 보여요.")
                .font(.caption).foregroundStyle(.secondary)
            if let problem = model.keyProblem {
                Text(problem).font(.caption).foregroundStyle(.red)
            }
        }
        .padding(24)
    }

    /// Preset radios plus "pick another key" for one key setting.
    func keyPicker(_ title: String, selection: Binding<Int64>, presets: [Int64], defaultKey: Int64, other: Int64,
                   otherName: String, setting: SettingsModel.KeySetting) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Picker(title, selection: selection) {
                ForEach(presets, id: \.self) { code in
                    Text(FlipKeys.name(code) + (code == defaultKey ? " (기본)" : "")
                         + (FlipKeys.same(code, other) ? " — \(otherName)로 사용 중" : "")).tag(code)
                }
                if !presets.contains(selection.wrappedValue) {
                    Text(FlipKeys.name(selection.wrappedValue)).tag(selection.wrappedValue)
                }
            }
            .pickerStyle(.radioGroup)
            Button(model.recording == setting ? "바꿀 키를 누르세요 (esc: 취소)" : "다른 키로 바꾸기...") { model.record(setting) }
                .disabled(model.recording != nil)
        }
    }

    var bubble: some View {
        VStack(alignment: .leading, spacing: 14) {
            Toggle("커서 위에 결과 미리 보기", isOn: $model.caretPreview)
            Text("단어를 다 치기 전에 어떻게 확정될지 커서 바로 위에 보여 줍니다.")
                .foregroundStyle(.secondary).font(.callout)
            Text("디자인").font(.headline).padding(.top, 6)
            HStack(spacing: 12) {
                ForEach([(BubbleStyle.white, "흰색"), (.dark, "어두운색"), (.minimal, "간단히")], id: \.0) { style, name in
                    Button { model.bubbleStyle = style } label: {
                        VStack(spacing: 6) {
                            Image(nsImage: CaretBubble.sample(Guess(text: "김민재", korean: true, alternative: "rlaalswo", confidence: 1),
                                                              style: style))
                            Text(name)
                        }
                        .frame(width: 140, height: 84)
                        .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
                        .overlay(RoundedRectangle(cornerRadius: 10)
                            .stroke(model.bubbleStyle == style ? Color(nsColor: CaretBubble.korean) : .clear, lineWidth: 2))
                    }
                    .buttonStyle(.plain)
                }
            }
            Spacer()
        }
        .padding(24)
    }

    var apps: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("여기 있는 앱에서는 한글리쉬가 키를 건드리지 않습니다. 메뉴 막대의 \"…에서는 쓰지 않기\"로 추가하세요.")
                .foregroundStyle(.secondary).font(.callout)
            List(model.excludedApps, id: \.self) { id in
                HStack {
                    Text(id)
                    Spacer()
                    Button("다시 쓰기") { model.include(id) }
                }
            }
        }
        .padding(24)
    }

    var corrections: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("뒤집기 키로 바꾼 단어들입니다.").foregroundStyle(.secondary).font(.callout)
            Table(model.corrections) {
                TableColumn("친 키", value: \.raw)
                TableColumn("결과", value: \.preferred)
                TableColumn("적용") { entry in
                    Text(entry.reach == .everywhere ? "어디서나" : entry.before.isEmpty ? "맨 앞에서" : "'\(entry.before)' 뒤에서만")
                }
                TableColumn("") { entry in Button("지우기") { model.remove(entry) } }.width(60)
            }
            HStack { Spacer(); Button("모두 지우기") { model.clearCorrections() }.disabled(model.corrections.isEmpty) }
        }
        .padding(24)
    }

    var about: some View {
        VStack(spacing: 10) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 80, height: 80)
            Text("한글리쉬 Hanglish \(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "")")
                .font(.title3.bold())
            Text("ABC 입력 소스 그대로 두벌식 자리를 치면, 단어가 끝날 때 한글로 쓸지 영어로 쓸지 골라 줍니다. 판단은 이 Mac 안에서만 하며 입력 내용을 보내거나 저장하지 않습니다.")
                .multilineTextAlignment(.center).foregroundStyle(.secondary)
            Link("github.com/Mingjaam/hanglish", destination: URL(string: "https://github.com/Mingjaam/hanglish")!)
            Text("MIT License · 단어 빈도 데이터: FrequencyWords, wordfreq (CC BY-SA 4.0)").font(.caption).foregroundStyle(.secondary)
        }
        .padding(28)
    }
}

extension Correction: Identifiable {
    public var id: String { "\(raw)\u{1}\(before)\u{1}\(preferred)" }
}

/// Opens (or brings back) the settings window.
final class SettingsWindow {
    private var window: NSWindow?
    private var model: SettingsModel?

    /// Shows changes made from the keyboard (the preview toggle key) in an open window.
    func refresh() { model?.objectWillChange.send() }

    func show(app: AppDelegate) {
        if window == nil {
            let model = SettingsModel(app: app)
            self.model = model
            let host = NSHostingController(rootView: SettingsView(model: model))
            let window = NSWindow(contentViewController: host)
            window.title = "한글리쉬 설정"
            window.styleMask = [.titled, .closable]
            window.isReleasedWhenClosed = false
            window.center()
            self.window = window
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}
