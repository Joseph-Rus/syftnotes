// Draws every Syft Notes icon, logo and favicon from Joey's pixel s ("Pixel Sift": his logo board and
// syft-motion.html), and writes the vector sources beside them.
//
//   xcrun swift tools/syft/make_art.swift                 everything below
//   xcrun swift tools/syft/make_art.swift --iconset DIR    only the Mac app icon's iconset (macos/make_icon.swift)
//   xcrun swift tools/syft/make_art.swift --lab DIR        the small sizes drawn every way they could be, to compare
//
// The mark is 24 rounded cells on a 6 × 7 grid: cell 6.6, corner 1.8, pitch 8, in a tile of 100 units with a 22.2-unit
// corner (both read off his board). Three looks:
//   light  white tile, the sorted s (lilac, sky, coral, an ink bottom row), six loose bits tumbling in above it
//   dark   ink tile, the empty grid behind in mesh-dark, the s with a mustard bottom row, three bits falling out below
//   ink    every cell ink (the menu bar's and the tray's mark, a template image)
// The dark tile is the app icon (Mac, Windows, favicons, the extension); the iPhone's home screen icon is the light
// tile, its dark-mode icon the dark tile. Pictures under 300 px put each cell on whole pixels (see `snap(for:)`), and
// below 28 px the falling bits go: they'd be mud.
//
// Writes (keep each file's name and size; other code finds them by both):
//   assets/syft/*.svg                          the marks, both tiles, the wordmark and both lockups, as vectors
//   assets/icon.png 256, assets/study-stash.ico 16–256, assets/apple-touch-icon.png 180, docs/images/icon.png 128
//   engine/src/StudyStash.App/Assets/icon.png 256, mark-32.png, mark-36.png (the ink s, for the tray and the menu bar)
//   ios/StudyStash/Resources/Assets.xcassets/AppIcon.appiconset/AppIcon-1024(-dark).png, Logo.imageset/Logo(-dark).png
//   web/public/favicon-32.png, icon-192.png, icon-512.png, icon-maskable-192/512.png, apple-touch-icon.png
//   site/assets/favicon.png 64, apple-touch-icon.png 180, img/icon.webp 256 (needs cwebp), social.png 1200 × 630
//   extension/icon-16.png, icon-32.png, icon-48.png, icon-128.png
//
// The wordmark ("syft" in Archivo Black Italic, " notes" in Archivo Regular, letter-spacing −0.02em) is set from the
// fonts when they're found (SYFT_FONTS, else the app's Assets/Fonts) and kept as outlines in assets/syft/wordmark.svg,
// which is read back when they aren't, so nothing here needs the fonts installed.

import AppKit
import CoreText

let here = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
let root = here.appendingPathComponent("../..").standardizedFileURL
func repo(_ path: String) -> URL { root.appendingPathComponent(path) }

// MARK: - The brand

let lilac: UInt32 = 0xCDBDFF, sky: UInt32 = 0xA3DAF2, coral: UInt32 = 0xFF6E4E, mustard: UInt32 = 0xFFC943
let ink: UInt32 = 0x16131D, white: UInt32 = 0xFFFFFF, meshDark: UInt32 = 0x24212D
/// The hairline round the white tile where it sits on a white or paper page (his light lockup).
let tileEdge: UInt32 = 0xE6E4DF

func hex(_ c: UInt32) -> String { String(format: "#%06X", c) }

/// A colour as its sRGB hex, so what's drawn is the hex.
func cg(_ c: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((c >> 16) & 0xff) / 255, green: CGFloat((c >> 8) & 0xff) / 255, blue: CGFloat(c & 0xff) / 255, alpha: a)
}

/// The 24 cells of the s in path order, p0 → p23, as (column, row) on the 6 × 7 grid (syft-motion.html's #s-cells ÷ 8).
let path: [(Int, Int)] = [(5, 1), (5, 0), (4, 0), (3, 0), (2, 0), (1, 0), (0, 1), (1, 1), (0, 2), (1, 2), (1, 3), (2, 3),
                          (3, 3), (4, 3), (4, 4), (5, 4), (4, 5), (5, 5), (4, 6), (3, 6), (2, 6), (1, 6), (0, 6), (0, 5)]
let pitch = 8.0, cellSize = 6.6, cellCorner = 1.8
let gridW = 5 * pitch + cellSize, gridH = 6 * pitch + cellSize   // 46.6 × 54.6, the cells' own extent
let tileCorner = 22.2                                             // of 100: both tiles on his board

/// p0–p7 lilac, p8–p13 sky, p14–p17 and p23 coral, p18–p22 `bottom` (ink on the light tile, mustard on the dark).
func sorted(bottom: UInt32) -> [UInt32] {
    (0..<24).map { i in i < 8 ? lilac : i < 14 ? sky : i < 18 || i == 23 ? coral : bottom }
}

/// A loose bit: its centre in pitches from the centre of the grid's top-left cell (column 0, row 0), its side and its
/// turn (degrees, clockwise), read off his board.
struct Bit { let colour: UInt32; let x, y, size, angle: Double }

