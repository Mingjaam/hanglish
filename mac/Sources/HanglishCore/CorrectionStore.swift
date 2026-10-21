import Foundation

public enum CorrectionReach: String, Codable { case afterSameWord = "AfterSameWord", everywhere = "Everywhere" }

public struct Correction: Codable, Equatable {
    public let raw: String
    public let before: String
    public let preferred: String
    public let reach: CorrectionReach

    enum CodingKeys: String, CodingKey { case raw = "Raw", before = "Before", preferred = "Preferred", reach = "Reach" }
}

/// Remembers the user's flips. A flip applies only after the same preceding word, unless the
/// same choice was made after two different words, which makes it apply everywhere.
/// Mirrors src/Hanglish.Core/CorrectionStore.cs and uses the same JSON layout.
public final class CorrectionStore {
    let url: URL?
    public private(set) var entries: [Correction] = []

    public init(url: URL?) {
        self.url = url
        if let url, let data = try? Data(contentsOf: url) {
            entries = (try? JSONDecoder().decode([Correction].self, from: data)) ?? []
        }
    }

    public var count: Int { entries.count }

    /// - Parameter before: The word right before this one in the same field, or "".
    public func lookup(_ raw: String, before: String) -> String? {
        (entries.last { $0.raw == raw && $0.reach == .everywhere }
            ?? entries.last { $0.raw == raw && $0.before == before })?.preferred
    }

    public func remember(_ raw: String, before: String, preferred: String) {
        entries.removeAll { $0.raw == raw && ($0.before == before || $0.reach == .everywhere) }
        let elsewhere = entries.contains { $0.raw == raw && $0.preferred == preferred }
        if elsewhere { entries.removeAll { $0.raw == raw } }
        entries.append(Correction(raw: raw, before: elsewhere ? "" : before, preferred: preferred,
                                  reach: elsewhere ? .everywhere : .afterSameWord))
        save()
    }

    public func forget(_ raw: String, before: String) {
        let count = entries.count
        entries.removeAll { $0.raw == raw && ($0.before == before || $0.reach == .everywhere) }
        if entries.count != count { save() }
    }

    public func remove(_ entry: Correction) {
        guard let index = entries.firstIndex(of: entry) else { return }
        entries.remove(at: index)
        save()
    }

    public func clear() { entries.removeAll(); save() }

    func save() {
        guard let url else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted]
        try? encoder.encode(entries).write(to: url, options: .atomic)
    }
}
