import AppKit
import SwiftUI

private enum MainPage: String, CaseIterable, Identifiable, Hashable {
    case home, fixText, settings
    var id: String { rawValue }
    var icon: String { switch self { case .home: "house"; case .fixText: "character.cursor.ibeam"; case .settings: "gearshape" } }
}

struct KlavrioRootView: View {
    @ObservedObject var controller: KlavrioCoordinator
    @State private var page = MainPage.home
    @State private var showingRestore = false

    private var l: Localizer { controller.localizer }
    private var colorScheme: ColorScheme { controller.settings.theme == .dark ? .dark : .light }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider().opacity(0.5)
            NavigationSplitView {
                List(MainPage.allCases, selection: $page) { item in
                    Label(title(item), systemImage: item.icon).tag(item)
                }
                .navigationSplitViewColumnWidth(min: 170, ideal: 190, max: 220)
            } detail: {
                ScrollView {
                    VStack(alignment: .leading, spacing: 15) {
                        switch page {
                        case .home: HomePage(controller: controller)
                        case .fixText: FixTextPage(controller: controller)
                        case .settings: SettingsPage(controller: controller, showingRestore: $showingRestore)
                        }
                    }
                    .padding(22).frame(maxWidth: 920)
                }
                .background(Color.primary.opacity(colorScheme == .dark ? 0.018 : 0.028))
            }
            .navigationSplitViewStyle(.balanced)
            StatusBar(status: controller.status).padding(.horizontal, 18).padding(.vertical, 10)
        }
        .frame(minWidth: 760, idealWidth: 1020, minHeight: 620, idealHeight: 780)
        .preferredColorScheme(colorScheme)
        .environment(\.layoutDirection, l.layoutDirection)
        .tint(KlavrioStyle.accent)
        .alert(l["restoreTitle"], isPresented: $showingRestore) {
            Button(l["cancel"], role: .cancel) { }
            Button(l["confirmRestore"], role: .destructive) { controller.restoreDefaults() }
        } message: {
            Text(l["restoreQuestion"] + "\n\n" + l["restoreDetails"])
        }
    }

    private var header: some View {
        HStack(spacing: 13) {
            AppLogo().frame(width: 46, height: 46)
            VStack(alignment: .leading, spacing: 2) {
                Text("Klavrio").font(.title2.weight(.bold)).environment(\.layoutDirection, .leftToRight)
                Text(l["tagline"]).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button { controller.openGitHub() } label: { Label("GitHub", systemImage: "link") }.buttonStyle(SecondaryButtonStyle())
            HStack(spacing: 7) {
                Circle().fill(controller.settings.automaticCorrectionEnabled ? KlavrioStyle.success : Color.secondary).frame(width: 8, height: 8)
                Text(l[controller.settings.automaticCorrectionEnabled ? "active" : "standby"]).font(.caption.weight(.bold))
            }
            .padding(.horizontal, 12).frame(height: 38)
            .background(Color.primary.opacity(0.05), in: Capsule())
        }
        .padding(.horizontal, 20).padding(.vertical, 13)
    }

    private func title(_ page: MainPage) -> String {
        switch page { case .home: l["home"]; case .fixText: l["fixText"]; case .settings: l["settings"] }
    }
}

private struct AppLogo: View {
    var body: some View {
        Group {
            if let url = Bundle.main.url(forResource: "Klavrio", withExtension: "png"), let image = NSImage(contentsOf: url) {
                Image(nsImage: image).resizable().scaledToFit()
            } else {
                Image(systemName: "character.cursor.ibeam").resizable().scaledToFit().padding(9).foregroundStyle(.white)
                    .background(KlavrioStyle.accent, in: RoundedRectangle(cornerRadius: 13))
            }
        }
    }
}

private struct HomePage: View {
    @ObservedObject var controller: KlavrioCoordinator
    private var l: Localizer { controller.localizer }

