// Draws Egghead Notes' icons, all from the one mark: Eli's egghead (his glasses, bow tie and egg, in his own pen
// strokes: assets/egghead/mark.json, which tools/egghead/make_art.py writes from his drawing).
//   swift make_icon.swift out.iconset         the Mac app icon (light), every size iconutil wants
//   swift make_icon.swift out.png             one 1024-px Mac app icon (light); out-dark.png draws the dark one
//   swift make_icon.swift out.tiles           icon-<px>.png: the Windows / web tile (white, the egghead on it), no margin
//   swift make_icon.swift out.ico             the Windows icon: the tile at 16–256 px in one .ico
//   swift make_icon.swift out.marks           mark-<px>.png: the monochrome egghead for the menu bar and the tray
//   swift make_icon.swift out.ios             AppIcon-1024.png and AppIcon-1024-dark.png for the iPhone/iPad app:
//                                             the tile filling a square picture (iOS rounds the corners itself)
//   swift make_icon.swift out.chrome          icon-<px>.png for the Chrome extension (copy them into extension/):
//                                             16, 32 and 48 the egg and its glasses, bold, on a flat white tile (the
//                                             toolbar, the extensions page), 128 the tile at 96 px with Chrome's 16-px margin
// Light: a flat white squircle, the egg in eggshell, Eli's lines in near-black ink, and four loose pen strokes under the
// egg for its shadow. Dark: a flat near-black squircle, the egg dim and the lines chalk white, so his glasses still show
// where they reach past the egg. Below 128 px the egg keeps only its glasses (the face turns to mud), below 48 the
// shadow goes too, and the lines get bolder as the picture gets smaller. On Apple's macOS grid: an 824-pt rounded square on a 1024 canvas.
// The icons in assets/ and the app's Assets/ are made with this script.

import AppKit

let out = URL(fileURLWithPath: CommandLine.arguments[1])

/// A colour as its sRGB hex, so what's drawn is the hex (CGColor(red:…) would be generic RGB, and come out lighter).
func color(_ hex: UInt32, _ a: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xff) / 255, green: CGFloat((hex >> 8) & 0xff) / 255,
            blue: CGFloat(hex & 0xff) / 255, alpha: a)
}

func gradient(_ colors: [CGColor], _ stops: [CGFloat]? = nil) -> CGGradient {
    let at = stops ?? colors.indices.map { CGFloat($0) / CGFloat(max(1, colors.count - 1)) }
    return CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors as CFArray, locations: at)!
}

func rounded(_ r: CGRect, _ radius: CGFloat) -> CGPath {
    CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil)
}

let pen: UInt32 = 0x1d1d1f, shell: UInt32 = 0xf3ead9, chalk: UInt32 = 0xf1eadb, darkShell: UInt32 = 0x3a3a3e

// ---- the egghead: his strokes in GoodNotes page points (y down), fitted into 1024 design units below ----

/// One pen stroke: its centre line and its nib width, in his page points.
struct Ink { let w: CGFloat; let pts: [CGPoint] }

/// The menu bar's clean lines (see `mark`): the egg's outline, the two lenses' boxes and the eyes, in his page points.
struct MenuBar { let egg: [CGPoint]; let lenses: [CGRect]; let eyes: [CGPoint] }

struct Egghead { let egg: [CGPoint]; let full: [Ink]; let small: [Ink]; let shadow: [Ink]; let menubar: MenuBar }

let egghead: Egghead = {
    let here = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
    let file = here.appendingPathComponent("../assets/egghead/mark.json").standardizedFileURL
    let json = try! JSONSerialization.jsonObject(with: Data(contentsOf: file)) as! [String: Any]
    func points(_ a: Any) -> [CGPoint] { (a as! [[Double]]).map { CGPoint(x: $0[0], y: $0[1]) } }
    func inks(_ a: Any) -> [Ink] { (a as! [[String: Any]]).map { Ink(w: CGFloat($0["w"] as! Double), pts: points($0["pts"]!)) } }
    let bar = json["menubar"] as! [String: Any]
    let lenses = (bar["lenses"] as! [[Double]]).map { CGRect(x: $0[0], y: $0[1], width: $0[2] - $0[0], height: $0[3] - $0[1]) }
    return Egghead(egg: points(json["egg"]!), full: inks(json["full"]!), small: inks(json["small"]!), shadow: inks(json["shadow"]!),
                   menubar: MenuBar(egg: points(bar["egg"]!), lenses: lenses, eyes: points(bar["eyes"]!)))
}()

