import Foundation

public enum PreviewMode: String, Codable {
    /// Show Hangul while typing, like a Korean IME; switch to English at the word's end.
    case hangul = "Hangul"
    /// Let the Latin letters through and replace them at the word's end when Korean wins.
    case raw = "Raw"
}

/// What the word being typed will become at its end, for the caret preview.
public struct Guess: Equatable {
    public let text: String
    public let korean: Bool
    public let alternative: String
    public let confidence: Double
}

/// What the keyboard layer must do for one key: optionally swallow it, erase
/// `backspaces` characters, type `text`, then replay the key.
public struct KeyAction: Equatable {
    public var swallow: Bool
    public var backspaces = 0
    public var text = ""
    public var replay = false
    public static let pass = KeyAction(swallow: false)
    public var injects: Bool { backspaces > 0 || !text.isEmpty }
}

/// The typing state machine, independent of macOS. Mirrors src/Hanglish.Core/Engine.cs:
/// it tracks the word being typed, what is shown for it, and the words committed since the
/// caret last jumped, so a confident word can still correct ambiguous words before it ("so" + "good").
public final class Engine {
    final class Word {
        let raw: String
        let before: String      // preceding word, the context for remembered flips
        let model: String       // what the model alone chose
        var options: [String]   // [shown first, other reading]
        var shown = 0
        var trailing = ""       // boundary characters typed after the word
        var waiting = false     // ambiguous; a following word may still switch it
        var chosen = false      // set by the user or by memory, never switched automatically
        var text: String { options[shown] }
        var other: String { options[1 - shown] }

        init(raw: String, before: String, model: String, options: [String], chosen: Bool = false) {
            self.raw = raw; self.before = before; self.model = model; self.options = options; self.chosen = chosen
        }
    }

    static let anchorConfidence = 0.9, startAnchorConfidence = 0.97, waitBelow = 0.75
    static let waitWindow = 3, chainLimit = 20

    let decider: Decider
    let corrections: CorrectionStore
    var chain: [Word] = []
    var raw = "", shown = ""

    public var preview: PreviewMode = .hangul

    public init(decider: Decider, corrections: CorrectionStore) {
        self.decider = decider
        self.corrections = corrections
    }

    public var composing: String { raw }

    /// The expected result for the word being typed, or nil when nothing is being typed.
    public func guess() -> Guess? {
        if raw.isEmpty { return nil }
        let (word, confidence) = decide()
        return Guess(text: word.text,
                     korean: Self.endLanguage(word.text) == .korean && Self.startLanguage(word.text) == .korean,
                     alternative: word.other, confidence: confidence)
    }

    public func letter(_ key: Character) -> KeyAction {
        raw.append(key)
        if preview == .raw { shown = raw; return .pass }
        return show(previewText(), swallow: true)
    }

    public func backspace() -> KeyAction {
        if raw.isEmpty {
            // Erasing a trailing space keeps the words in play; erasing into a word ends tracking.
            if let last = chain.last, !last.trailing.isEmpty { last.trailing.removeLast() } else { chain.removeAll() }
            return .pass
        }
        raw.removeLast()
        if preview == .raw { shown = raw; return .pass }
        return show(raw.isEmpty ? "" : previewText(), swallow: true)
    }

    /// - Parameter text: The character the key types (" ", ".", "1"), or nil for Return/Tab.
    public func boundary(_ text: String?) -> KeyAction {
        if raw.isEmpty {
            if text == nil { chain.removeAll() } else if let last = chain.last { last.trailing += text! }
            return .pass
        }
        let (word, confidence) = decide()
        let (first, oldTail) = backfill(word, confidence)
        chain.append(word)
        var action = rewrite(first, oldTail)
        action.swallow = true
        action.replay = true
        word.trailing = text ?? ""
        if text == nil { chain.removeAll() }
        trim()
        return action
    }

    /// Esc: keep the word being typed as the Latin letters.
    public func escape() -> KeyAction {
        if raw.isEmpty || shown == raw { reset(); return .pass }
        let action = show(raw, swallow: true)
        chain.append(Word(raw: raw, before: previousWord(), model: raw, options: [raw, Hangul.compose(raw)], chosen: true))
        raw = ""; shown = ""
        trim()
        return action
    }

    /// The flip key: switch the current or just-committed word to the other reading and remember it.
    public func flip() -> KeyAction {
        if !raw.isEmpty {
            let (word, _) = decide()
            word.shown = 1 - word.shown
            word.chosen = true
            word.waiting = false
            let action = show(word.text, swallow: true)
            chain.append(word)
            raw = ""; shown = ""
            learn(word)
            return action
        }
        guard let last = chain.last else { return .pass }
        let erase = last.text.count + last.trailing.count
        last.shown = 1 - last.shown
        last.chosen = true
        last.waiting = false
        learn(last)
        return KeyAction(swallow: true, backspaces: erase, text: last.text + last.trailing)
    }