    var body: some View {
        pageTitle(l["home"], l["tagline"])
        Card {
            HStack(spacing: 20) {
                SectionHeading(l["automatic"], icon: "wand.and.stars", subtitle: l["automaticHelp"])
                Toggle("", isOn: Binding(get: { controller.settings.automaticCorrectionEnabled }, set: controller.setAutomatic)).labelsHidden().toggleStyle(.switch)
            }
        }
        Card {
            SectionHeading(l["existing"], icon: "text.badge.checkmark", subtitle: l["existingHelp"])
            HStack(spacing: 8) {
                ForEach(["step1", "step2", "step3"], id: \.self) { key in
                    Text(l[key]).font(.caption.weight(.medium)).padding(.horizontal, 11).padding(.vertical, 8)
                        .background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 9))
                }
                Spacer()
            }.padding(.vertical, 12)
            HotkeyRecorder(binding: controller.settings.selectionHotkey, prompt: l["pressShortcut"], onChange: controller.setSelectionHotkey)
        }
        Card {
            SectionHeading(l["try"], icon: "flask", subtitle: l["tryHelp"])
            HStack {
                TextField(l["inputHint"], text: $controller.previewInput).textFieldStyle(.roundedBorder).onSubmit(controller.preview)
                Button(l["preview"], action: controller.preview).buttonStyle(PrimaryButtonStyle())
            }.padding(.top, 12)
            Text(controller.previewOutput).frame(maxWidth: .infinity, minHeight: 42, alignment: .leading)
                .padding(.horizontal, 12).background(Color.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 10))
                .padding(.top, 8)
        }
    }
}

private struct FixTextPage: View {
    @ObservedObject var controller: KlavrioCoordinator
    private var l: Localizer { controller.localizer }

    var body: some View {
        pageTitle(l["fixText"], l["existingHelp"])
        Card {
            SectionHeading(l["mode"], icon: "translate", subtitle: l["modeHelp"])
            Picker("", selection: Binding(get: { controller.settings.selectionMode }, set: controller.setSelectionMode)) {
                ForEach(SelectionDirection.allCases) { mode in Text(modeName(mode)).tag(mode) }
            }.labelsHidden().pickerStyle(.menu).frame(maxWidth: 360, alignment: .leading).padding(.top, 12)
        }
        Card {
            SectionHeading(l["selectionShortcut"], icon: "keyboard", subtitle: l["shortcutHelp"])
            HotkeyRecorder(binding: controller.settings.selectionHotkey, prompt: l["pressShortcut"], onChange: controller.setSelectionHotkey).padding(.top, 12)
        }
        Card {
            SectionHeading(l["toggleShortcut"], icon: "switch.2", subtitle: l["shortcutHelp"])
            HotkeyRecorder(binding: controller.settings.toggleHotkey, prompt: l["pressShortcut"], onChange: controller.setToggleHotkey).padding(.top, 12)
        }
    }

    private func modeName(_ mode: SelectionDirection) -> String {
        switch mode { case .auto: l["autoMode"]; case .toEnglish: l["toEnglish"]; case .toHebrew: l["toHebrew"]; case .toArabic: l["toArabic"]; case .toRussian: l["toRussian"]; case .toGerman: l["toGerman"]; case .toFrench: l["toFrench"] }
    }
}

private struct SettingsPage: View {
    @ObservedObject var controller: KlavrioCoordinator
    @Binding var showingRestore: Bool
    private var l: Localizer { controller.localizer }

