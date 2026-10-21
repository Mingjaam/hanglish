import Foundation

public enum Language { case english, korean, mixed }

public struct Choice: Equatable {
    public let text: String
    public let language: Language
    public let probability: Double
}

public struct Decision {
    public let raw: String
    public let best: Choice
    public let ranking: [Choice]
}

/// Chooses English, Hangul, or an English word with a Korean particle for one typed word.
/// Mirrors src/Hanglish.Core/Decider.cs.
public final class Decider {
    // English stems that commonly take a Korean particle: "github" + "에서".
    static let particles = [
        "은", "는", "이", "가", "을", "를", "에", "에서", "의", "도", "로", "으로", "와", "과", "랑", "이랑",
        "하고", "에게", "한테", "까지", "부터", "만", "이다", "입니다", "이에요", "예요", "야", "이야", "처럼",
        "보다", "같은", "해", "해서", "하는", "한", "할", "했다", "했어", "했어요", "해요", "합니다", "하면",
        "하기", "된", "되는", "인", "이고", "에서는", "으로는", "에는", "이나", "나", "라고", "이라고", "께서",
        "이라서", "라서", "이니까", "니까", "이라", "라", "이랑은", "에게서", "으로서", "로서", "이지만", "지만",
        "이었어", "였어", "이었다", "였다", "이죠", "죠", "이네", "네", "이래", "래", "하자", "할게", "할까",
    ]

    /// Scoring knobs, tuned on tests/corpus/*_dev.txt and checked on *_test.txt.
    public struct Tuning {
        public var mixedPrior = 0.02
        public var mixedStemMin = 1e-7
        public var unknownStemPenalty = 0.05
        public var syllableBonus = 5.0
        public var priorLow = 0.05
        public var priorHigh = 0.95
        public var contextDecay = 0.6
        public var contextWords = 6
        public var commonWord = 1e-5
        public var loosePenalty = 0.03
        public var shiftPenalty = 0.02
        /// Per letter when Shift is read as held over from the letter before (있다 typed as dlTEk).
        public var slipPenalty = 0.2
        /// Per lower-to-upper switch inside an English word (gkTEk is not English).
        public var casePenalty = 0.05
        public init() {}
    }

    public var knobs = Tuning()

    let english = KeyModel(), korean = KeyModel()
    let particleKeys: [String: String]

    /// Loads the bundled lexicon: lines of "<k|e>\t<word>\t<frequency>".
    public convenience init() throws {
        // The app ships the lexicon in Contents/Resources; tests and `swift run` use the package bundle.
        guard let url = Bundle.main.url(forResource: "lexicon", withExtension: "tsv")
                ?? Bundle.module.url(forResource: "lexicon", withExtension: "tsv") else {
            throw NSError(domain: "Hanglish", code: 1, userInfo: [NSLocalizedDescriptionKey: "Lexicon resource missing"])
        }
        try self.init(lexicon: String(contentsOf: url, encoding: .utf8))
    }

    public init(lexicon: String) {
        for line in lexicon.split(separator: "\n") {
            let parts = line.split(separator: "\t")
            guard parts.count == 3, let frequency = Double(parts[2]) else { continue }
            if parts[0] == "e" { english.add(String(parts[1]), frequency) }
            else if let keys = Hangul.toKeys(String(parts[1])) { korean.add(keys, frequency) }
        }
        // Chat shorthand made only of jamo: ㅋㅋ, ㅎㅎ, ㅠㅠ, ㅇㅋ ...
        for jamo in ["ㅋ", "ㅎ", "ㅠ", "ㅜ"] {
            for n in 2...8 { korean.add(Hangul.toKeys(String(repeating: jamo, count: n))!, 2e-4 / Double(n)) }
        }
        for shorthand in ["ㅇㅇ", "ㄴㄴ", "ㄱㄱ", "ㅇㅋ", "ㄷㄷ", "ㅊㅋ", "ㅈㅅ", "ㄱㅅ", "ㅅㄱ", "ㅂㅂ", "ㅎㅇ", "ㄹㅇ"] {
            korean.add(Hangul.toKeys(shorthand)!, 5e-5)
        }
        english.train()
        korean.train()
        particleKeys = Dictionary(uniqueKeysWithValues: Self.particles.map { (Hangul.toKeys($0)!, $0) })
    }