/// Where the head goes: `inks`' bounds fitted to `side` design units, centred on `centre` (design units, y down),
/// then flipped into CoreGraphics' y-up on the 1024 canvas. The scale comes back too, for the nib widths.
func fit(_ inks: [Ink], side: CGFloat, centre: CGPoint) -> (CGAffineTransform, CGFloat) {
    let all = inks.flatMap(\.pts)
    let minX = all.map(\.x).min()!, maxX = all.map(\.x).max()!, minY = all.map(\.y).min()!, maxY = all.map(\.y).max()!
    let scale = side / max(maxX - minX, maxY - minY)
    let t = CGAffineTransform(translationX: -(minX + maxX) / 2, y: -(minY + maxY) / 2)
        .concatenating(CGAffineTransform(scaleX: scale, y: -scale))
        .concatenating(CGAffineTransform(translationX: centre.x, y: 1024 - centre.y))
    return (t, scale)
}

func eggPath(_ t: CGAffineTransform) -> CGPath {
    let p = CGMutablePath()
    p.addLines(between: egghead.egg, transform: t)
    p.closeSubpath()
    return p
}

/// His lines, each at its own nib width times `weight`.
func strokeInks(_ ctx: CGContext, _ inks: [Ink], _ t: CGAffineTransform, scale: CGFloat, weight: CGFloat, _ ink: CGColor) {
    ctx.saveGState()
    ctx.setLineCap(.round)
    ctx.setLineJoin(.round)
    ctx.setStrokeColor(ink)
    for s in inks where s.pts.count > 1 {
        let p = CGMutablePath()
        p.addLines(between: s.pts, transform: t)
        ctx.addPath(p)
        ctx.setLineWidth(max(0.6, s.w) * scale * weight)
        ctx.strokePath()
    }
    ctx.restoreGState()
}

/// The egghead on a tile: the egg filled in `egg`, his lines in `ink`, and (from 48 px up) the pen strokes of its
/// shadow under it, the whole drawing centred. Small pictures draw only the egg and its glasses, bolder.
func drawHead(_ ctx: CGContext, px: Int, ink: CGColor, egg: CGColor, side: CGFloat? = nil) {
    let small = px < 128
    let inks = small ? egghead.small : egghead.full
    let shadow = px >= 48 ? egghead.shadow : []
    let (t, scale) = fit(inks + shadow, side: side ?? (small ? 720 : 700), centre: CGPoint(x: 512, y: 512))
    ctx.addPath(eggPath(t))
    ctx.setFillColor(egg)
    ctx.fillPath()
    let weight: CGFloat = px <= 20 ? 6.5 : px <= 32 ? 6 : px < 128 ? 5 : 5.4
    strokeInks(ctx, inks + shadow, t, scale: scale, weight: weight, ink)
}

// ---- drawing helpers ----

