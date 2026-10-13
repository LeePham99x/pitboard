import AppKit

/// Pitboard's mark, a pit board held up with one row lit, drawn for the menu bar: every edge
/// on a whole point so it stays sharp at 1x, and a template image so macOS colours it.
enum MenuBarMark {
    static let size = NSSize(width: 16, height: 16)
    static let dimAlpha: CGFloat = 0.35

    /// The board, its corners cut a point in, and the pole it is held by.
    private static let board = [
        NSRect(x: 2, y: 2, width: 12, height: 9),
        NSRect(x: 1, y: 3, width: 14, height: 7),
        NSRect(x: 7, y: 11, width: 2, height: 4),
    ]
    private static let litRow = NSRect(x: 3, y: 4, width: 10, height: 2)
    private static let dimRow = NSRect(x: 3, y: 7, width: 7, height: 1)

    static let image: NSImage = {
        let image = NSImage(size: size, flipped: true) { _ in
            NSColor.black.setFill()
            for part in board { part.fill() }
            litRow.fill(using: .clear)
            NSColor.black.withAlphaComponent(dimAlpha).setFill()
            dimRow.fill(using: .copy)
            return true
        }
        image.isTemplate = true
        return image
    }()
}
