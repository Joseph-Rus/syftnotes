// Draws Study Stash's icons, all from the one mark below: a round-stroked "S" with a dot at its lower right ("S.").
//   swift make_icon.swift out.iconset         the Mac app icon (light), every size iconutil wants
//   swift make_icon.swift out.png             one 1024-px Mac app icon (light); out-dark.png draws the dark one
//   swift make_icon.swift out.tiles           icon-<px>.png: the Windows / web tile (cream, the S in navy), no margin
//   swift make_icon.swift out.ico             the Windows icon: the tile at 16–256 px in one .ico
//   swift make_icon.swift out.marks           mark-<px>.png: the monochrome "S." for the menu bar and the tray
// Light: a warm cream squircle with a fine paper grain, the S pressed into it in deep navy and the dot pressed in
// cream. Dark: a glossy near-black squircle, the S and dot in brushed copper. On Apple's macOS grid: an 824-pt
// rounded square on a 1024 canvas. The icons in assets/ and the app's Assets/ are made with this script.

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

// ---- the mark, in 1024 units with y going down (as drawn on paper); flipped into CoreGraphics' y-up below ----

/// The S's centre line: in from the top right, round the upper bowl, down the spine, round the lower bowl, out at the
/// bottom left. Stroked with round ends.
func sLine() -> CGPath {
    let p = CGMutablePath()
    p.move(to: CGPoint(x: 662, y: 352))
    p.addCurve(to: CGPoint(x: 506, y: 318), control1: CGPoint(x: 630, y: 328), control2: CGPoint(x: 570, y: 318))
    p.addCurve(to: CGPoint(x: 398, y: 414), control1: CGPoint(x: 440, y: 318), control2: CGPoint(x: 398, y: 358))
    p.addCurve(to: CGPoint(x: 512, y: 506), control1: CGPoint(x: 398, y: 468), control2: CGPoint(x: 446, y: 488))
    p.addCurve(to: CGPoint(x: 636, y: 604), control1: CGPoint(x: 588, y: 526), control2: CGPoint(x: 636, y: 548))
    p.addCurve(to: CGPoint(x: 504, y: 700), control1: CGPoint(x: 636, y: 664), control2: CGPoint(x: 582, y: 700))
    p.addCurve(to: CGPoint(x: 366, y: 648), control1: CGPoint(x: 444, y: 700), control2: CGPoint(x: 394, y: 680))
    return p
}
let strokeWidth: CGFloat = 104
let dotCentre = CGPoint(x: 702, y: 694)
let dotRadius: CGFloat = 40

/// y-down design units → CoreGraphics' y-up, on a 1024 canvas.
let flip = CGAffineTransform(a: 1, b: 0, c: 0, d: -1, tx: 0, ty: 1024)

func sOutline(width: CGFloat = strokeWidth) -> CGPath {
    sLine().copy(strokingWithWidth: width, lineCap: .round, lineJoin: .round, miterLimit: 10, transform: flip)
}

func dotPath(radius: CGFloat = dotRadius) -> CGPath {
    let c = dotCentre.applying(flip)
    return CGPath(ellipseIn: CGRect(x: c.x - radius, y: c.y - radius, width: 2 * radius, height: 2 * radius), transform: nil)
}

// ---- drawing helpers ----