/// A clear `px`-pixel picture in sRGB, so every colour lands as its hex.
func canvas(_ px: Int) -> CGContext {
    CGContext(data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
}

/// The iPhone/iPad icon: the 824-unit tile fills the whole picture, square, for iOS to round.
var squareForIOS = false

/// Device pixels per design unit for the picture being drawn: CoreGraphics takes shadow offsets and blurs in device
/// pixels, not in the scaled units everything else is drawn in.
var unit: CGFloat = 1

/// A shadow given in design units.
func shadow(_ ctx: CGContext, dx: CGFloat, dy: CGFloat, blur: CGFloat, _ c: CGColor) {
    ctx.setShadow(offset: CGSize(width: dx * unit, height: dy * unit), blur: blur * unit, color: c)
}

enum Look { case light, dark, tile }

/// One picture of the icon at `px` pixels, as PNG.
func icon(_ px: Int, _ look: Look) -> Data { iconRep(px, look).representation(using: .png, properties: [:])! }

/// One picture of the icon at `px` pixels. `.light`/`.dark` are the Mac app icon on Apple's grid (margin and shadow
/// included); `.tile` is the Windows / web tile filling the picture.
func iconRep(_ px: Int, _ look: Look) -> NSBitmapImageRep {
    let ctx = canvas(px)
    let tileRect = CGRect(x: 100, y: 100, width: 824, height: 824)
    var radius: CGFloat = 185
    if squareForIOS {
        let scale = CGFloat(px) / 824
        ctx.translateBy(x: -100 * scale, y: -100 * scale)
        ctx.scaleBy(x: scale, y: scale)
        radius = 0
        unit = scale
    } else if look == .tile {  // the 824-unit tile fills 96% of the picture, with Windows 11's softer corner
        let scale = 0.96 * CGFloat(px) / 824
        ctx.translateBy(x: 0.02 * CGFloat(px) - 100 * scale, y: 0.02 * CGFloat(px) - 100 * scale)
        ctx.scaleBy(x: scale, y: scale)
        radius = 170
        unit = scale
    } else {
        ctx.scaleBy(x: CGFloat(px) / 1024, y: CGFloat(px) / 1024)
        unit = CGFloat(px) / 1024
    }
    let tile = rounded(tileRect, radius)

    if look == .dark {
        // A glossy near-black tile on its shadow, a soft sheen across the top.
        ctx.saveGState()
        shadow(ctx, dx: 0, dy: -12, blur: 30, color(0x000000, 0.4))
        ctx.addPath(tile)
        ctx.setFillColor(color(0x161618))
        ctx.fillPath()
        ctx.restoreGState()
        ctx.saveGState()
        ctx.addPath(tile)
        ctx.clip()
        ctx.setFillColor(color(0x1d1d1f))
        ctx.fill(tileRect)
        ctx.restoreGState()

        // The egg dim, his lines chalk white: the glasses still show where they reach past the egg.
        drawHead(ctx, px: px, ink: color(chalk), egg: color(darkShell))
    } else {
        // A flat white tile (on its shadow on the Mac).
        ctx.saveGState()
        if look == .light { shadow(ctx, dx: 0, dy: -12, blur: 30, color(0x000000, 0.28)) }
        ctx.addPath(tile)
        ctx.setFillColor(color(0xffffff))
        ctx.fillPath()
        ctx.restoreGState()

        drawHead(ctx, px: px, ink: color(pen), egg: color(shell))

        // The fine edge along the tile, as on Apple's icons.
        ctx.addPath(rounded(tileRect.insetBy(dx: 1.5, dy: 1.5), radius - 1))
        ctx.setLineWidth(3)
        ctx.setStrokeColor(color(0x000000, 0.08))
        ctx.strokePath()
    }

    return NSBitmapImageRep(cgImage: ctx.makeImage()!)
}

/// The monochrome egghead for the menu bar (a template image: black on clear, the menu bar tints it) and the Windows
/// tray (the app tints it white or black): one clean line each, taken from his drawing (mark.json's "menubar": the
/// egg's smoothed outline, his two lenses with the bridge between them, his eyes), at the system symbols' weight of
/// 1.5 pt. His sketchy double passes blur to grey at this size. It fills most of a `px`-pixel square, room at its top
/// right for the recording badge.
func mark(_ px: Int) -> Data {
    let ctx = canvas(px)
    let m = egghead.menubar
    // His page points → this picture's pixels (y up): the egg and lenses together fill 90% of the square's height.
    let all = m.egg + m.lenses.flatMap { [CGPoint(x: $0.minX, y: $0.minY), CGPoint(x: $0.maxX, y: $0.maxY)] }
    let minX = all.map(\.x).min()!, maxX = all.map(\.x).max()!, minY = all.map(\.y).min()!, maxY = all.map(\.y).max()!
    let side = CGFloat(px)
    let scale = 0.9 * side / max(maxX - minX, maxY - minY)
    let t = CGAffineTransform(translationX: -(minX + maxX) / 2, y: -(minY + maxY) / 2)
        .concatenating(CGAffineTransform(scaleX: scale, y: -scale))
        .concatenating(CGAffineTransform(translationX: side / 2 - side * 0.03, y: side / 2))
    let line = side / 12   // 1.5 pt: 3 px in the 36-px (18-pt @2x) picture, 1.5 px at 18
    ctx.setStrokeColor(color(0x000000))
    ctx.setFillColor(color(0x000000))
    ctx.setLineWidth(line)
    ctx.setLineJoin(.round)
    ctx.setLineCap(.round)
    let egg = CGMutablePath()
    egg.addLines(between: m.egg, transform: t)
    egg.closeSubpath()
    ctx.addPath(egg)
    ctx.strokePath()
    // The lenses, a line's width apart at the bridge so they don't run into one bar, and the bridge across.
    let gap = line / scale
    let left = m.lenses[0].divided(atDistance: gap / 2, from: .maxXEdge).remainder
    let right = m.lenses[1].divided(atDistance: gap / 2, from: .minXEdge).remainder
    for lens in [left, right] {
        ctx.addPath(CGPath(roundedRect: lens.applying(t).standardized, cornerWidth: line * 0.8, cornerHeight: line * 0.8, transform: nil))
        ctx.strokePath()
    }
    let bridgeY = (left.midY + right.midY) / 2
    ctx.move(to: CGPoint(x: left.maxX, y: bridgeY).applying(t))
    ctx.addLine(to: CGPoint(x: right.minX, y: bridgeY).applying(t))
    ctx.strokePath()
    for eye in m.eyes {
        let c = eye.applying(t), r = line * 0.75
        ctx.fillEllipse(in: CGRect(x: c.x - r, y: c.y - r, width: 2 * r, height: 2 * r))
    }
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}

/// The Chrome toolbar's egghead: the egg and its glasses, bold, on a flat white tile, larger than on the app icon, so
/// it still reads at 16 px on a light or a dark toolbar.
func toolbarIcon(_ px: Int) -> Data {
    let ctx = canvas(px)
    let side = CGFloat(px)
    let inset = side <= 16 ? 0.5 : side * 0.03
    let tile = rounded(CGRect(x: inset, y: inset, width: side - 2 * inset, height: side - 2 * inset), side * 0.22)
    ctx.addPath(tile)
    ctx.clip()
    ctx.setFillColor(color(0xffffff))
    ctx.fill(CGRect(x: 0, y: 0, width: side, height: side))
    ctx.resetClip()
    ctx.saveGState()
    ctx.scaleBy(x: side / 1024, y: side / 1024)
    drawHead(ctx, px: px, ink: color(pen), egg: color(shell), side: 860)
    ctx.restoreGState()
    ctx.addPath(rounded(CGRect(x: inset, y: inset, width: side - 2 * inset, height: side - 2 * inset).insetBy(dx: 0.5, dy: 0.5), side * 0.22 - 0.5))
    ctx.setLineWidth(1)
    ctx.setStrokeColor(color(0x000000, 0.14))
    ctx.strokePath()
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}

/// The Chrome Web Store's and the extensions page's 128-px icon: the full tile (its paper and face, drawn large and
/// scaled down) at 96 px, centred, with the 16-px clear margin Chrome asks for.
func storeIcon() -> Data {
    let ctx = canvas(128)
    ctx.interpolationQuality = .high
    ctx.draw(iconRep(384, .tile).cgImage!, in: CGRect(x: 16, y: 16, width: 96, height: 96))
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}

/// A Windows .ico holding the tile at each size: 256 px as PNG, the rest as 32-bit bitmaps, which every Windows
/// tool (the .NET SDK's icon embedding, Inno Setup, Explorer) reads.
func ico(_ sizes: [Int]) -> Data {
    let images: [Data] = sizes.map { px in
        let rep = iconRep(px, .tile)
        if px >= 256 { return rep.representation(using: .png, properties: [:])! }
        var dib = Data()
        func u32(_ v: UInt32) { withUnsafeBytes(of: v.littleEndian) { dib.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { withUnsafeBytes(of: v.littleEndian) { dib.append(contentsOf: $0) } }
        u32(40); u32(UInt32(px)); u32(UInt32(px * 2)); u16(1); u16(32); u32(0); u32(0); u32(0); u32(0); u32(0); u32(0)
        for y in (0..<px).reversed() {  // bottom-up rows of BGRA, not premultiplied
            for x in 0..<px {
                let c = rep.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB)
                let a = c?.alphaComponent ?? 0
                func byte(_ v: CGFloat) -> UInt8 { UInt8(max(0, min(255, (v * 255).rounded()))) }
                dib.append(contentsOf: [byte(c?.blueComponent ?? 0), byte(c?.greenComponent ?? 0), byte(c?.redComponent ?? 0), byte(a)])
            }
        }
        let maskRow = ((px + 31) / 32) * 4  // the AND mask: all clear (the alpha above decides)
        dib.append(Data(count: maskRow * px))
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

switch out.pathExtension {
case "png":
    try! icon(1024, out.lastPathComponent.hasSuffix("-dark.png") ? .dark : .light).write(to: out)
case "tiles":
    try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
    for px in [16, 20, 24, 32, 40, 48, 64, 128, 180, 192, 256, 512] {
        try! icon(px, .tile).write(to: out.appendingPathComponent("icon-\(px).png"))
    }
case "ico":
    try! ico([16, 20, 24, 32, 40, 48, 64, 128, 256]).write(to: out)
case "ios":
    try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
    squareForIOS = true
    try! icon(1024, .light).write(to: out.appendingPathComponent("AppIcon-1024.png"))
    try! icon(1024, .dark).write(to: out.appendingPathComponent("AppIcon-1024-dark.png"))
case "chrome":
    try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
    for px in [16, 32, 48] { try! toolbarIcon(px).write(to: out.appendingPathComponent("icon-\(px).png")) }
    try! storeIcon().write(to: out.appendingPathComponent("icon-128.png"))
case "marks":
    try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
    for px in [16, 18, 32, 36, 64] {
        try! mark(px).write(to: out.appendingPathComponent("mark-\(px).png"))
    }
default:
    try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
    for (name, px) in [("16x16", 16), ("16x16@2x", 32), ("32x32", 32), ("32x32@2x", 64), ("128x128", 128),
                       ("128x128@2x", 256), ("256x256", 256), ("256x256@2x", 512), ("512x512", 512),
                       ("512x512@2x", 1024)] {
        try! icon(px, .light).write(to: out.appendingPathComponent("icon_\(name).png"))
    }
}
