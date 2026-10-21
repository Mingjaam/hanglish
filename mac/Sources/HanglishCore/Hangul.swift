import Foundation

/// Dubeolsik (두벌식) key ↔ Hangul conversion. Mirrors src/Hanglish.Core/Hangul.cs.
public enum Hangul {
    static let choseong = Array("ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ")
    static let jungseong = Array("ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ")
    // Index 0 is "no final consonant".
    static let jongseong = Array(" ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ")

    static let keyToJamo: [Character: Character] = [
        "q": "ㅂ", "w": "ㅈ", "e": "ㄷ", "r": "ㄱ", "t": "ㅅ", "y": "ㅛ", "u": "ㅕ", "i": "ㅑ", "o": "ㅐ", "p": "ㅔ",
        "a": "ㅁ", "s": "ㄴ", "d": "ㅇ", "f": "ㄹ", "g": "ㅎ", "h": "ㅗ", "j": "ㅓ", "k": "ㅏ", "l": "ㅣ",
        "z": "ㅋ", "x": "ㅌ", "c": "ㅊ", "v": "ㅍ", "b": "ㅠ", "n": "ㅜ", "m": "ㅡ",
        "Q": "ㅃ", "W": "ㅉ", "E": "ㄸ", "R": "ㄲ", "T": "ㅆ", "O": "ㅒ", "P": "ㅖ",
    ]

    struct Pair: Hashable { let a: Character, b: Character }

    static let vowelPairs: [Pair: Character] = [
        Pair(a: "ㅗ", b: "ㅏ"): "ㅘ", Pair(a: "ㅗ", b: "ㅐ"): "ㅙ", Pair(a: "ㅗ", b: "ㅣ"): "ㅚ",
        Pair(a: "ㅜ", b: "ㅓ"): "ㅝ", Pair(a: "ㅜ", b: "ㅔ"): "ㅞ", Pair(a: "ㅜ", b: "ㅣ"): "ㅟ", Pair(a: "ㅡ", b: "ㅣ"): "ㅢ",
    ]

    static let finalPairs: [Pair: Character] = [
        Pair(a: "ㄱ", b: "ㅅ"): "ㄳ", Pair(a: "ㄴ", b: "ㅈ"): "ㄵ", Pair(a: "ㄴ", b: "ㅎ"): "ㄶ", Pair(a: "ㄹ", b: "ㄱ"): "ㄺ",
        Pair(a: "ㄹ", b: "ㅁ"): "ㄻ", Pair(a: "ㄹ", b: "ㅂ"): "ㄼ", Pair(a: "ㄹ", b: "ㅅ"): "ㄽ", Pair(a: "ㄹ", b: "ㅌ"): "ㄾ",
        Pair(a: "ㄹ", b: "ㅍ"): "ㄿ", Pair(a: "ㄹ", b: "ㅎ"): "ㅀ", Pair(a: "ㅂ", b: "ㅅ"): "ㅄ",
    ]

    static let finalSplit: [Character: Pair] = Dictionary(uniqueKeysWithValues: finalPairs.map { ($0.value, $0.key) })

    static let jamoToKeys: [Character: String] = {
        var map: [Character: String] = [:]
        for (key, jamo) in keyToJamo { map[jamo] = String(key) }
        for (pair, value) in vowelPairs.merging(finalPairs, uniquingKeysWith: { a, _ in a }) {
            map[value] = map[pair.a]! + map[pair.b]!
        }
        return map
    }()

    static func isVowel(_ jamo: Character) -> Bool { jungseong.contains(jamo) }

    static func isUpperLetter(_ c: Character) -> Bool { c.isASCII && c.isUppercase && c.isLetter }

    /// The jamo a key produces, honoring Shift only where Dubeolsik uses it.
    public static func jamo(_ key: Character) -> Character? {
        if let j = keyToJamo[key] { return j }
        if isUpperLetter(key), let j = keyToJamo[Character(key.lowercased())] { return j }
        return nil
    }

    /// Normalizes raw keys to what matters for Hangul: Shift kept only on QWERTOP.
    public static func koreanKeys(_ raw: String) -> String {
        String(raw.map { isUpperLetter($0) && keyToJamo[$0] == nil ? Character($0.lowercased()) : $0 })
    }

    /// Composes raw Latin keystrokes into Hangul. Non-letter characters pass through.
    public static func compose(_ raw: String) -> String {
        var output = ""
        var cho: Character? = nil, jung: Character? = nil, jong: Character? = nil

        func flush() {
            if let c = cho, let v = jung { output.append(syllable(c, v, jong)) }
            else if let c = cho { output.append(c) }
            else if let v = jung { output.append(v) }
            cho = nil; jung = nil; jong = nil
        }

        for key in raw {
            guard let j = jamo(key) else { flush(); output.append(key); continue }
            if !isVowel(j) {
                if jung == nil || cho == nil { flush(); cho = j }
                else if jong == nil {
                    if jongseong.contains(j) { jong = j } else { flush(); cho = j }
                } else if let pair = finalPairs[Pair(a: jong!, b: j)] { jong = pair }
                else { flush(); cho = j }
            } else {
                if let final = jong {
                    let next: Character
                    if let split = finalSplit[final] { jong = split.a; next = split.b }
                    else { next = final; jong = nil }
                    flush()
                    cho = next
                    jung = j
                } else if let current = jung {
                    if let pair = vowelPairs[Pair(a: current, b: j)] { jung = pair } else { flush(); jung = j }
                } else { jung = j }
            }
        }
        flush()
        return output
    }

    static func syllable(_ cho: Character, _ jung: Character, _ jong: Character?) -> Character {
        let c = choseong.firstIndex(of: cho)!, v = jungseong.firstIndex(of: jung)!
        let f = jong.map { jongseong.firstIndex(of: $0)! } ?? 0
        return Character(Unicode.Scalar(0xAC00 + (c * 21 + v) * 28 + f)!)
    }

    /// Converts Hangul text back to the Dubeolsik keys that type it.
    public static func toKeys(_ text: String) -> String? {
        var out = ""
        for c in text {
            if let v = c.unicodeScalars.first?.value, c.unicodeScalars.count == 1, (0xAC00...0xD7A3).contains(v) {
                let index = Int(v) - 0xAC00
                out += jamoToKeys[choseong[index / 588]]!
                out += jamoToKeys[jungseong[index % 588 / 28]]!
                if index % 28 != 0 { out += jamoToKeys[jongseong[index % 28]]! }
            } else if let keys = jamoToKeys[c] { out += keys }
            else { return nil }
        }
        return out
    }

    /// Counts compatibility jamo left unattached to a syllable.
    public static func looseJamo(_ text: String) -> Int {
        text.unicodeScalars.filter { (0x3131...0x3163).contains($0.value) }.count
    }

    static func isSyllable(_ c: Character) -> Bool {
        guard c.unicodeScalars.count == 1, let v = c.unicodeScalars.first?.value else { return false }
        return (0xAC00...0xD7A3).contains(v)
    }

    static func isKoreanScript(_ c: Character) -> Bool {
        guard let v = c.unicodeScalars.first?.value else { return false }
        return v >= 0x3131
    }
}