/// A clear `px`-pixel picture in sRGB, so every colour lands as its hex.
func canvas(_ px: Int) -> CGContext {
    CGContext(data: nil, width: px, height: px, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
}

/// Device pixels per design unit for the picture being drawn: CoreGraphics takes shadow offsets and blurs in device
/// pixels, not in the scaled units everything else is drawn in.
var unit: CGFloat = 1

/// A shadow given in design units.
func shadow(_ ctx: CGContext, dx: CGFloat, dy: CGFloat, blur: CGFloat, _ c: CGColor) {
    ctx.setShadow(offset: CGSize(width: dx * unit, height: dy * unit), blur: blur * unit, color: c)
}

/// A shape pressed into the surface: its lower edge catches the light (a pale lip just below it), its fill, then a
/// soft shadow along its upper inside edge.
func deboss(_ ctx: CGContext, _ shape: CGPath, fill: CGColor, lip: CGColor, depth: CGFloat) {
    ctx.saveGState()
    ctx.translateBy(x: 0, y: -depth * 0.6)
    ctx.addPath(shape)
    ctx.setFillColor(lip)
    ctx.fillPath()
    ctx.restoreGState()

    ctx.addPath(shape)
    ctx.setFillColor(fill)
    ctx.fillPath()

    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()
    let around = CGMutablePath()
    around.addRect(CGRect(x: -200, y: -200, width: 1424, height: 1424))
    around.addPath(shape)
    shadow(ctx, dx: 0, dy: -depth, blur: depth * 1.6, color(0x000000, 0.55))
    ctx.addPath(around)
    ctx.setFillColor(color(0x000000, 1))
    ctx.fillPath(using: .evenOdd)
    ctx.restoreGState()
}

/// Deterministic noise (the same grain on every build).
struct Grain {
    var state: UInt64 = 0x5eed_1234_abcd
    mutating func next() -> CGFloat {
        state = state &* 6364136223846793005 &+ 1442695040888963407
        return CGFloat((state >> 33) & 0xffffff) / CGFloat(0xffffff)
    }
}

func paperGrain(_ ctx: CGContext, in tile: CGPath, px: Int) {
    ctx.saveGState()
    ctx.addPath(tile)
    ctx.clip()
    var g = Grain()
    // One device pixel per speck, over about a tenth of the tile, whatever the size.
    let size: CGFloat = max(1.2, 1024 / CGFloat(px))
    let side = 824 / size
    let dots = Int(0.1 * side * side)
    for _ in 0..<dots {
        let x = 100 + g.next() * 824, y = 100 + g.next() * 824
        let light = g.next() > 0.5
        ctx.setFillColor(light ? color(0xffffff, 0.10 * g.next()) : color(0x5a4a30, 0.07 * g.next()))
        ctx.fill(CGRect(x: x, y: y, width: size, height: size))
    }
    ctx.restoreGState()
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
    if look == .tile {  // the 824-unit tile fills 96% of the picture, with Windows 11's softer corner
        let scale = 0.96 * CGFloat(px) / 824
        ctx.translateBy(x: 0.02 * CGFloat(px) - 100 * scale, y: 0.02 * CGFloat(px) - 100 * scale)
        ctx.scaleBy(x: scale, y: scale)
        radius = 170
        unit = scale
    } else {
        ctx.scaleBy(x: CGFloat(px) / 1024, y: CGFloat(px) / 1024)
        unit = CGFloat(px) / 1024
    }
    // Below 128 px the grain and the relief turn to mud: a flat S (a little bolder at Finder-list and taskbar sizes).
    let flat = px < 128, small = px <= 32
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
        ctx.drawLinearGradient(gradient([color(0x2c2c2f), color(0x1a1a1c), color(0x0f0f11)]), start: CGPoint(x: 0, y: tileRect.maxY),
                               end: CGPoint(x: 0, y: tileRect.minY), options: [])
        let sheen = CGMutablePath()
        sheen.move(to: CGPoint(x: tileRect.minX, y: tileRect.maxY))
        sheen.addLine(to: CGPoint(x: tileRect.minX, y: tileRect.maxY - 250))
        sheen.addQuadCurve(to: CGPoint(x: tileRect.maxX, y: tileRect.maxY - 250), control: CGPoint(x: 512, y: tileRect.maxY - 330))
        sheen.addLine(to: CGPoint(x: tileRect.maxX, y: tileRect.maxY))
        sheen.closeSubpath()
        ctx.addPath(sheen)
        ctx.clip()
        ctx.drawLinearGradient(gradient([color(0xffffff, 0.10), color(0xffffff, 0.03)]), start: CGPoint(x: 0, y: tileRect.maxY),
                               end: CGPoint(x: 0, y: tileRect.maxY - 320), options: [])
        ctx.restoreGState()

        // The S and dot in brushed copper, lifted off the tile by a soft shadow.
        let mark = CGMutablePath()
        mark.addPath(sOutline(width: small ? strokeWidth * 1.15 : strokeWidth))
        mark.addPath(dotPath())
        ctx.saveGState()
        shadow(ctx, dx: 0, dy: -10, blur: 24, color(0x000000, 0.55))
        ctx.addPath(mark)
        ctx.setFillColor(color(0xb8744a))
        ctx.fillPath()
        ctx.restoreGState()
        ctx.saveGState()
        ctx.addPath(mark)
        ctx.clip()
        ctx.drawLinearGradient(gradient([color(0xf7d2b0), color(0xdb996c), color(0xb56d45), color(0xe8b38c), color(0xc98158)],
                                        [0, 0.3, 0.55, 0.78, 1]),
                               start: CGPoint(x: 330, y: 780), end: CGPoint(x: 720, y: 280), options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
        if !flat {
            var g = Grain(state: 0xc0ffee)
            for i in 0..<420 {  // the brushing: fine, faint streaks along the metal
                let y = 250 + CGFloat(i) * 1.3
                ctx.setFillColor(g.next() > 0.5 ? color(0xffffff, 0.07 * g.next()) : color(0x6a3418, 0.08 * g.next()))
                ctx.fill(CGRect(x: 300, y: y, width: 460, height: 0.8))
            }
            // A soft sheen along the upper side of the stroke.
            shadow(ctx, dx: -4, dy: 8, blur: 30, color(0xfff1e0, 0.35))
            ctx.addPath(sLine().copy(strokingWithWidth: strokeWidth * 0.2, lineCap: .round, lineJoin: .round, miterLimit: 10,
                                     transform: flip.translatedBy(x: -6, y: -12)))
            ctx.setFillColor(color(0xffe6cc, 0.14))
            ctx.fillPath()
        }
        ctx.restoreGState()
    } else {
        // A warm cream tile (on its shadow on the Mac), a faint paper grain, a soft light across the top.
        ctx.saveGState()
        if look == .light { shadow(ctx, dx: 0, dy: -12, blur: 30, color(0x000000, 0.28)) }
        ctx.addPath(tile)
        ctx.setFillColor(color(0xe4ded2))
        ctx.fillPath()
        ctx.restoreGState()
        ctx.saveGState()
        ctx.addPath(tile)
        ctx.clip()
        ctx.drawLinearGradient(gradient([color(0xf1ede5), color(0xe7e1d6), color(0xdbd4c6)]), start: CGPoint(x: 0, y: tileRect.maxY),
                               end: CGPoint(x: 0, y: tileRect.minY), options: [])
        ctx.restoreGState()
        if !flat {
            paperGrain(ctx, in: tile, px: px)
            ctx.saveGState()
            ctx.addPath(tile)
            ctx.clip()
            ctx.drawLinearGradient(gradient([color(0xffffff, 0.35), color(0xffffff, 0)]), start: CGPoint(x: 0, y: tileRect.maxY),
                                   end: CGPoint(x: 0, y: tileRect.maxY - 300), options: [])
            ctx.restoreGState()
        }

        let navy = color(0x2f4a7a)
        if flat {
            ctx.addPath(sOutline(width: small ? strokeWidth * 1.18 : strokeWidth))
            ctx.setFillColor(navy)
            ctx.fillPath()
            if !small {
                ctx.addPath(dotPath())
                ctx.setFillColor(color(0xcfc7b7))
                ctx.fillPath()
            }
        } else {
            // The S pressed in, in navy; the dot pressed in, in the tile's own cream.
            deboss(ctx, sOutline(), fill: navy, lip: color(0xffffff, 0.75), depth: 9)
            ctx.saveGState()
            ctx.addPath(sOutline())
            ctx.clip()
            ctx.drawLinearGradient(gradient([color(0x000000, 0.10), color(0xffffff, 0.03)]), start: CGPoint(x: 0, y: 760),
                                   end: CGPoint(x: 0, y: 280), options: [])
            ctx.restoreGState()
            deboss(ctx, dotPath(), fill: color(0xe2dccf), lip: color(0xffffff, 0.8), depth: 7)
        }

        // The fine edge along the tile, as on Apple's icons.
        ctx.addPath(rounded(tileRect.insetBy(dx: 1.5, dy: 1.5), radius - 1))
        ctx.setLineWidth(3)
        ctx.setStrokeColor(color(0x000000, 0.08))
        ctx.strokePath()
    }

    return NSBitmapImageRep(cgImage: ctx.makeImage()!)
}

/// The monochrome "S." for the menu bar (a template image: black on clear, the menu bar tints it) and the Windows
/// tray (the app tints it white or black): the mark alone, filling most of a `px`-pixel square, room at its top right
/// for the recording badge.
func mark(_ px: Int) -> Data {
    let ctx = canvas(px)
    // The mark spans x 314…730, y 266…752 (design units): fit its height to 84% of the square, centred.
    let height: CGFloat = 752 - 266, scale = 0.84 * CGFloat(px) / height
    let midX: CGFloat = (314 + 730) / 2, midY: CGFloat = 1024 - (266 + 752) / 2
    ctx.translateBy(x: CGFloat(px) / 2 - midX * scale, y: CGFloat(px) / 2 - midY * scale)
    ctx.scaleBy(x: scale, y: scale)
    let m = CGMutablePath()
    m.addPath(sOutline(width: strokeWidth * 1.12))
    m.addPath(dotPath(radius: dotRadius * 1.15))
    ctx.addPath(m)
    ctx.setFillColor(color(0x000000))
    ctx.fillPath()
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
    for px in [16, 20, 24, 32, 40, 48, 64, 128, 180, 256, 512] {
        try! icon(px, .tile).write(to: out.appendingPathComponent("icon-\(px).png"))
    }
case "ico":
    try! ico([16, 20, 24, 32, 40, 48, 64, 128, 256]).write(to: out)
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