    /// The caret moved or focus changed: forget everything about the field.
    public func reset() {
        raw = ""; shown = ""
        chain.removeAll()
    }

    func previousWord() -> String { chain.last?.text ?? "" }

    func beforeText() -> String { chain.map { $0.text + $0.trailing }.joined() }

    func previewText() -> String { corrections.lookup(raw, before: previousWord()) ?? Hangul.compose(raw) }

    func decide() -> (Word, Double) {
        let decision = decider.decide(raw, before: beforeText())
        let model = decision.best.text
        let other = decision.best.language == .english ? Hangul.compose(raw) : raw
        let before = previousWord()
        let remembered = corrections.lookup(raw, before: before)
        if let remembered, remembered != model {
            return (Word(raw: raw, before: before, model: model, options: [remembered, model], chosen: true), 1)
        }
        let word = Word(raw: raw, before: before, model: model, options: [model, other])
        word.waiting = remembered == nil && readable(other, raw: raw)
            && (decision.best.probability < Self.waitBelow || decider.bothCommon(raw))
        return (word, decision.best.probability)
    }

    /// Lets a confident new word switch the ambiguous words right before it to its own language.
    /// Returns the index of the first chain word whose text may change, and the text shown from there on.
    func backfill(_ word: Word, _ confidence: Double) -> (Int, String) {
        var start = chain.count
        while start > 0, chain.count - start < Self.waitWindow, chain[start - 1].waiting, !chain[start - 1].chosen,
              !chain[start - 1].trailing.isEmpty, chain[start - 1].trailing.allSatisfy({ $0 == " " }) {
            start -= 1
        }
        let oldTail = chain[start...].map { $0.text + $0.trailing }.joined() + shown
        if start == chain.count || word.waiting || confidence < Self.anchorConfidence { return (start, oldTail) }

        // Each waiting word joins the language its neighbors meet it with: the script the
        // confident word starts in, and the script the word before the run ends in.
        let right = Self.startLanguage(word.text)
        let waiting = Array(chain[start...])
        let left: Language? = start > 0 ? Self.endLanguage(chain[start - 1].text) : nil
        let settle = left == right || waiting.count >= 2 || (left == nil && confidence >= Self.startAnchorConfidence)
        for w in waiting {
            // Switch only when the other reading is the one that meets the confident word in its language.
            if settle && Self.endLanguage(w.text) != right && Self.endLanguage(w.other) == right { w.shown = 1 - w.shown }
            w.waiting = false
        }
        return (start, oldTail)
    }

    /// Replaces what is shown from chain[first] on (plus the current word) with the chain's text.
    func rewrite(_ first: Int, _ oldTail: String) -> KeyAction {
        let newTail = chain[first...].map { $0.text + $0.trailing }.joined()
        let common = Self.commonPrefix(oldTail, newTail)
        raw = ""; shown = ""
        return KeyAction(swallow: true, backspaces: oldTail.count - common, text: String(newTail.dropFirst(common)))
    }

    func trim() {
        if chain.count > Self.chainLimit { chain.removeFirst(chain.count - Self.chainLimit) }
    }

    func learn(_ word: Word) {
        // Flipping back to the model's own choice undoes the memory for these keys here.
        if word.text == word.model { corrections.forget(word.raw, before: word.before) }
        else { corrections.remember(word.raw, before: word.before, preferred: word.text) }
    }

    static func startLanguage(_ text: String) -> Language {
        if let c = text.first, c.isASCII, c.isLetter { return .english }
        return .korean
    }

    static func endLanguage(_ text: String) -> Language {
        if let c = text.last, c.isASCII, c.isLetter { return .english }
        return .korean
    }

    /// A reading worth switching to later: whole syllables, or jamo the lexicon knows (ㅋㅋ).
    func readable(_ text: String, raw: String) -> Bool {
        text.allSatisfy { $0.isASCII || Hangul.isSyllable($0) } || decider.knownKorean(raw)
    }

    static func commonPrefix(_ a: String, _ b: String) -> Int {
        var n = 0
        for (x, y) in zip(a, b) { if x != y { break }; n += 1 }
        return n
    }

    func show(_ text: String, swallow: Bool) -> KeyAction {
        let common = Self.commonPrefix(shown, text)
        let action = KeyAction(swallow: swallow, backspaces: shown.count - common, text: String(text.dropFirst(common)))
        shown = text
        return action
    }
}
