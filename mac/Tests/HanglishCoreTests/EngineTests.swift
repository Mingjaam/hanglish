import XCTest
@testable import HanglishCore

/// The same state-machine cases as src/Hanglish.Eval/EngineTests.cs, plus a parity check:
/// the macOS engine must type what the Windows engine typed (Fixtures/parity.tsv).
final class EngineTests: XCTestCase {
    static let decider: Decider = try! Decider()

    /// A simulated text field. Keys: "_" Space, "." period, "!" flip, "<" Backspace, "~" Esc.
    final class Field {
        var text = ""
        let engine: Engine

        init(_ mode: PreviewMode, store: CorrectionStore = CorrectionStore(url: nil)) {
            engine = Engine(decider: EngineTests.decider, corrections: store)
            engine.preview = mode
        }

        func apply(_ a: KeyAction, _ passed: String?) {
            text = String(text.dropLast(a.backspaces)) + a.text
            if (!a.swallow || a.replay), let passed {
                if passed == "\u{8}" { text = String(text.dropLast()) } else { text += passed }
            }
        }

        @discardableResult
        func type(_ keys: String) -> Field {
            for k in keys {
                switch k {
                case "_": apply(engine.boundary(" "), " ")
                case ".": apply(engine.boundary("."), ".")
                case "!": apply(engine.flip(), nil)
                case "<": apply(engine.backspace(), "\u{8}")
                case "~": apply(engine.escape(), nil)
                default: apply(engine.letter(k), String(k))
                }
            }
            return self
        }
    }

    func testStateMachine() {
        let cases: [(String, String)] = [
            ("dkssudgktpdy_", "안녕하세요 "), ("hello_world_", "hello world "), ("githubdptj_", "github에서 "),
            ("dkssus<d_", "안녕 "), ("dkssud_!", "dkssud "), ("dkssud_!!", "안녕 "), ("hello!", "ㅗ디ㅣㅐ"),
            ("dkssud~", "dkssud"), ("dkssud.", "안녕."), ("dkssud_<!", "dkssud"), ("rlaalswo_", "김민재 "),
            ("dkssud_go_", "안녕 해 "), ("so_good_", "so good "), ("go_home_", "go home "), ("so_dkssud_", "내 안녕 "),
            ("dlTEk_", "있다 "), ("dhsmf_to_do_list_", "오늘 to do list "),
        ]
        for mode in [PreviewMode.hangul, .raw] {
            for (keys, want) in cases {
                XCTAssertEqual(Field(mode).type(keys).text, want, "[\(mode)] \(keys)")
            }
        }
    }

    func testCorrectionsStayInContext() {
        let store = CorrectionStore(url: nil)
        func run(_ keys: String) -> String { Field(.hangul, store: store).type(keys).text }
        _ = run("rmfo_dkssud_!")
        XCTAssertEqual(run("rmfo_dkssud_"), "그래 dkssud ")
        XCTAssertEqual(run("dkssud_"), "안녕 ")
        _ = run("wjsms_dkssud_!")
        XCTAssertEqual(run("dkssud_"), "dkssud ")
    }

    /// Types a sentence's Dubeolsik keys and presses Return, like src/Hanglish.Eval/CorpusEval.cs.
    static func typeSentence(_ expected: String) -> String {
        let engine = Engine(decider: decider, corrections: CorrectionStore(url: nil))
        var field = ""
        func apply(_ a: KeyAction, _ passed: String?) {
            field = String(field.dropLast(a.backspaces)) + a.text
            if (!a.swallow || a.replay), let passed { field += passed }
        }
        for c in expected {
            if Hangul.isSyllable(c), let keys = Hangul.toKeys(String(c)) {
                for k in keys { apply(engine.letter(k), String(k)) }
            } else if c.isASCII, c.isLetter {
                apply(engine.letter(c), String(c))
            } else {
                apply(engine.boundary(String(c)), String(c))
            }
        }
        apply(engine.boundary(nil), nil)
        return field
    }

    func testParityWithWindowsEngine() throws {
        let url = try XCTUnwrap(Bundle.module.url(forResource: "parity", withExtension: "tsv", subdirectory: "Fixtures"))
        let lines = try String(contentsOf: url, encoding: .utf8).split(separator: "\n")
        var same = 0, correct = 0
        var differences: [String] = []
        for line in lines {
            let parts = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            let got = Self.typeSentence(parts[0])
            if got == parts[1] { same += 1 } else if differences.count < 20 { differences.append("\(parts[0]) → mac \(got) / win \(parts[1])") }
            if got == parts[0] { correct += 1 }
        }
        let parity = Double(same) / Double(lines.count)
        print("parity \(same)/\(lines.count), accuracy \(correct)/\(lines.count)")
        differences.forEach { print("  " + $0) }
        // Floating-point summation order differs slightly between the two engines.
        XCTAssertGreaterThanOrEqual(parity, 0.995)
    }
}