/// Six bits tumbling in above the s (the white hero tile), drawn left to right: the lilac one lies over the coral.
let tumbling = [Bit(colour: coral, x: 0.26, y: -1.51, size: 6.6, angle: -21), Bit(colour: lilac, x: 1.02, y: -1.72, size: 6.6, angle: 13),
                Bit(colour: sky, x: 1.52, y: -2.57, size: 6.6, angle: 15), Bit(colour: mustard, x: 2.64, y: -2.07, size: 6.6, angle: -7),
                Bit(colour: lilac, x: 3.76, y: -2.29, size: 6.6, angle: 25), Bit(colour: ink, x: 4.76, y: -1.80, size: 6.6, angle: -31)]
/// Three bits falling out below the s through the sieve (the app icon), smaller as they go.
let falling = [Bit(colour: coral, x: 1.0, y: 7.6, size: 6.6, angle: 20), Bit(colour: sky, x: 2.76, y: 8.35, size: 5.8, angle: -17),
               Bit(colour: lilac, x: 4.14, y: 7.47, size: 5.0, angle: 35)]

/// One look of the mark: its cells' colours, the empty grid behind them (or none), its bits, and where the grid's top-left
/// sits in a 100-unit tile, with the tile's colour.
struct Look {
    var cells: [UInt32]
    var mesh: UInt32?
    var bits: [Bit]
    var origin: CGPoint
    var tile: UInt32?
}

let light = Look(cells: sorted(bottom: ink), mesh: nil, bits: tumbling, origin: CGPoint(x: (100 - gridW) / 2, y: 29.8), tile: white)
let dark = Look(cells: sorted(bottom: mustard), mesh: meshDark, bits: falling, origin: CGPoint(x: (100 - gridW) / 2, y: 14.0), tile: ink)
let inkMark = Look(cells: Array(repeating: ink, count: 24), mesh: nil, bits: [], origin: CGPoint(x: (100 - gridW) / 2, y: (100 - gridH) / 2), tile: nil)

// MARK: - Drawing

/// Small pictures put the grid on whole pixels: `pitch` px from cell to cell, each cell `cell` px square, its corners
/// rounded only when it's big enough to show them; `bits` says whether the loose bits stay.
struct Snap { var pitch: Double; var cell: Double; var bits: Bool }

/// Where the snapped columns (or rows) start, from the grid's first pixel.
func steps(_ n: Int, _ pitch: Double) -> [Double] { (0..<n).map { (Double($0) * pitch + 0.5).rounded(.down) } }

/// A `w` × `h` picture in sRGB, y down (as in the SVGs); `opaque` has no alpha channel at all (an App Store icon mustn't).
func canvas(_ w: Int, _ h: Int, opaque: Bool = false) -> CGContext {
    let ctx = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: (opaque ? CGImageAlphaInfo.noneSkipLast : CGImageAlphaInfo.premultipliedLast).rawValue)!
    ctx.translateBy(x: 0, y: CGFloat(h))
    ctx.scaleBy(x: 1, y: -1)
    ctx.setShouldAntialias(true)
    return ctx
}

func roundedRect(_ r: CGRect, _ radius: CGFloat) -> CGPath {
    let k = max(0, min(radius, min(r.width, r.height) / 2))
    return CGPath(roundedRect: r, cornerWidth: k, cornerHeight: k, transform: nil)
}

func fill(_ ctx: CGContext, _ p: CGPath, _ c: UInt32) {
    ctx.addPath(p)
    ctx.setFillColor(cg(c))
    ctx.fillPath()
}

/// A bit: a rounded square `size` across, turned `angle` degrees clockwise about its centre.
func bitPath(centre: CGPoint, size: CGFloat, corner: CGFloat, angle: Double) -> CGPath {
    var t = CGAffineTransform(translationX: centre.x, y: centre.y).rotated(by: CGFloat(angle * .pi / 180))
    return CGPath(roundedRect: CGRect(x: -size / 2, y: -size / 2, width: size, height: size),
                  cornerWidth: corner, cornerHeight: corner, transform: &t)
}

enum Shape { case rounded, square, none }

