import Foundation

/// Word probabilities over raw keystrokes: a lexicon unigram backed off to an
/// interpolated Witten-Bell character n-gram. Mirrors src/Hanglish.Core/KeyModel.cs;
/// contexts are packed into integers because Swift string keys are too slow to train at launch.
final class KeyModel {
    static let order = 5
    static let lexiconWeight = 0.7
    static let begin: UInt64 = 1, end: UInt64 = 2

    private var lexicon: [String: Double] = [:]
    private var counts: [UInt64: Double] = [:]                     // (context, next) -> count
    private var summaries: [UInt64: (total: Double, types: Int)] = [:]  // context -> totals
    private var symbols: [Character: UInt64] = [:]
    private var alphabet = 1

    func contains(_ word: String) -> Bool { lexicon[word] != nil }

    func frequency(_ word: String) -> Double { lexicon[word] ?? 0 }

    func add(_ keys: String, _ frequency: Double) { lexicon[keys, default: 0] += frequency }

    private func symbol(_ c: Character) -> UInt64 {
        if let s = symbols[c] { return s }
        let s = UInt64(symbols.count + 3)   // 1 and 2 are the word boundaries
        symbols[c] = s
        return s
    }

    /// Context of length n ending before position i, packed 6 bits per symbol plus its length.
    private static func context(_ padded: [UInt64], _ i: Int, _ n: Int) -> UInt64 {
        var packed = UInt64(n)
        for k in (i - n)..<i { packed = packed << 6 | padded[k] }
        return packed
    }

    private func padded(_ keys: String) -> [UInt64] {
        Array(repeating: Self.begin, count: Self.order - 1) + keys.map { symbols[$0] ?? 63 } + [Self.end]
    }

    /// Trains the n-gram on the lexicon, weighting words between type and token counts.
    func train() {
        for word in lexicon.keys { for c in word { _ = symbol(c) } }
        for (keys, frequency) in lexicon {
            let weight = 1 + log10(1 + frequency * 1e7)
            let p = padded(keys)
            for i in (Self.order - 1)..<p.count {
                for n in 0..<Self.order {
                    counts[Self.context(p, i, n) << 6 | p[i], default: 0] += weight
                }
            }
        }
        var unigram = 0
        for (key, count) in counts {
            let context = key >> 6
            summaries[context, default: (0, 0)].total += count
            summaries[context, default: (0, 0)].types += 1
            if context == 0 { unigram += 1 }
        }
        alphabet = unigram + 1
    }

    private func next(_ p: [UInt64], _ i: Int) -> Double {
        var probability = 1.0 / Double(alphabet)
        for n in 0..<Self.order {
            let context = Self.context(p, i, n)
            guard let s = summaries[context] else { break }
            let c = counts[context << 6 | p[i]] ?? 0
            probability = (c + Double(s.types) * probability) / (s.total + Double(s.types))
        }
        return probability
    }

    /// log P(keys as one whole word) under the n-gram alone.
    func ngramLog(_ keys: String) -> Double {
        let p = padded(keys)
        var log = 0.0
        for i in (Self.order - 1)..<p.count { log += Foundation.log(next(p, i)) }
        return log
    }

    /// log P(word): mixture of the lexicon frequency and the n-gram.
    func wordLog(_ keys: String) -> Double {
        Foundation.log(Self.lexiconWeight * (lexicon[keys] ?? 0) + (1 - Self.lexiconWeight) * exp(ngramLog(keys)))
    }
}
