import AppKit
import Testing

@testable import PitboardApp

/// The alpha of each pixel of an image drawn at a scale, top row first.
private func alphas(of image: NSImage, scale: Int) throws -> [[UInt8]] {
    let side = Int(image.size.width) * scale
    let rep = try #require(
        NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: side, pixelsHigh: side, bitsPerSample: 8,
            samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
            bytesPerRow: 0, bitsPerPixel: 0))
    rep.size = image.size
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    image.draw(in: NSRect(origin: .zero, size: image.size))
    NSGraphicsContext.restoreGraphicsState()
    return (0..<side).map { y in
        (0..<side).map { x in
            UInt8((rep.colorAt(x: x, y: y)?.alphaComponent ?? 0) * 255 + 0.5)
        }
    }
}

/// The mark is a 16 pt template image, so macOS colours it with the menu bar.
@Test func theMenuBarMarkIsATemplateOf16Points() {
    #expect(MenuBarMark.image.isTemplate)
    #expect(MenuBarMark.image.size == NSSize(width: 16, height: 16))
}

/// At 1x the board is solid with its corners cut, the lit row is cut through it, the dim row
/// shows at its alpha, and the pole hangs from the middle.
@Test func theMenuBarMarkDrawsTheBoardAtOneX() throws {
    let alpha = try alphas(of: MenuBarMark.image, scale: 1)
    let dim = UInt8(MenuBarMark.dimAlpha * 255 + 0.5)
    #expect(alpha[0][7] == 0, "nothing above the board")
    #expect(alpha[2][1] == 0, "the corner is cut")
    #expect(alpha[3][1] == 255, "the board's edge")
    #expect(alpha[4][5] == 0, "the lit row is cut through")
    #expect(alpha[7][5] == dim, "the dim row")
    #expect(alpha[7][11] == 255, "the board past the dim row's end")
    #expect(alpha[13][7] == 255, "the pole")
    #expect(alpha[13][6] == 0, "beside the pole")
}

/// Every pixel is fully on, fully off or the dim row's alpha, at 1x and at 2x: no edge falls
/// between pixels and blurs.
@Test(arguments: [1, 2]) func theMenuBarMarkLandsOnWholePixels(scale: Int) throws {
    let dim = UInt8(MenuBarMark.dimAlpha * 255 + 0.5)
    let values = Set(try alphas(of: MenuBarMark.image, scale: scale).joined())
    #expect(values == [0, dim, 255])
}