    /// - Parameters:
    ///   - raw: Latin letters as typed.
    ///   - before: Text already typed before this word in the same field.
    public func decide(_ raw: String, before: String = "") -> Decision {
        let koreanPrior = self.koreanPrior(before)
        var scores: [(text: String, language: Language, log: Double)] = [
            (raw, .english, log(1 - koreanPrior) + englishLog(raw)),
            (Hangul.compose(raw), .korean, log(koreanPrior) + koreanLog(raw)),
        ]
        // Shift held a key too long: any capital right after another capital may really be unshifted.
        for (unslipped, slips) in Self.unslips(raw) {
            scores.append((Hangul.compose(unslipped), .korean,
                           log(koreanPrior) + koreanLog(unslipped) + Double(slips) * log(knobs.slipPenalty)))
        }
        let chars = Array(raw)
        if chars.count > 2 {
            for split in 2..<chars.count {
                let stem = String(chars[..<split])
                let known = english.frequency(stem.lowercased()) >= knobs.mixedStemMin
                if !known && split < 3 { continue }
                if let particle = particleKeys[Hangul.koreanKeys(String(chars[split...]))] {
                    scores.append((stem + particle, .mixed,
                                   log(knobs.mixedPrior) + englishLog(stem) + log(1.0 / Double(Self.particles.count))
                                   + (known ? 0 : log(knobs.unknownStemPenalty))))
                }
            }
        }
        let maxLog = scores.map(\.log).max()!
        let sum = scores.reduce(0) { $0 + exp($1.log - maxLog) }
        // Readings that produce the same text pool their probability; the likeliest names the language.
        var order: [String] = []
        var pooled: [String: (language: Language, best: Double, mass: Double)] = [:]
        for s in scores {
            let mass = exp(s.log - maxLog)
            if let p = pooled[s.text] {
                pooled[s.text] = (s.log > p.best ? s.language : p.language, max(p.best, s.log), p.mass + mass)
            } else {
                order.append(s.text)
                pooled[s.text] = (s.language, s.log, mass)
            }
        }
        let ranking = order.enumerated()
            .map { (index: $0.offset, choice: Choice(text: $0.element, language: pooled[$0.element]!.language,
                                                      probability: pooled[$0.element]!.mass / sum)) }
            .sorted { $0.choice.probability != $1.choice.probability ? $0.choice.probability > $1.choice.probability : $0.index < $1.index }
            .map(\.choice)
        return Decision(raw: raw, best: ranking[0], ranking: ranking)
    }

    /// Prior for Korean from the words before this one, judged by the script each word ends in
    /// (so "github에서" counts as Korean for what follows), recent words weighing more.
    func koreanPrior(_ before: String) -> Double {
        let lasts = before.split(separator: " ")
            .compactMap { $0.last(where: { ($0.isASCII && $0.isLetter) || Hangul.isKoreanScript($0) }) }
            .suffix(knobs.contextWords).reversed()
        if lasts.isEmpty { return 0.5 }
        var weight = 1.0, total = 0.0, koreanWeight = 0.0
        for last in lasts {
            total += weight
            if Hangul.isKoreanScript(last) { koreanWeight += weight }
            weight *= knobs.contextDecay
        }
        return knobs.priorLow + (knobs.priorHigh - knobs.priorLow) * koreanWeight / total
    }

    /// Both readings are everyday words (to/새, do/애), so only the following words can tell.
    public func bothCommon(_ raw: String) -> Bool {
        english.frequency(raw.lowercased()) >= knobs.commonWord
            && korean.frequency(Hangul.koreanKeys(raw)) >= knobs.commonWord
    }

    /// The Hangul reading of these keys is a word Korean speakers actually write (ㅋㅋ, 예).
    public func knownKorean(_ raw: String) -> Bool { korean.contains(Hangul.koreanKeys(raw)) }

    func englishLog(_ raw: String) -> Double {
        // "Hello" and "NASA" are fine; a capital after a small letter (heLLo) is not how English is typed.
        let chars = Array(raw)
        var switches = 0
        for i in chars.indices.dropFirst() where Self.isLower(chars[i - 1]) && Self.isUpper(chars[i]) { switches += 1 }
        return english.wordLog(raw.lowercased()) + Double(switches) * log(knobs.casePenalty)
    }

    static func isUpper(_ c: Character) -> Bool { c.isASCII && c.isLetter && c.isUppercase }
    static func isLower(_ c: Character) -> Bool { c.isASCII && c.isLetter && c.isLowercase }

    /// Every way of lowering capitals that directly follow another capital (at most 4 such keys).
    static func unslips(_ raw: String) -> [(String, Int)] {
        let chars = Array(raw)
        let followers = Array(chars.indices.dropFirst().filter { isUpper(chars[$0 - 1]) && isUpper(chars[$0]) }.prefix(4))
        guard !followers.isEmpty else { return [] }
        var result: [(String, Int)] = []
        for mask in 1..<(1 << followers.count) {
            var copy = chars
            var slips = 0
            for (b, i) in followers.enumerated() where (mask >> b) & 1 != 0 {
                copy[i] = Character(copy[i].lowercased())
                slips += 1
            }
            result.append((String(copy), slips))
        }
        return result
    }

    func koreanLog(_ raw: String) -> Double {
        let keys = Hangul.koreanKeys(raw)
        let ignoredShifts = zip(raw, keys).filter { $0 != $1 }.count
        let composed = Hangul.compose(raw)
        let loose = korean.contains(keys) ? 0 : Hangul.looseJamo(composed)
        let whole = loose == 0 && composed.allSatisfy(Hangul.isSyllable)
            ? Double(composed.count) * log(knobs.syllableBonus) : 0
        return whole + korean.wordLog(keys) + Double(ignoredShifts) * log(knobs.shiftPenalty)
            + Double(loose) * log(knobs.loosePenalty)
    }
}
