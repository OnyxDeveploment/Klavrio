import SwiftUI

enum KlavrioStyle {
    static let accent = Color(red: 0.29, green: 0.38, blue: 0.83)
    static let secondary = Color(red: 0.06, green: 0.72, blue: 0.80)
    static let success = Color(red: 0.25, green: 0.83, blue: 0.57)
    static let warning = Color(red: 0.98, green: 0.72, blue: 0.31)
    static let error = Color(red: 0.97, green: 0.44, blue: 0.44)
}

struct Card<Content: View>: View {
    @Environment(\.colorScheme) private var colorScheme
    @State private var hovering = false
    let content: Content
    init(@ViewBuilder content: () -> Content) { self.content = content() }

    var body: some View {
        content
            .padding(22)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(colorScheme == .dark ? Color.white.opacity(0.055) : Color.white.opacity(0.92))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.primary.opacity(0.10), lineWidth: 1))
            .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
            .shadow(color: .black.opacity(colorScheme == .dark ? 0.18 : 0.07), radius: hovering ? 16 : 12, y: hovering ? 7 : 5)
            .offset(y: hovering ? -2 : 0)
            .onHover { value in withAnimation(.easeOut(duration: 0.18)) { hovering = value } }
    }
}

struct SectionHeading: View {
    let icon: String
    let title: String
    let subtitle: String?
    init(_ title: String, icon: String, subtitle: String? = nil) { self.title = title; self.icon = icon; self.subtitle = subtitle }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon)
                .font(.system(size: 17, weight: .semibold))
                .foregroundStyle(KlavrioStyle.secondary)
                .frame(width: 38, height: 38)
                .background(KlavrioStyle.accent.opacity(0.18), in: RoundedRectangle(cornerRadius: 12))
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.title3.weight(.semibold))
                if let subtitle { Text(subtitle).font(.subheadline).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true) }
            }
            Spacer(minLength: 0)
        }
    }
}

struct StatusBar: View {
    let status: StatusMessage
    private var color: Color {
        switch status.tone {
        case .success: KlavrioStyle.success
        case .warning: KlavrioStyle.warning
        case .error: KlavrioStyle.error
        case .neutral: .secondary
        }
    }
    private var icon: String {
        switch status.tone {
        case .success: "checkmark.circle"
        case .warning: "exclamationmark.triangle"
        case .error: "xmark.circle"
        case .neutral: "info.circle"
        }
    }
    var body: some View {
        HStack(spacing: 9) {
            Image(systemName: icon)
            Text(status.text).lineLimit(1)
            Spacer()
        }
        .font(.subheadline.weight(.medium))
        .foregroundStyle(color)
        .padding(.horizontal, 16).frame(height: 46)
        .background(color.opacity(0.09), in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(color.opacity(0.22)))
    }
}

struct PrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 16).frame(minHeight: 42)
            .background(KlavrioStyle.accent.opacity(configuration.isPressed ? 0.72 : 1), in: RoundedRectangle(cornerRadius: 11))
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
            .animation(.easeOut(duration: 0.14), value: configuration.isPressed)
    }
}

struct SecondaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.subheadline.weight(.semibold))
            .padding(.horizontal, 14).frame(minHeight: 40)
            .background(Color.primary.opacity(configuration.isPressed ? 0.10 : 0.045), in: RoundedRectangle(cornerRadius: 11))
            .overlay(RoundedRectangle(cornerRadius: 11).stroke(KlavrioStyle.accent.opacity(0.65)))
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
            .animation(.easeOut(duration: 0.14), value: configuration.isPressed)
    }
}
