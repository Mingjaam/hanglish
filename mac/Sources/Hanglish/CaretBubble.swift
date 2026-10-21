import AppKit
import ApplicationServices
import HanglishCore

enum BubbleStyle: String, CaseIterable { case white, dark, minimal }

/// A small one-line pill above the text caret showing what the word being typed will become.
/// Mirrors src/Hanglish/BubbleRenderer.cs; the caret comes from the Accessibility API.
final class CaretBubble {
    static let korean = NSColor(srgbRed: 0.91, green: 0.34, blue: 0.23, alpha: 1)
    static let english = NSColor(srgbRed: 0.09, green: 0.59, blue: 0.83, alpha: 1)

    private let panel: NSPanel
    private let view = PillView()

    init() {
        panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        panel.level = .statusBar
        panel.ignoresMouseEvents = true
        panel.hasShadow = false   // the pill draws its own soft shadow
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.collectionBehavior = [.canJoinAllSpaces, .transient, .ignoresCycle]
        panel.contentView = view
    }

    /// Shows the guess above the caret, or hides the bubble when the caret cannot be found.
    func present(_ guess: Guess?, style: BubbleStyle) {
        guard let guess, let caret = Self.caret() else { panel.orderOut(nil); return }
        view.guess = guess
        view.style = style
        let size = view.fittingSize
        // Accessibility rectangles use a top-left origin on the primary screen; AppKit uses bottom-left.
        let primary = NSScreen.screens.first?.frame.height ?? 0
        let margin = PillView.margin
        var origin = NSPoint(x: caret.minX - margin - 8, y: primary - caret.minY + 2 - margin)
        if let screen = NSScreen.screens.first(where: { $0.frame.contains(NSPoint(x: caret.minX, y: primary - caret.maxY)) }) {
            let visible = screen.visibleFrame
            origin.x = min(max(origin.x, visible.minX - margin), visible.maxX - size.width + margin)
            if origin.y + size.height - margin > visible.maxY { origin.y = primary - caret.maxY - size.height + margin - 4 }
        }
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        view.needsDisplay = true
        panel.orderFrontRegardless()
    }

    func hide() { panel.orderOut(nil) }

    /// Renders a sample for the settings window.
    static func sample(_ guess: Guess, style: BubbleStyle) -> NSImage {
        let view = PillView()
        view.guess = guess
        view.style = style
        let size = view.fittingSize
        view.frame = NSRect(origin: .zero, size: size)
        let image = NSImage(size: size)
        image.lockFocus()
        view.draw(view.bounds)
        image.unlockFocus()
        return image
    }

    /// The caret of the focused text element, in Accessibility (top-left) screen coordinates.
    static func caret() -> CGRect? {
        let system = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(system, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let element = focused else { return nil }
        let field = element as! AXUIElement
        var range: CFTypeRef?
        guard AXUIElementCopyAttributeValue(field, kAXSelectedTextRangeAttribute as CFString, &range) == .success,
              let range else { return nil }
        var bounds: CFTypeRef?
        guard AXUIElementCopyParameterizedAttributeValue(field, kAXBoundsForRangeParameterizedAttribute as CFString,
                                                         range, &bounds) == .success, let bounds else { return nil }
        var rect = CGRect.zero
        guard AXValueGetValue(bounds as! AXValue, .cgRect, &rect), rect.height > 0 else { return nil }
        return rect
    }
}

/// Draws the pill: text, then a tinted language tag (or a dot in the minimal style).
final class PillView: NSView {
    static let margin: CGFloat = 8
    var guess = Guess(text: "", korean: true, alternative: "", confidence: 1)
    var style = BubbleStyle.white

    private var main: [NSAttributedString.Key: Any] {
        [.font: NSFont.systemFont(ofSize: 13, weight: .semibold), .foregroundColor: style == .dark ? NSColor.white : NSColor(white: 0.1, alpha: 1)]
    }
    private var tagAttributes: [NSAttributedString.Key: Any] {
        [.font: NSFont.systemFont(ofSize: 9.5, weight: .bold), .foregroundColor: guess.korean ? CaretBubble.korean : CaretBubble.english]
    }
    private var tag: String { guess.korean ? "한" : "EN" }

    override var fittingSize: NSSize {
        let text = (guess.text as NSString).size(withAttributes: main)
        let tagSize = (tag as NSString).size(withAttributes: tagAttributes)
        let mark = style == .minimal ? 6 : tagSize.width + 9
        return NSSize(width: ceil(text.width + 6 + mark + 20 + Self.margin * 2), height: ceil(text.height + 10 + Self.margin * 2))
    }

    override func draw(_ dirtyRect: NSRect) {
        let card = bounds.insetBy(dx: Self.margin, dy: Self.margin)
        let radius = card.height / 2
        NSGraphicsContext.saveGraphicsState()
        let shadow = NSShadow()
        shadow.shadowColor = NSColor(srgbRed: 0.08, green: 0.09, blue: 0.18, alpha: style == .dark ? 0.35 : 0.18)
        shadow.shadowBlurRadius = 6
        shadow.shadowOffset = NSSize(width: 0, height: -2)
        shadow.set()
        (style == .dark ? NSColor(srgbRed: 0.11, green: 0.11, blue: 0.16, alpha: 1) : NSColor.white).setFill()
        NSBezierPath(roundedRect: card, xRadius: radius, yRadius: radius).fill()
        NSGraphicsContext.restoreGraphicsState()
        (style == .dark ? NSColor(white: 0.25, alpha: 1) : NSColor(srgbRed: 0.89, green: 0.9, blue: 0.94, alpha: 1)).setStroke()
        let outline = NSBezierPath(roundedRect: card.insetBy(dx: 0.5, dy: 0.5), xRadius: radius, yRadius: radius)
        outline.lineWidth = 1
        outline.stroke()

        let accent = guess.korean ? CaretBubble.korean : CaretBubble.english
        let textSize = (guess.text as NSString).size(withAttributes: main)
        var x = card.minX + 10
        let textY = card.midY - textSize.height / 2
        if style == .minimal {
            accent.setFill()
            NSBezierPath(ovalIn: NSRect(x: x, y: card.midY - 3, width: 6, height: 6)).fill()
            x += 12
            (guess.text as NSString).draw(at: NSPoint(x: x, y: textY), withAttributes: main)
            return
        }
        (guess.text as NSString).draw(at: NSPoint(x: x, y: textY), withAttributes: main)
        x += textSize.width + 6
        let tagSize = (tag as NSString).size(withAttributes: tagAttributes)
        let pill = NSRect(x: x, y: card.midY - (tagSize.height + 3) / 2, width: tagSize.width + 9, height: tagSize.height + 3)
        accent.withAlphaComponent(style == .dark ? 0.28 : 0.12).setFill()
        NSBezierPath(roundedRect: pill, xRadius: pill.height / 2, yRadius: pill.height / 2).fill()
        (tag as NSString).draw(at: NSPoint(x: pill.midX - tagSize.width / 2, y: pill.midY - tagSize.height / 2), withAttributes: tagAttributes)
    }
}