/// The mark in `look`, its 100-unit tile filling `rect` (pixels, y down): the tile (`shape`, in the look's colour), the
/// empty grid, the s and its bits. `snap` puts the grid on whole pixels; `scale` shrinks the s and its bits about the
/// block's centre (the maskable icon's safe zone).
func drawTile(_ ctx: CGContext, _ look: Look, in rect: CGRect, shape: Shape, snap: Snap? = nil, scale: CGFloat = 1) {
    let u = rect.width / 100
    if let tile = look.tile, shape != .none {
        fill(ctx, shape == .rounded ? roundedRect(rect, tileCorner * u) : CGPath(rect: rect, transform: nil), tile)
    }
    // The grid's top-left and the size of one unit, after any shrinking about the block's centre.
    var origin = CGPoint(x: rect.minX + look.origin.x * u, y: rect.minY + look.origin.y * u)
    var k = u
    if scale != 1 {
        let c = blockCentre(look)
        let centre = CGPoint(x: rect.minX + c.x * u, y: rect.minY + c.y * u)
        origin = CGPoint(x: centre.x + (origin.x - centre.x) * scale, y: centre.y + (origin.y - centre.y) * scale)
        k = u * scale
    }

    if let snap {
        // Whole pixels: the snapped s (and its bits, if they stay) centred in the tile as one block.
        let p = snap.pitch
        let w = (5 * p + 0.5).rounded(.down) + snap.cell, h = (6 * p + 0.5).rounded(.down) + snap.cell
        func bitAt(_ b: Bit) -> (centre: CGPoint, side: Double) {
            (CGPoint(x: snap.cell / 2 + b.x * p, y: snap.cell / 2 + b.y * p), b.size / pitch * p)
        }
        var block = CGRect(x: 0, y: 0, width: w, height: h)
        if snap.bits {
            for b in look.bits {
                let (c, side) = bitAt(b)
                block = block.union(bitPath(centre: c, size: side, corner: 0, angle: b.angle).boundingBoxOfPath)
            }
        }
        let gx = (rect.midX - block.midX).rounded(), gy = (rect.midY - block.midY).rounded()
        let xs = steps(6, p).map { gx + $0 }, ys = steps(7, p).map { gy + $0 }
        let corner = snap.cell >= 5 ? snap.cell * cellCorner / cellSize : 0
        func cell(_ c: Int, _ r: Int) -> CGPath { roundedRect(CGRect(x: xs[c], y: ys[r], width: snap.cell, height: snap.cell), corner) }
        // The empty grid only where there are gaps for it to show through: with the cells touching it'd be a grey block.
        if let mesh = look.mesh, snap.cell < p {
            for r in 0..<7 { for c in 0..<6 { fill(ctx, cell(c, r), mesh) } }
        }
        for (i, (c, r)) in path.enumerated() { fill(ctx, cell(c, r), look.cells[i]) }
        if snap.bits {
            for b in look.bits {
                let (c, side) = bitAt(b)
                fill(ctx, bitPath(centre: CGPoint(x: gx + c.x, y: gy + c.y), size: side, corner: side * cellCorner / cellSize, angle: b.angle), b.colour)
            }
        }
        return
    }

    func cellRect(_ c: Int, _ r: Int) -> CGRect {
        CGRect(x: origin.x + Double(c) * pitch * k, y: origin.y + Double(r) * pitch * k, width: cellSize * k, height: cellSize * k)
    }
    if let mesh = look.mesh {
        for r in 0..<7 { for c in 0..<6 { fill(ctx, roundedRect(cellRect(c, r), cellCorner * k), mesh) } }
    }
    for (i, (c, r)) in path.enumerated() { fill(ctx, roundedRect(cellRect(c, r), cellCorner * k), look.cells[i]) }
    for b in look.bits {
        let centre = CGPoint(x: origin.x + (cellSize / 2 + b.x * pitch) * k, y: origin.y + (cellSize / 2 + b.y * pitch) * k)
        fill(ctx, bitPath(centre: centre, size: b.size * k, corner: b.size * cellCorner / cellSize * k, angle: b.angle), b.colour)
    }
}

/// The corners of a bit, in units from the grid's top-left.
func bitCorners(_ b: Bit) -> [CGPoint] {
    let cx: Double = cellSize / 2 + b.x * pitch, cy: Double = cellSize / 2 + b.y * pitch
    let a: Double = b.angle * .pi / 180, h: Double = b.size / 2
    let corners: [(Double, Double)] = [(-h, -h), (h, -h), (h, h), (-h, h)]
    return corners.map { (x: Double, y: Double) -> CGPoint in
        let rx: Double = x * cos(a) - y * sin(a), ry: Double = x * sin(a) + y * cos(a)
        return CGPoint(x: cx + rx, y: cy + ry)
    }
}

/// The s and its bits as one block: its extent in units from the grid's top-left.
func blockBounds(_ look: Look) -> CGRect {
    var r = CGRect(x: 0, y: 0, width: gridW, height: gridH)
    for b in look.bits { for p in bitCorners(b) { r = r.union(CGRect(origin: p, size: .zero)) } }
    return r
}

/// The block's centre in the 100-unit tile.
func blockCentre(_ look: Look) -> CGPoint {
    let b = blockBounds(look)
    return CGPoint(x: look.origin.x + b.midX, y: look.origin.y + b.midY)
}

// MARK: - Sizes

/// How a tile is drawn when it fills `px` pixels: on whole pixels below 300 px (cells with crisp edges), as drawn above
/// that. Below 28 px the gaps can't show, so the cells touch and the s is a bold pixel letter, and the bits go.
func snap(for px: Double) -> Snap? {
    switch px {
    case ..<14: return Snap(pitch: 1, cell: 1, bits: false)        // the Mac's 16: a 6 × 7 s in its 12-px tile
    case ..<28: return Snap(pitch: 2, cell: 2, bits: false)        // 16, 20, 24: a 12 × 14 pixel letter
    case ..<44: return Snap(pitch: 3, cell: 2, bits: true)         // 32, 40: 17 × 20, the grid showing
    case ..<56: return Snap(pitch: 4, cell: 3, bits: true)         // 48: 23 × 27
    case ..<80: return Snap(pitch: 5, cell: 4, bits: true)         // 64: 29 × 34
    case ..<300: return Snap(pitch: (px * pitch / 100).rounded(), cell: (px * cellSize / 100).rounded(), bits: true)
    default: return nil
    }
}

// MARK: - Pictures

func png(_ ctx: CGContext) -> Data {
    let data = NSMutableData()
    let dest = CGImageDestinationCreateWithData(data, "public.png" as CFString, 1, nil)!
    CGImageDestinationAddImage(dest, ctx.makeImage()!, nil)
    CGImageDestinationFinalize(dest)
    return data as Data
}