    var body: some View {
        pageTitle(l["settings"], l["tagline"])
        Card {
            SectionHeading(l["appearance"], icon: "paintpalette")
            HStack(spacing: 14) {
                Picker(l["interfaceLanguage"], selection: Binding(get: { controller.settings.uiLanguage }, set: controller.setLanguage)) {
                    ForEach(Localizer.languages) { item in Text(item.name).tag(item.code) }
                }.frame(maxWidth: 260)
                Picker(l["theme"], selection: Binding(get: { controller.settings.theme }, set: controller.setTheme)) {
                    Text(l["dark"]).tag(AppTheme.dark); Text(l["light"]).tag(AppTheme.light)
                }.frame(maxWidth: 220)
                Spacer()
            }.padding(.top, 12)
            Toggle(l["startup"], isOn: Binding(get: { controller.settings.startAtLogin }, set: controller.setStartAtLogin)).padding(.top, 8)
        }
        Card {
            SectionHeading(l["permissions"], icon: "hand.raised", subtitle: l["permissionsHelp"])
            HStack {
                Label(controller.permissionsReady ? l["permissionsReady"] : l["permissionsMissing"], systemImage: controller.permissionsReady ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    .foregroundStyle(controller.permissionsReady ? KlavrioStyle.success : KlavrioStyle.warning)
                Spacer()
                if !controller.permissionsReady { Button(l["openPermissions"], action: controller.requestPermissions).buttonStyle(PrimaryButtonStyle()) }
            }.padding(.top, 12)
        }
        Card {
            SectionHeading(l["customData"], icon: "folder.badge.gearshape", subtitle: l["customDataHelp"])
            HStack {
                Button(l["openCustomData"], action: controller.openCustomData).buttonStyle(SecondaryButtonStyle())
                Button(l["reload"], action: controller.reloadDictionaries).buttonStyle(SecondaryButtonStyle())
            }.padding(.top, 12)
            Divider().padding(.vertical, 14)
            SectionHeading(l["communityUpdates"], icon: "person.3", subtitle: l["communityHelp"])
            Toggle(l["checkWordsOnOpen"], isOn: Binding(get: { controller.settings.checkDictionaryUpdatesOnStartup }, set: controller.setCheckDictionaryUpdates)).padding(.vertical, 8)
            HStack {
                ReadOnlyURLField(KlavrioLinks.dictionaryFeed)
                Button(l["updateWords"]) { Task { await controller.checkDictionaryUpdates() } }.buttonStyle(PrimaryButtonStyle())
            }
            HStack {
                Text(controller.dictionarySummary).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button(l["contributeWords"], action: controller.openContributionGuide).buttonStyle(.link)
            }.padding(.top, 5)
        }
        Card {
            SectionHeading(l["updates"], icon: "arrow.triangle.2.circlepath", subtitle: l["updatesHelp"])
            Toggle(l["checkOnOpen"], isOn: Binding(get: { controller.settings.checkUpdatesOnStartup }, set: controller.setCheckUpdates)).padding(.vertical, 8)
            HStack {
                ReadOnlyURLField(KlavrioLinks.updateFeed)
                Button(l["checkNow"]) { Task { await controller.checkForUpdates() } }.buttonStyle(PrimaryButtonStyle())
            }
            HStack { Text(controller.updateSummary).font(.caption).foregroundStyle(.secondary); Spacer(); if controller.updateURL != nil { Button(l["openUpdate"], action: controller.openAvailableUpdate) } }.padding(.top, 5)
        }
        Card {
            SectionHeading(l["privacy"], icon: "lock.shield", subtitle: l["privacyHelp"])
            HStack {
                Button(l["openLogs"], action: controller.openLogs).buttonStyle(SecondaryButtonStyle())
                Button(l["copyLog"], action: controller.copyLogPath).buttonStyle(SecondaryButtonStyle())
                Button(l["restore"]) { showingRestore = true }.buttonStyle(SecondaryButtonStyle())
            }.padding(.top, 12)
        }
        Card {
            SectionHeading(l["project"], icon: "link", subtitle: l["projectHelp"])
            HStack {
                ReadOnlyURLField(KlavrioLinks.project)
                Button(l["openGitHub"], action: controller.openGitHub).buttonStyle(PrimaryButtonStyle())
            }.padding(.top, 12)
        }
    }
}

private struct ReadOnlyURLField: View {
    let value: String

    init(_ value: String) { self.value = value }

    var body: some View {
        Text(value)
            .font(.system(.body, design: .monospaced))
            .lineLimit(1)
            .truncationMode(.middle)
            .textSelection(.enabled)
            .environment(\.layoutDirection, .leftToRight)
            .frame(maxWidth: .infinity, minHeight: 22, alignment: .leading)
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
            .background(Color.primary.opacity(0.035), in: RoundedRectangle(cornerRadius: 6))
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.primary.opacity(0.2)))
    }
}

@ViewBuilder
private func pageTitle(_ title: String, _ subtitle: String) -> some View {
    VStack(alignment: .leading, spacing: 5) {
        Text(title).font(.largeTitle.weight(.bold))
        Text(subtitle).foregroundStyle(.secondary)
    }.padding(.bottom, 4)
}
