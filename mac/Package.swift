// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Hanglish",
    platforms: [.macOS(.v13)],
    targets: [
        // The lexicon is copied in by scripts/prepare.sh from ../src/Hanglish.Core/Resources.
        .target(name: "HanglishCore", resources: [.copy("Resources/lexicon.tsv")]),
        .executableTarget(name: "Hanglish", dependencies: ["HanglishCore"]),
        .testTarget(name: "HanglishCoreTests", dependencies: ["HanglishCore"],
                    resources: [.copy("Fixtures")]),
    ]
)