var written: [String] = []

func write(_ data: Data, _ rel: String) {
    let url = repo(rel)
    try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    try! data.write(to: url)
    written.append(rel)
}

/// The dark tile filling a `px` picture, its corners rounded and clear (the Windows / web / extension icon).
func darkTile(_ px: Int) -> CGContext {
    let ctx = canvas(px, px)
    drawTile(ctx, dark, in: CGRect(x: 0, y: 0, width: px, height: px), shape: .rounded, snap: snap(for: Double(px)))
    return ctx
}

/// A look filling a square picture edge to edge, no alpha (iOS and the home screens round the corners themselves).
func square(_ look: Look, _ px: Int) -> CGContext {
    let ctx = canvas(px, px, opaque: true)
    drawTile(ctx, look, in: CGRect(x: 0, y: 0, width: px, height: px), shape: .square, snap: snap(for: Double(px)))
    return ctx
}

/// The Mac app icon at `px`: the dark tile on Apple's grid (an 824-unit tile centred on a 1024 canvas), flat, with no
/// shadow of its own; the tile's edge on whole pixels in the small pictures.
func macIcon(_ px: Int) -> CGContext {
    let ctx = canvas(px, px)
    let side = Double(px)
    var inset = side * 100 / 1024, tile = side * 824 / 1024
    if px <= 64 { inset = inset.rounded(); tile = side - 2 * inset }
    drawTile(ctx, dark, in: CGRect(x: inset, y: inset, width: tile, height: tile), shape: .rounded, snap: snap(for: tile))
    return ctx
}

/// The maskable icon's s: shrunk so the s and its bits stay inside the safe zone (a circle 80% of the picture across)
/// with a little to spare, on ink edge to edge.
func maskableScale() -> CGFloat {
    let c = blockCentre(dark)
    var far = 0.0
    var pts: [CGPoint] = []
    for (col, row) in path {
        let x = dark.origin.x + Double(col) * pitch, y = dark.origin.y + Double(row) * pitch
        pts += [CGPoint(x: x, y: y), CGPoint(x: x + cellSize, y: y), CGPoint(x: x, y: y + cellSize), CGPoint(x: x + cellSize, y: y + cellSize)]
    }
    for b in dark.bits { pts += bitCorners(b).map { CGPoint(x: dark.origin.x + $0.x, y: dark.origin.y + $0.y) } }
    for p in pts { far = max(far, hypot(p.x - c.x, p.y - c.y)) }
    return CGFloat(38 / far)  // the safe zone's radius is 40 of the 100 units
}

/// The dark look shrunk into the maskable safe zone, its block centred in the picture.
func maskable(_ px: Int) -> CGContext {
    let ctx = canvas(px, px, opaque: true)
    let side = CGFloat(px)
    fill(ctx, CGPath(rect: CGRect(x: 0, y: 0, width: side, height: side), transform: nil), ink)
    // Shift the tile so the block's centre lands on the picture's centre, then shrink about it.
    let c = blockCentre(dark)
    var look = dark
    look.origin = CGPoint(x: look.origin.x + 50 - c.x, y: look.origin.y + 50 - c.y)
    look.tile = nil
    drawTile(ctx, look, in: CGRect(x: 0, y: 0, width: side, height: side), shape: .none, scale: maskableScale())
    return ctx
}

/// The ink s alone, black on clear, for a `px` menu bar or tray picture (the app tints it): on whole pixels, filling the
/// picture's height. TrayMark.cs draws the same cells (its Grid table matches this one).
func markGrid(_ px: Int) -> (pitch: Double, cell: Double) {
    switch px {
    case 18: return (2, 2)
    case 32: return (4, 3)
    case 36: return (5, 4)
    default:
        let p = max(1, (Double(px) / 7.2).rounded(.down))
        var c = max(1, (p * 0.8).rounded())
        if c >= p && p > 1 { c = p - 1 }
        return (p, c)
    }
}

func mark(_ px: Int) -> CGContext {
    let ctx = canvas(px, px)
    let (p, c) = markGrid(px)
    let w = (5 * p + 0.5).rounded(.down) + c, h = (6 * p + 0.5).rounded(.down) + c
    let gx = ((Double(px) - w) / 2).rounded(.down), gy = ((Double(px) - h) / 2).rounded(.down)
    let xs = steps(6, p), ys = steps(7, p)
    ctx.setFillColor(cg(0x000000))
    for (col, row) in path { ctx.fill(CGRect(x: gx + xs[col], y: gy + ys[row], width: c, height: c)) }
    return ctx
}

