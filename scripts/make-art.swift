// Draws the README demo (docs/demo.gif) and the link preview image (docs/social.png).
// Everything is drawn here rather than recorded, so the art never shows anyone's real desktop. The
// desktop is a drawing, not a recording of Windows: the editor, SyncHelper and the words typed are
// all made up. Runs on a Mac with `swift scripts/make-art.swift`.
//
//   swift scripts/make-art.swift
import AppKit
import ImageIO
import UniformTypeIdentifiers

let ink = NSColor(srgbRed: 0.05, green: 0.06, blue: 0.07, alpha: 1)
let ice = NSColor(srgbRed: 0.62, green: 0.82, blue: 1.0, alpha: 1)

func bitmap(_ width: Int, _ height: Int, draw: () -> Void) -> NSBitmapImageRep {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8, samplesPerPixel: 4,
        hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    draw()
    NSGraphicsContext.restoreGraphicsState()
    return rep
}

/// Cool silver light on a dark wallpaper, like the other apps' art.
func wallpaper(_ rect: NSRect) {
    NSGradient(starting: NSColor(srgbRed: 0.08, green: 0.09, blue: 0.13, alpha: 1), ending: ink)!.draw(in: rect, angle: -90)
    let glows: [(CGFloat, CGFloat, CGFloat, NSColor)] = [
        (0.18, 0.75, 0.42, NSColor(srgbRed: 0.84, green: 0.89, blue: 0.95, alpha: 0.45)),
        (0.85, 0.72, 0.38, NSColor(srgbRed: 0.59, green: 0.68, blue: 0.81, alpha: 0.40)),
        (0.70, 0.12, 0.46, NSColor(srgbRed: 0.75, green: 0.81, blue: 0.89, alpha: 0.32)),
        (0.22, 0.15, 0.30, NSColor(srgbRed: 0.44, green: 0.53, blue: 0.65, alpha: 0.32)),
    ]
    for (x, y, r, color) in glows {
        let center = NSPoint(x: rect.minX + rect.width * x, y: rect.minY + rect.height * y)
        NSGradient(starting: color, ending: color.withAlphaComponent(0))!
            .draw(fromCenter: center, radius: 0, toCenter: center, radius: rect.width * r, options: [])
    }
}

@discardableResult
func text(
    _ string: String, at point: NSPoint, size: CGFloat, weight: NSFont.Weight = .regular, color: NSColor = .white,
    centered: Bool = false, mono: Bool = false
) -> CGFloat {
    let font = mono ? NSFont.monospacedSystemFont(ofSize: size, weight: weight) : NSFont.systemFont(ofSize: size, weight: weight)
    let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
    let width = (string as NSString).size(withAttributes: attributes).width
    (string as NSString).draw(at: NSPoint(x: centered ? point.x - width / 2 : point.x, y: point.y), withAttributes: attributes)
    return width
}


let width: CGFloat = 720
let height: CGFloat = 450

func panel(_ rect: NSRect, fill: NSColor, radius: CGFloat = 10, shadow: Bool = true) {
    NSGraphicsContext.saveGraphicsState()
    if shadow {
        let s = NSShadow()
        s.shadowColor = .black.withAlphaComponent(0.5)
        s.shadowBlurRadius = 22
        s.shadowOffset = NSSize(width: 0, height: -8)
        s.set()
    }
    fill.setFill()
    NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
    NSGraphicsContext.restoreGraphicsState()
    NSColor.white.withAlphaComponent(0.1).setStroke()
    NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).stroke()
}

let dark = NSColor(srgbRed: 0.13, green: 0.14, blue: 0.16, alpha: 1)
let raised = NSColor(srgbRed: 0.17, green: 0.18, blue: 0.21, alpha: 1)

/// A window with a plain title bar and the three controls on the right.
func window(_ rect: NSRect, title: String, front: Bool) {
    panel(rect, fill: dark)
    text(title, at: NSPoint(x: rect.minX + 14, y: rect.maxY - 24), size: 12, color: .white.withAlphaComponent(front ? 0.9 : 0.5))
    for (i, mark) in ["✕", "☐", "—"].enumerated() {
        text(mark, at: NSPoint(x: rect.maxX - 24 - CGFloat(i) * 30, y: rect.maxY - 25), size: 11, color: .white.withAlphaComponent(0.55), centered: true)
    }
}

func taskbar() {
    NSColor(srgbRed: 0.09, green: 0.10, blue: 0.12, alpha: 0.96).setFill()
    NSRect(x: 0, y: 0, width: width, height: 36).fill()
    for i in 0..<4 {
        NSColor.white.withAlphaComponent(i == 0 ? 0.5 : 0.22).setFill()
        NSBezierPath(roundedRect: NSRect(x: width / 2 - 70 + CGFloat(i) * 36, y: 8, width: 20, height: 20), xRadius: 5, yRadius: 5).fill()
    }
    // The tray icon, with a dot once something has stolen focus.
    NSImage(contentsOfFile: "docs/icon.png")!.draw(in: NSRect(x: width - 96, y: 9, width: 18, height: 18))
    text("9:41 AM", at: NSPoint(x: width - 66, y: 11), size: 11, color: .white.withAlphaComponent(0.8))
}

