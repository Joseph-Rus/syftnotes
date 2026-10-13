// A stand-in for full-screen slides or a full-screen Zoom call: one plain window that goes full screen in its own
// Space and stays there until it's ended. The self-test (STUDYSTASH_SELFTEST_FULLSCREEN=<this file>) runs it to prove
// the recorder pill floats over another app's full-screen Space.
//   swift macos/fullscreen-app.swift
import AppKit

final class Delegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    func applicationDidFinishLaunching(_ note: Notification) {
        window = NSWindow(contentRect: NSRect(x: 200, y: 200, width: 800, height: 500),
                          styleMask: [.titled, .resizable, .closable], backing: .buffered, defer: false)
        window.title = "Full-screen stand-in"
        window.backgroundColor = NSColor(calibratedRed: 0.15, green: 0.35, blue: 0.6, alpha: 1)
        window.collectionBehavior = [.fullScreenPrimary]
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { self.window.toggleFullScreen(nil) }
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
let delegate = Delegate()
app.delegate = delegate
app.run()