/// A Windows .ico of the dark tile at each size: 256 as PNG, the rest as 32-bit bitmaps, which every Windows tool reads.
func ico(_ sizes: [Int]) -> Data {
    let images: [Data] = sizes.map { px in
        let ctx = darkTile(px)
        if px >= 256 { return png(ctx) }
        var dib = Data()
        func u32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { dib.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { dib.append(contentsOf: $0) } }
        u32(40); u32(UInt32(px)); u32(UInt32(px * 2)); u16(1); u16(32); u32(0); u32(0); u32(0); u32(0); u32(0); u32(0)
        let bytes = ctx.data!.assumingMemoryBound(to: UInt8.self), row = ctx.bytesPerRow
        for y in (0..<px).reversed() {  // bottom-up rows of BGRA, not premultiplied
            for x in 0..<px {
                let i = y * row + x * 4
                let a = bytes[i + 3]
                func un(_ v: UInt8) -> UInt8 { a == 0 ? 0 : UInt8(min(255, (Double(v) * 255 / Double(a)).rounded())) }
                dib.append(contentsOf: [un(bytes[i + 2]), un(bytes[i + 1]), un(bytes[i]), a])
            }
        }
        dib.append(Data(count: ((px + 31) / 32) * 4 * px))  // the AND mask: all clear (the alpha decides)
        return dib
    }
    var d = Data([0, 0, 1, 0, UInt8(sizes.count), 0])
    var offset = 6 + 16 * sizes.count
    for (px, image) in zip(sizes, images) {
        let side = UInt8(px >= 256 ? 0 : px)
        d.append(contentsOf: [side, side, 0, 0, 1, 0, 32, 0])
        withUnsafeBytes(of: UInt32(image.count).littleEndian) { d.append(contentsOf: $0) }
        withUnsafeBytes(of: UInt32(offset).littleEndian) { d.append(contentsOf: $0) }
        offset += image.count
    }
    for image in images { d.append(image) }
    return d
}

/// A .webp through cwebp (lossless), which macOS can't write itself.
func webp(_ ctx: CGContext, _ rel: String) {
    let cwebp = ["/opt/homebrew/bin/cwebp", "/usr/local/bin/cwebp"].first { FileManager.default.isExecutableFile(atPath: $0) }
    guard let cwebp else { print("skipped \(rel): no cwebp (brew install webp)"); return }
    let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("syft-\(UUID().uuidString).png")
    try! png(ctx).write(to: tmp)
    let p = Process()
    p.executableURL = URL(fileURLWithPath: cwebp)
    p.arguments = ["-quiet", "-lossless", "-exact", "-z", "9", tmp.path, "-o", repo(rel).path]
    try! p.run()
    p.waitUntilExit()
    try? FileManager.default.removeItem(at: tmp)
    if p.terminationStatus == 0 { written.append(rel) } else { print("cwebp failed for \(rel)") }
}

// MARK: - The wordmark

/// The wordmark's outlines as SVG path data (absolute M/L/Q/C/Z, y down) set at 100 units, and their extent.
struct Wordmark { var d: String; var bounds: CGRect }

/// The fonts: SYFT_FONTS, else the app's own copies.
func fontsDir() -> URL? {
    var dirs: [URL] = []
    if let env = ProcessInfo.processInfo.environment["SYFT_FONTS"] { dirs.append(URL(fileURLWithPath: env)) }
    dirs.append(repo("engine/src/StudyStash.App/Assets/Fonts"))
    return dirs.first { FileManager.default.fileExists(atPath: $0.appendingPathComponent("Archivo-BlackItalic.ttf").path) }
}

func font(_ url: URL, size: CGFloat) -> CTFont {
    let descs = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as! [CTFontDescriptor]
    return CTFontCreateWithFontDescriptor(descs[0], size, nil)
}

/// A number for an SVG: two decimals at most, no trailing zeros.
func n(_ v: CGFloat) -> String {
    var t = String(format: "%.2f", Double(v))
    while t.contains(".") && (t.hasSuffix("0") || t.hasSuffix(".")) { t.removeLast() }
    return t == "-0" ? "0" : t
}

/// "syft notes" set at 100 units: "syft" Black Italic, " notes" Regular, every letter 0.02em closer (as CSS's
/// letter-spacing: −0.02em), the ink's left edge at x 0 and the baseline at y 0.
func setWordmark(_ dir: URL) -> Wordmark {
    let size: CGFloat = 100
    let black = font(dir.appendingPathComponent("Archivo-BlackItalic.ttf"), size: size)
    let regular = font(dir.appendingPathComponent("Archivo-Regular.ttf"), size: size)
    let text = NSMutableAttributedString()
    text.append(NSAttributedString(string: "syft", attributes: [.init(kCTFontAttributeName as String): black, .init(kCTKernAttributeName as String): -0.02 * size]))
    text.append(NSAttributedString(string: " notes", attributes: [.init(kCTFontAttributeName as String): regular, .init(kCTKernAttributeName as String): -0.02 * size]))
    let line = CTLineCreateWithAttributedString(text)
    let all = CGMutablePath()
    for run in CTLineGetGlyphRuns(line) as! [CTRun] {
        let attrs = CTRunGetAttributes(run) as NSDictionary
        let f = attrs[kCTFontAttributeName as String] as! CTFont
        let count = CTRunGetGlyphCount(run)
        var glyphs = [CGGlyph](repeating: 0, count: count), positions = [CGPoint](repeating: .zero, count: count)
        CTRunGetGlyphs(run, CFRange(location: 0, length: count), &glyphs)
        CTRunGetPositions(run, CFRange(location: 0, length: count), &positions)
        for (g, p) in zip(glyphs, positions) {
            guard let gp = CTFontCreatePathForGlyph(f, g, nil) else { continue }
            // Glyphs are y up from the baseline: flip to y down.
            all.addPath(gp, transform: CGAffineTransform(translationX: p.x, y: p.y).scaledBy(x: 1, y: -1))
        }
    }
    let b = all.boundingBoxOfPath
    var shift = CGAffineTransform(translationX: -b.minX, y: 0)
    let placed = all.copy(using: &shift)!
    var d = ""
    placed.applyWithBlock { el in
        let e = el.pointee, p = e.points
        switch e.type {
        case .moveToPoint: d += "M\(n(p[0].x)) \(n(p[0].y))"
        case .addLineToPoint: d += "L\(n(p[0].x)) \(n(p[0].y))"
        case .addQuadCurveToPoint: d += "Q\(n(p[0].x)) \(n(p[0].y)) \(n(p[1].x)) \(n(p[1].y))"
        case .addCurveToPoint: d += "C\(n(p[0].x)) \(n(p[0].y)) \(n(p[1].x)) \(n(p[1].y)) \(n(p[2].x)) \(n(p[2].y))"
        case .closeSubpath: d += "Z"
        @unknown default: break
        }
    }
    return Wordmark(d: d, bounds: placed.boundingBoxOfPath)
}