func scene(typed: String, thief: Bool, toast: Bool, log: Bool) -> NSBitmapImageRep {
    bitmap(Int(width), Int(height)) {
        wallpaper(NSRect(x: 0, y: 0, width: width, height: height))
        let editor = NSRect(x: 40, y: 70, width: 470, height: 330)
        window(editor, title: "report.md — Editor", front: !thief)
        let lines = ["Week 6 lab report", "", "Results were consistent across the three", "trials, so the next step is " + typed]
        for (i, line) in lines.enumerated() {
            text(line, at: NSPoint(x: editor.minX + 22, y: editor.maxY - 70 - CGFloat(i) * 24), size: i == 0 ? 17 : 14,
                 weight: i == 0 ? .semibold : .regular, color: .white.withAlphaComponent(0.85))
        }
        if thief {
            let pop = NSRect(x: 250, y: 150, width: 300, height: 160)
            window(pop, title: "SyncHelper", front: true)
            text("Syncing 3 of 412 files…", at: NSPoint(x: pop.minX + 20, y: pop.maxY - 80), size: 13, color: .white.withAlphaComponent(0.8))
            text("sults were consiste", at: NSPoint(x: pop.minX + 20, y: pop.maxY - 110), size: 13, color: ice)
        }
        if log {
            let rect = NSRect(x: 120, y: 60, width: 520, height: 250)
            window(rect, title: "Who Stole My Focus", front: true)
            let rows = [
                ("9:41 AM", "SyncHelper (CloudSync)", "Stole focus", "Between your keystrokes"),
                ("9:12 AM", "Meetings", "Stole focus", "No input for 41 s"),
                ("8:55 AM", "SyncHelper (CloudSync)", "Stole focus", "No input for 2 min"),
            ]
            text("Top offender: SyncHelper (CloudSync), 2 today, usually from Editor", at: NSPoint(x: rect.minX + 16, y: rect.maxY - 58), size: 11.5, color: ice)
            for (i, row) in rows.enumerated() {
                let y = rect.maxY - 96 - CGFloat(i) * 34
                if i == 0 { raised.setFill(); NSRect(x: rect.minX + 8, y: y - 9, width: rect.width - 16, height: 30).fill() }
                text(row.0, at: NSPoint(x: rect.minX + 16, y: y), size: 11.5, color: .white.withAlphaComponent(0.6))
                text(row.1, at: NSPoint(x: rect.minX + 90, y: y), size: 12, weight: .medium, color: .white)
                text(row.2, at: NSPoint(x: rect.minX + 270, y: y), size: 12, color: NSColor(srgbRed: 1, green: 0.62, blue: 0.54, alpha: 1))
                text(row.3, at: NSPoint(x: rect.minX + 360, y: y), size: 12, color: .white.withAlphaComponent(0.75))
            }
        }
        taskbar()
        if toast {
            let rect = NSRect(x: width - 330, y: 50, width: 310, height: 74)
            panel(rect, fill: raised, radius: 8)
            NSImage(contentsOfFile: "docs/icon.png")!.draw(in: NSRect(x: rect.minX + 14, y: rect.maxY - 34, width: 20, height: 20))
            text("SyncHelper (CloudSync) stole focus from Editor", at: NSPoint(x: rect.minX + 42, y: rect.maxY - 30), size: 11.5, weight: .semibold)
            text("While you were typing: it came forward", at: NSPoint(x: rect.minX + 42, y: rect.maxY - 50), size: 11, color: .white.withAlphaComponent(0.7))
            text("between your keystrokes", at: NSPoint(x: rect.minX + 42, y: rect.maxY - 65), size: 11, color: .white.withAlphaComponent(0.7))
        }
    }
}

var frames: [(NSBitmapImageRep, Double)] = []
let words = "to compare the"
var typed = ""
for ch in words {
    typed.append(ch)
    frames.append((scene(typed: typed + "▍", thief: false, toast: false, log: false), 0.09))
}
frames.append((scene(typed: typed, thief: true, toast: false, log: false), 1.2))
frames.append((scene(typed: typed, thief: true, toast: true, log: false), 2.2))
frames.append((scene(typed: typed, thief: false, toast: false, log: true), 3.2))

let destination = CGImageDestinationCreateWithURL(URL(fileURLWithPath: "docs/demo.gif") as CFURL, UTType.gif.identifier as CFString, frames.count, nil)!
CGImageDestinationSetProperties(destination, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary)
for (rep, hold) in frames {
    CGImageDestinationAddImage(destination, rep.cgImage!, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFDelayTime: hold]] as CFDictionary)
}
guard CGImageDestinationFinalize(destination) else { fatalError("Couldn't write the GIF") }

let social = bitmap(1280, 640) {
    wallpaper(NSRect(x: 0, y: 0, width: 1280, height: 640))
    NSImage(contentsOfFile: "docs/icon.png")!.draw(in: NSRect(x: 96, y: 380, width: 140, height: 140))
    text("Who Stole My Focus", at: NSPoint(x: 96, y: 270), size: 72, weight: .semibold)
    text("Now on Windows.", at: NSPoint(x: 100, y: 212), size: 34, color: ice)
    text("Find the app that keeps jumping in front of your work.", at: NSPoint(x: 100, y: 160), size: 24, color: .white.withAlphaComponent(0.75))
    text("free · open source · no admin · no internet access", at: NSPoint(x: 100, y: 96), size: 18, color: .white.withAlphaComponent(0.5), mono: true)
}
try social.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "docs/social.png"))
Swift.print("Wrote docs/demo.gif and docs/social.png")
