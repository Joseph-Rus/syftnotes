import SwiftUI

/// Over Google Drive, a laptop that's closed, asleep or off says so here, before the student asks it anything: "Your
/// laptop is asleep or off. Last here 2 hours ago." And when it's heard from again, "Your laptop is back." for a few
/// seconds. Nothing at all while the laptop's there (or not yet known to be away).
struct LaptopBanner: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        Group {
            if case .away(let since) = app.laptop {
                note(icon: "cloud_off", title: "Your laptop is asleep or off.",
                     detail: "\(LaptopPresence.lastHere(since)) Ask can't answer and new notes won't show until it's back.")
                    .accessibilityIdentifier("banner.laptopAway")
            } else if app.laptopBack {
                note(icon: "check_circle", title: "Your laptop is back.", detail: nil)
                    .accessibilityIdentifier("banner.laptopBack")
            }
        }
        .transition(.opacity.combined(with: .offset(y: -8)))
    }

    private func note(icon: String, title: String, detail: String?) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Icon(icon, size: 16, relativeTo: .subheadline).foregroundStyle(.inkSubtle)
                .alignmentGuide(.firstTextBaseline) { $0[.bottom] - 3 }
            VStack(alignment: .leading, spacing: 2) {
                Text(title).textStyle(.nav).foregroundStyle(.ink)
                if let detail {
                    Text(detail).textStyle(.meta).foregroundStyle(.inkMuted)
                }
            }
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(.surfaceSunken, in: .rect(cornerRadius: Radius.panel))
        .overlay(RoundedRectangle(cornerRadius: Radius.panel).strokeBorder(.line))
        .accessibilityElement(children: .combine)
    }
}

extension View {
    /// The laptop banner at the top of a main screen, under the title bar, when there's something to say.
    func laptopNotice() -> some View { modifier(LaptopNotice()) }
}

private struct LaptopNotice: ViewModifier {
    @Environment(AppModel.self) private var app
    @Environment(\.horizontalSizeClass) private var sizeClass

    func body(content: Content) -> some View {
        content.safeAreaInset(edge: .top, spacing: 0) {
            if app.laptop.isAway || app.laptopBack {
                LaptopBanner()
                    .padding(.horizontal, sizeClass == .regular ? Space.pageRegular : Space.pageCompact)
                    .padding(.vertical, 8)
                    .frame(maxWidth: .infinity)
            }
        }
    }
}
