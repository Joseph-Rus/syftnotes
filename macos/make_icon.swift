// Draws Syft Notes' Mac app icon when the app is built (macos/build-app.sh): Joey's pixel s on its dark tile (ink, the
// empty grid behind in mesh-dark, a mustard bottom row, three bits falling out below), at every size iconutil wants,
// flat on Apple's grid (an 824-px tile on the 1024 canvas). The 16- and 32-px pictures put every cell on whole pixels,
// with no bits: they'd be mud.
//   swift make_icon.swift out.iconset
// The drawing is tools/syft/make_art.swift, the one place the mark's geometry lives (it draws every other icon file in
// the repo too); this runs it for the iconset.

import Foundation

let out = URL(fileURLWithPath: CommandLine.arguments[1]).path
let art = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
    .appendingPathComponent("../tools/syft/make_art.swift").standardizedFileURL.path
let swift = Process()
swift.executableURL = URL(fileURLWithPath: "/usr/bin/xcrun")
swift.arguments = ["swift", art, "--iconset", out]
try swift.run()
swift.waitUntilExit()
exit(swift.terminationStatus)