/// Path data as written above, back into a CGPath.
func parse(_ d: String) -> CGPath {
    let p = CGMutablePath()
    var nums: [CGFloat] = [], cmd: Character = " "
    func flush() {
        switch cmd {
        case "M": p.move(to: CGPoint(x: nums[0], y: nums[1]))
        case "L": p.addLine(to: CGPoint(x: nums[0], y: nums[1]))
        case "Q": p.addQuadCurve(to: CGPoint(x: nums[2], y: nums[3]), control: CGPoint(x: nums[0], y: nums[1]))
        case "C": p.addCurve(to: CGPoint(x: nums[4], y: nums[5]), control1: CGPoint(x: nums[0], y: nums[1]), control2: CGPoint(x: nums[2], y: nums[3]))
        case "Z": p.closeSubpath()
        default: break
        }
        nums = []
    }
    var token = ""
    for ch in d + "E" {
        if ch.isLetter || ch == " " {
            if !token.isEmpty { nums.append(CGFloat(Double(token)!)); token = "" }
            if ch.isLetter { if cmd != " " { flush() }; cmd = ch }
        } else if ch == "-" && !token.isEmpty {
            nums.append(CGFloat(Double(token)!)); token = "-"
        } else {
            token.append(ch)
        }
    }
    return p
}

let wordmarkFile = "assets/syft/wordmark.svg"

/// The wordmark: set from the fonts and written to assets/syft/wordmark.svg, or read back from it.
func wordmark() -> Wordmark {
    if let dir = fontsDir() {
        let w = setWordmark(dir)
        let b = w.bounds
        let svg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="\(n(b.minX)) \(n(b.minY)) \(n(b.width)) \(n(b.height))" width="\(n(b.width))" height="\(n(b.height))">
        <!-- Syft Notes' wordmark: "syft" Archivo Black Italic, " notes" Archivo Regular, letter-spacing -0.02em, set at 100 units
             with the baseline at y 0 (tools/syft/make_art.swift writes this file from the fonts; don't edit it). -->
        <path fill="\(hex(ink))" d="\(w.d)"/>
        </svg>

        """
        write(svg.data(using: .utf8)!, wordmarkFile)
        return w
    }
    let text = try! String(contentsOf: repo(wordmarkFile), encoding: .utf8)
    let d = text.components(separatedBy: " d=\"")[1].components(separatedBy: "\"")[0]
    return Wordmark(d: d, bounds: parse(d).boundingBoxOfPath)
}

// MARK: - Lockups

/// The lockup on his board, in units of the 100-unit tile: the tile, then the wordmark (font size 48.7, x-height 25.6)
/// starting 23.5 units after it, its baseline 65.7 down, so its lower-case letters sit level with the tile's middle.
let wordSize = 48.7, wordGap = 23.5, wordBaseline = 65.7

func lockupWidth(_ w: Wordmark) -> Double { 100 + wordGap + w.bounds.width * wordSize / 100 }

/// The dark lockup with its (unseen) tile at `origin` (pixels), `u` pixels to the unit: the night mark, which needs no
/// tile of its own on ink, and the wordmark in white.
func drawDarkLockup(_ ctx: CGContext, at origin: CGPoint, u: CGFloat, word: Wordmark) {
    drawTile(ctx, dark, in: CGRect(x: origin.x, y: origin.y, width: 100 * u, height: 100 * u), shape: .none)
    var t = CGAffineTransform(translationX: origin.x + (100 + wordGap) * u, y: origin.y + wordBaseline * u).scaledBy(x: wordSize / 100 * u, y: wordSize / 100 * u)
    ctx.addPath(parse(word.d).copy(using: &t)!)
    ctx.setFillColor(cg(white))
    ctx.fillPath()
}

/// The link preview (1200 × 630): the dark lockup on ink, as on his board.
func social(_ word: Wordmark) -> CGContext {
    let w = 1200, h = 630
    let ctx = canvas(w, h, opaque: true)
    fill(ctx, CGPath(rect: CGRect(x: 0, y: 0, width: w, height: h), transform: nil), ink)
    let u: CGFloat = 2.3
    let lw = lockupWidth(word) * u
    // The lockup's box (the tile's 100 units tall, the letters' end) centred.
    drawDarkLockup(ctx, at: CGPoint(x: ((CGFloat(w) - lw) / 2).rounded(), y: ((CGFloat(h) - 100 * u) / 2).rounded()), u: u, word: word)
    return ctx
}

// MARK: - SVG sources

func svgCells(_ look: Look, dx: Double, dy: Double, indent: String = "  ") -> String {
    var s = ""
    func rect(_ x: Double, _ y: Double, _ c: UInt32) -> String {
        "\(indent)<rect x=\"\(n(x))\" y=\"\(n(y))\" width=\"6.6\" height=\"6.6\" rx=\"1.8\" fill=\"\(hex(c))\"/>\n"
    }
    if let mesh = look.mesh {
        s += "\(indent)<!-- the empty grid -->\n"
        for r in 0..<7 { for c in 0..<6 { s += rect(dx + Double(c) * pitch, dy + Double(r) * pitch, mesh) } }
    }
    s += "\(indent)<!-- the s, p0 → p23 -->\n"
    for (i, (c, r)) in path.enumerated() { s += rect(dx + Double(c) * pitch, dy + Double(r) * pitch, look.cells[i]) }
    if !look.bits.isEmpty { s += "\(indent)<!-- the loose bits -->\n" }
    for b in look.bits {
        let cx = dx + cellSize / 2 + b.x * pitch, cy = dy + cellSize / 2 + b.y * pitch
        s += "\(indent)<rect x=\"\(n(cx - b.size / 2))\" y=\"\(n(cy - b.size / 2))\" width=\"\(n(b.size))\" height=\"\(n(b.size))\" rx=\"\(n(b.size * cellCorner / cellSize))\" fill=\"\(hex(b.colour))\" transform=\"rotate(\(n(b.angle)) \(n(cx)) \(n(cy)))\"/>\n"
    }
    return s
}

func svgDoc(_ vb: CGRect, _ note: String, _ body: String) -> String {
    let w = vb.width, h = vb.height
    return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"\(n(vb.minX)) \(n(vb.minY)) \(n(w)) \(n(h))\" width=\"\(n(w * 4))\" height=\"\(n(h * 4))\">\n"
        + "  <!-- \(note) (tools/syft/make_art.swift writes this file; don't edit it) -->\n" + body + "</svg>\n"
}

func writeSVGs(_ word: Wordmark) {
    // The marks alone, the viewBox round the s and its bits.
    for (name, look, note) in [("mark-light", light, "Syft Notes' mark, light: the sorted s, six bits tumbling in above (for white or paper)"),
                               ("mark-dark", dark, "Syft Notes' mark, dark: the empty grid, the s with its mustard row, three bits falling out (for ink)"),
                               ("mark-ink", inkMark, "Syft Notes' mark in one ink (menu bar, tray, a stamp)")] {
        let b = blockBounds(look).insetBy(dx: -0.5, dy: -0.5)
        write(svgDoc(b, note, svgCells(look, dx: 0, dy: 0)).data(using: .utf8)!, "assets/syft/\(name).svg")
    }
    // The tiles: the app icons' artwork, 100 units with the 22.2 corner.
    for (name, look, note) in [("tile-light", light, "Syft Notes' white tile (the iPhone's icon)"),
                               ("tile-dark", dark, "Syft Notes' dark tile (the app icon)")] {
        let body = "  <rect width=\"100\" height=\"100\" rx=\"\(n(tileCorner))\" fill=\"\(hex(look.tile!))\"/>\n"
            + svgCells(look, dx: look.origin.x, dy: look.origin.y)
        write(svgDoc(CGRect(x: 0, y: 0, width: 100, height: 100), note, body).data(using: .utf8)!, "assets/syft/\(name).svg")
    }
    // The lockups.
    let lw = lockupWidth(word), k = wordSize / 100
    let wordAt = "translate(\(n(100 + wordGap)) \(n(wordBaseline))) scale(\(n(k)))"
    let lightBody = "  <rect x=\"0.25\" y=\"0.25\" width=\"99.5\" height=\"99.5\" rx=\"\(n(tileCorner - 0.25))\" fill=\"#FFFFFF\" stroke=\"\(hex(tileEdge))\" stroke-width=\"0.5\"/>\n"
        + svgCells(light, dx: light.origin.x, dy: light.origin.y)
        + "  <path transform=\"\(wordAt)\" fill=\"\(hex(ink))\" d=\"\(word.d)\"/>\n"
    write(svgDoc(CGRect(x: 0, y: 0, width: lw, height: 100), "Syft Notes' lockup, light: the white tile and the wordmark in ink", lightBody).data(using: .utf8)!, "assets/syft/lockup-light.svg")
    let darkBody = "  <rect x=\"-12\" y=\"-12\" width=\"\(n(lw + 24))\" height=\"124\" fill=\"\(hex(ink))\"/>\n"
        + svgCells(dark, dx: dark.origin.x, dy: dark.origin.y)
        + "  <path transform=\"\(wordAt)\" fill=\"#FFFFFF\" d=\"\(word.d)\"/>\n"
    write(svgDoc(CGRect(x: -12, y: -12, width: lw + 24, height: 124), "Syft Notes' lockup, dark: the night mark and the wordmark in white, on ink", darkBody).data(using: .utf8)!, "assets/syft/lockup-dark.svg")
}

// MARK: - Every file

/// The Mac app icon: every size iconutil wants.
func iconset(_ dir: URL) {
    try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    for (name, px) in [("16x16", 16), ("16x16@2x", 32), ("32x32", 32), ("32x32@2x", 64), ("128x128", 128), ("128x128@2x", 256),
                       ("256x256", 256), ("256x256@2x", 512), ("512x512", 512), ("512x512@2x", 1024)] {
        try! png(macIcon(px)).write(to: dir.appendingPathComponent("icon_\(name).png"))
    }
}

func everything() {
    let word = wordmark()
    writeSVGs(word)

    // The app icon (dark tile) wherever it's a picture of its own.
    write(png(darkTile(256)), "assets/icon.png")
    write(png(darkTile(256)), "engine/src/StudyStash.App/Assets/icon.png")
    write(png(darkTile(128)), "docs/images/icon.png")
    write(ico([16, 20, 24, 32, 40, 48, 64, 128, 256]), "assets/study-stash.ico")
    // Home-screen icons: edge to edge, the phone rounds them.
    for rel in ["assets/apple-touch-icon.png", "web/public/apple-touch-icon.png", "site/assets/apple-touch-icon.png"] {
        write(png(square(dark, 180)), rel)
    }

    // The menu bar's and the tray's ink s.
    write(png(mark(32)), "engine/src/StudyStash.App/Assets/mark-32.png")
    write(png(mark(36)), "engine/src/StudyStash.App/Assets/mark-36.png")

    // The iPhone: the white tile on the home screen, the dark one in dark mode; the logo in the app the same way.
    let xc = "ios/StudyStash/Resources/Assets.xcassets"
    write(png(square(light, 1024)), "\(xc)/AppIcon.appiconset/AppIcon-1024.png")
    write(png(square(dark, 1024)), "\(xc)/AppIcon.appiconset/AppIcon-1024-dark.png")
    write(png(square(light, 512)), "\(xc)/Logo.imageset/Logo.png")
    write(png(square(dark, 512)), "\(xc)/Logo.imageset/Logo-dark.png")
    write("""
    {
      "images" : [
        { "filename" : "Logo.png", "idiom" : "universal" },
        { "appearances" : [ { "appearance" : "luminosity", "value" : "dark" } ], "filename" : "Logo-dark.png", "idiom" : "universal" }
      ],
      "info" : { "author" : "xcode", "version" : 1 },
      "properties" : { "template-rendering-intent" : "original" }
    }

    """.data(using: .utf8)!, "\(xc)/Logo.imageset/Contents.json")

    // The phone web app.
    write(png(darkTile(32)), "web/public/favicon-32.png")
    write(png(darkTile(192)), "web/public/icon-192.png")
    write(png(darkTile(512)), "web/public/icon-512.png")
    write(png(maskable(192)), "web/public/icon-maskable-192.png")
    write(png(maskable(512)), "web/public/icon-maskable-512.png")

    // The website.
    write(png(darkTile(64)), "site/assets/favicon.png")
    webp(darkTile(256), "site/assets/img/icon.webp")
    write(png(social(word)), "site/assets/social.png")

    // The Canvas extension: the toolbar's tiles, and the store's 128 with the artwork at 96 inside Chrome's 16-px margin.
    for px in [16, 32, 48] { write(png(darkTile(px)), "extension/icon-\(px).png") }
    let store = canvas(128, 128)
    drawTile(store, dark, in: CGRect(x: 16, y: 16, width: 96, height: 96), shape: .rounded, snap: snap(for: 96))
    write(png(store), "extension/icon-128.png")

    for rel in written { print("wrote", rel) }
}

/// The small sizes drawn every way they could be, for choosing a `Snap`.
func lab(_ dir: URL) {
    try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    let options: [(String, Snap?)] = [("vector", nil), ("p1c1", Snap(pitch: 1, cell: 1, bits: false)), ("p2c1", Snap(pitch: 2, cell: 1, bits: false)),
                                      ("p2c2", Snap(pitch: 2, cell: 2, bits: false)),
                                      ("p3c2", Snap(pitch: 3, cell: 2, bits: false)), ("p3c2b", Snap(pitch: 3, cell: 2, bits: true)),
                                      ("p4c3", Snap(pitch: 4, cell: 3, bits: true)), ("p5c4", Snap(pitch: 5, cell: 4, bits: true))]
    for px in [16, 20, 24, 32, 40, 48, 64] {
        for (name, snap) in options {
            let ctx = canvas(px, px)
            drawTile(ctx, dark, in: CGRect(x: 0, y: 0, width: px, height: px), shape: .rounded, snap: snap)
            try! png(ctx).write(to: dir.appendingPathComponent("dark-\(px)-\(name).png"))
        }
    }
    for px in [16, 32, 64] { try! png(macIcon(px)).write(to: dir.appendingPathComponent("mac-\(px).png")) }
}

let args = CommandLine.arguments
if args.count >= 3 && args[1] == "--iconset" {
    iconset(URL(fileURLWithPath: args[2]))
} else if args.count >= 3 && args[1] == "--lab" {
    lab(URL(fileURLWithPath: args[2]))
} else {
    everything()
}
