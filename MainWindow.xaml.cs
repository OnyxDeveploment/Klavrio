using MaterialDesignThemes.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Klavrio;

public partial class MainWindow : Window
{
    private readonly LocalizationManager localization = new();
    private readonly AppSettings settings;
    private readonly CorrectionEngine engine;
    private readonly KeyboardMonitor monitor;
    private readonly Forms.NotifyIcon tray;
    private nint windowHandle;
    private HwndSource? windowSource;
    private HotkeyBinding? registeredSelection;
    private HotkeyBinding? registeredToggle;
    private System.Windows.Controls.Button? activeCaptureButton;
    private bool captureIsSelection;
    private bool convertingSelection;
    private bool initializing = true;
    private bool exiting;
    private bool trayHintShown;
    private bool updateCheckInProgress;
    private bool dictionaryUpdateCheckInProgress;
    private string? availableUpdateUrl;

    public MainWindow()
    {
        settings = AppSettings.Load();
        localization.SetLanguage(settings.UiLanguage);
        DataContext = localization;
        AppearanceManager.Apply(ParseTheme(settings.Theme));
        InitializeComponent();
        CustomDataManager.EnsureFiles();
        CommunityDataManager.EnsureDirectory();
        engine = new CorrectionEngine(
            Path.Combine(AppContext.BaseDirectory, "Data"),
            CommunityDataManager.DirectoryPath,
            CustomDataManager.DirectoryPath);
        SelectionConverter.Configure(engine);
        monitor = new KeyboardMonitor(engine, action => Dispatcher.BeginInvoke(action));
        tray = CreateTrayIcon();
        LoadBrandImage();

        ApplyLocalizedChoices();
        ConversionMode.SelectedValue = (SelectionDirection)Math.Clamp(settings.SelectionMode, 0, 6);
        SetSelectionShortcutText((settings.SelectionHotkey ?? HotkeyBinding.SelectionDefault).DisplayText);
        SetToggleShortcutText((settings.ToggleHotkey ?? HotkeyBinding.ToggleDefault).DisplayText);
        CloseToTrayCheckBox.IsChecked = settings.CloseToTray;
        settings.StartWithWindows = StartupManager.IsEnabled();
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        GitHubUrlTextBox.Text = settings.GitHubUrl;
        CheckUpdatesOnStartupCheckBox.IsChecked = settings.CheckUpdatesOnStartup;
        UpdateManifestUrlTextBox.Text = settings.UpdateManifestUrl;
        CheckDictionaryUpdatesOnStartupCheckBox.IsChecked = settings.CheckDictionaryUpdatesOnStartup;
        DictionaryManifestUrlTextBox.Text = settings.DictionaryManifestUrl;
        RefreshLocalizedUpdateSummaries();
        AutomaticToggle.IsChecked = settings.AutomaticCorrectionEnabled;
        ApplyFlowDirection();
        PreviewOutput.Text = localization["PreviewEmpty"];
        UpdateActiveState();
        initializing = false;

        monitor.Status = message =>
        {
            if (!Dispatcher.HasShutdownStarted) SetStatus(message, StatusTone.Success);
        };

        if (!monitor.Start())
        {
            AutomaticToggle.IsChecked = false;
            AutomaticToggle.IsEnabled = false;
            SetStatus("Windows blocked the keyboard monitor. Selection shortcuts still work.", StatusTone.Error);
        }
        else
        {
            SetStatus(localization["Ready"], StatusTone.Success);
        }
    }

    private static AppTheme ParseTheme(string? value) => Enum.TryParse(value, true, out AppTheme theme) ? theme : AppTheme.Dark;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout();
        if (settings.CheckDictionaryUpdatesOnStartup && !string.IsNullOrWhiteSpace(settings.DictionaryManifestUrl))
            await CheckDictionaryUpdatesAsync(manual: false);
        if (settings.CheckUpdatesOnStartup && !string.IsNullOrWhiteSpace(settings.UpdateManifestUrl))
            await CheckForUpdatesAsync(manual: false);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (PreviewComposer is null || HeaderGitHubButton is null) return;

        bool compact = ActualWidth < 780;
        bool veryCompact = ActualWidth < 690;
        bool rightToLeft = localization.IsRightToLeft;
        BrandTagline.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        GitHubButtonLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeaderGithubIcon.Margin = compact
            ? new Thickness(0)
            : rightToLeft ? new Thickness(7, 0, 0, 0) : new Thickness(0, 0, 7, 0);
        HeaderGitHubButton.Padding = compact ? new Thickness(12, 9, 12, 9) : new Thickness(16, 9, 16, 9);
        HomeTabLabel.Visibility = veryCompact ? Visibility.Collapsed : Visibility.Visible;
        ConvertTabLabel.Visibility = veryCompact ? Visibility.Collapsed : Visibility.Visible;
        SettingsTabLabel.Visibility = veryCompact ? Visibility.Collapsed : Visibility.Visible;

        if (compact)
        {
            PreviewComposer.RowDefinitions[1].Height = GridLength.Auto;
            PreviewComposer.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetRow(PreviewActionButton, 1);
            Grid.SetColumn(PreviewActionButton, 0);
            PreviewActionButton.Margin = new Thickness(0, 10, 0, 0);
            PreviewActionButton.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        }
        else
        {
            PreviewComposer.RowDefinitions[1].Height = new GridLength(0);
            PreviewComposer.ColumnDefinitions[1].Width = GridLength.Auto;
            Grid.SetRow(PreviewActionButton, 0);
            Grid.SetColumn(PreviewActionButton, 1);
            PreviewActionButton.Margin = rightToLeft ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0);
            PreviewActionButton.HorizontalAlignment = rightToLeft
                ? System.Windows.HorizontalAlignment.Left
                : System.Windows.HorizontalAlignment.Right;
        }
    }

    private void ApplyLocalizedChoices()
    {
        object? selectedMode = ConversionMode.SelectedValue;
        object? selectedTheme = ThemeCombo.SelectedValue;
        string selectedLanguage = settings.UiLanguage;

        ConversionMode.ItemsSource = new[]
        {
            new Choice<SelectionDirection>(SelectionDirection.Auto, localization["AutoMode"]),
            new Choice<SelectionDirection>(SelectionDirection.ToEnglish, localization["ToEnglish"]),
            new Choice<SelectionDirection>(SelectionDirection.ToHebrew, localization["ToHebrew"]),
            new Choice<SelectionDirection>(SelectionDirection.ToArabic, localization["ToArabic"]),
            new Choice<SelectionDirection>(SelectionDirection.ToRussian, localization["ToRussian"]),
            new Choice<SelectionDirection>(SelectionDirection.ToGerman, localization["ToGerman"]),
            new Choice<SelectionDirection>(SelectionDirection.ToFrench, localization["ToFrench"])
        };
        UiLanguageCombo.ItemsSource = new[]
        {
            new Choice<string>("en", "English"), new Choice<string>("he", "עברית"), new Choice<string>("ar", "العربية"),
            new Choice<string>("ru", "Русский"), new Choice<string>("de", "Deutsch"), new Choice<string>("fr", "Français")
        };
        ThemeCombo.ItemsSource = new[]
        {
            new Choice<AppTheme>(AppTheme.Dark, localization["Dark"]),
            new Choice<AppTheme>(AppTheme.Light, localization["Light"])
        };

        ConversionMode.SelectedValue = selectedMode ?? (SelectionDirection)Math.Clamp(settings.SelectionMode, 0, 6);
        UiLanguageCombo.SelectedValue = selectedLanguage;
        ThemeCombo.SelectedValue = selectedTheme ?? ParseTheme(settings.Theme);
    }

    private void RefreshLocalizedUpdateSummaries(bool packageRemainsInstalled = false)
    {
        UpdateStatusText.Text = string.IsNullOrWhiteSpace(settings.UpdateManifestUrl)
            ? localization["UpdateUrlNeeded"]
            : localization["UpdateConfigured"];

        DictionaryUpdateStatusText.Text = string.IsNullOrWhiteSpace(settings.DictionaryManifestUrl)
            ? CommunityDataManager.HasActivePackage && !string.IsNullOrWhiteSpace(settings.InstalledDictionaryVersion)
                ? string.Format(localization[packageRemainsInstalled ? "DictionaryRemainsInstalled" : "DictionaryInstalled"], settings.InstalledDictionaryVersion)
                : localization["DictionaryUrlNeeded"]
            : CommunityDataManager.HasActivePackage && !string.IsNullOrWhiteSpace(settings.InstalledDictionaryVersion)
                ? string.Format(localization["DictionaryInstalled"], settings.InstalledDictionaryVersion)
                : localization["DictionaryConfigured"];
    }

    private void ApplyFlowDirection()
    {
        bool rightToLeft = localization.IsRightToLeft;
        FlowDirection = rightToLeft ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

        if (Root is null) return;

        TextAlignment textAlignment = rightToLeft ? TextAlignment.Right : TextAlignment.Left;
        PreviewInput.TextAlignment = textAlignment;
        PreviewOutput.TextAlignment = textAlignment;
        StatusText.TextAlignment = textAlignment;

        // Technical values remain readable in their natural left-to-right order.
        foreach (FrameworkElement element in new FrameworkElement[]
                 {
                     HomeSelectionShortcutText, SelectionShortcutText, HomeToggleShortcutText, ToggleShortcutText,
                     GitHubUrlTextBox, UpdateManifestUrlTextBox, DictionaryManifestUrlTextBox
                 })
        {
            element.FlowDirection = System.Windows.FlowDirection.LeftToRight;
        }

        GitHubUrlTextBox.TextAlignment = TextAlignment.Left;
        UpdateManifestUrlTextBox.TextAlignment = TextAlignment.Left;
        DictionaryManifestUrlTextBox.TextAlignment = TextAlignment.Left;
        ApplyResponsiveLayout();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        Drawing.Icon icon;
        try { icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application; }
        catch { icon = Drawing.SystemIcons.Application; }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Klavrio", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("Toggle automatic correction", null, (_, _) => Dispatcher.Invoke(() => AutomaticToggle.IsChecked = AutomaticToggle.IsChecked != true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Diagnostic logs", null, (_, _) => Dispatcher.Invoke(OpenLogs));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var iconHost = new Forms.NotifyIcon
        {
            Text = "Klavrio",
            Icon = icon,
            Visible = true,
            ContextMenuStrip = menu
        };
        iconHost.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        return iconHost;
    }

    private void LoadBrandImage()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Klavrio.Logo.png");
            if (stream is null) return;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            BrandImage.Source = image;
        }
        catch (Exception exception) { CrashLogger.Record("Load logo", exception); }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        windowHandle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(windowHandle);
        windowSource?.AddHook(WndProc);
        WinApi.TrySetDarkTitleBar(windowHandle, ParseTheme(settings.Theme) == AppTheme.Dark);
        RegisterSavedHotkeys();
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != SelectionHotkey.Message) return 0;
        int id = wParam.ToInt32();
        if (id == SelectionHotkey.SelectionId)
        {
            _ = ConvertSelectedTextAsync();
            handled = true;
        }
        else if (id == SelectionHotkey.ToggleId)
        {
            AutomaticToggle.IsChecked = AutomaticToggle.IsChecked != true;
            handled = true;
        }
        return 0;
    }

    private void RegisterSavedHotkeys()
    {
        RegisterSavedHotkey(SelectionHotkey.SelectionId, settings.SelectionHotkey ?? HotkeyBinding.SelectionDefault, true);
        RegisterSavedHotkey(SelectionHotkey.ToggleId, settings.ToggleHotkey ?? HotkeyBinding.ToggleDefault, false);
    }

    private void RegisterSavedHotkey(int id, HotkeyBinding binding, bool selection)
    {
        try
        {
            SelectionHotkey.Register(windowHandle, id, binding);
            if (selection) registeredSelection = binding; else registeredToggle = binding;
            CrashLogger.Success($"Hotkey registered | action={(selection ? "selection" : "toggle")} | binding={binding.DisplayText}");
        }
        catch (Exception exception)
        {
            CrashLogger.Record($"Register {(selection ? "selection" : "toggle")} hotkey", exception);
            SetStatus(exception.Message, StatusTone.Error);
        }
    }

    private bool ChangeHotkey(int id, HotkeyBinding candidate, bool selection)
    {
        HotkeyBinding? previous = selection ? registeredSelection : registeredToggle;
        try
        {
            if (previous is not null) SelectionHotkey.Unregister(windowHandle, id);
            SelectionHotkey.Register(windowHandle, id, candidate);
            if (selection)
            {
                registeredSelection = candidate;
                settings.SelectionHotkey = candidate;
            }
            else
            {
                registeredToggle = candidate;
                settings.ToggleHotkey = candidate;
            }
            settings.Save();
            CrashLogger.Success($"Hotkey changed | action={(selection ? "selection" : "toggle")} | binding={candidate.DisplayText}");
            SetStatus($"Shortcut saved: {candidate.DisplayText}", StatusTone.Success);
            return true;
        }
        catch (Exception exception)
        {
            CrashLogger.Record("Change hotkey", exception);
            if (previous is not null)
            {
                try { SelectionHotkey.Register(windowHandle, id, previous); }
                catch (Exception rollbackException)
                {
                    if (selection) registeredSelection = null; else registeredToggle = null;
                    CrashLogger.Record("Restore previous hotkey", rollbackException);
                }
            }
            SetStatus(exception.Message, StatusTone.Error);
            return false;
        }
    }

    private void SelectionShortcutButton_Click(object sender, RoutedEventArgs e) => BeginShortcutCapture((System.Windows.Controls.Button)sender, true);
    private void ToggleShortcutButton_Click(object sender, RoutedEventArgs e) => BeginShortcutCapture((System.Windows.Controls.Button)sender, false);

    private void BeginShortcutCapture(System.Windows.Controls.Button button, bool selection)
    {
        CancelShortcutCapture();
        activeCaptureButton = button;
        captureIsSelection = selection;
        if (selection) SetSelectionShortcutText("Press a shortcut…");
        else SetToggleShortcutText("Press a shortcut…");
        button.Focus();
        SetStatus("Press Esc to cancel. Use a function key or include Ctrl, Alt, Shift, or Win.", StatusTone.Neutral);
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (activeCaptureButton is null) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CancelShortcutCapture(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        ModifierKeys activeModifiers = Keyboard.Modifiers;
        if (activeModifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (activeModifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (activeModifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (activeModifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;
        var candidate = new HotkeyBinding(modifiers, (Forms.Keys)KeyInterop.VirtualKeyFromKey(key));
        if (!candidate.IsValid)
        {
            if (captureIsSelection) SetSelectionShortcutText("Add a modifier or use F1–F24");
            else SetToggleShortcutText("Add a modifier or use F1–F24");
            return;
        }

        bool accepted = ChangeHotkey(captureIsSelection ? SelectionHotkey.SelectionId : SelectionHotkey.ToggleId, candidate, captureIsSelection);
        if (accepted)
        {
            if (captureIsSelection) SetSelectionShortcutText(candidate.DisplayText);
            else SetToggleShortcutText(candidate.DisplayText);
        }
        else RestoreShortcutText();
        activeCaptureButton = null;
    }

    private void CancelShortcutCapture()
    {
        if (activeCaptureButton is null) return;
        RestoreShortcutText();
        activeCaptureButton = null;
        SetStatus("Shortcut change cancelled.", StatusTone.Neutral);
    }

    private void RestoreShortcutText()
    {
        SetSelectionShortcutText((registeredSelection ?? settings.SelectionHotkey ?? HotkeyBinding.SelectionDefault).DisplayText);
        SetToggleShortcutText((registeredToggle ?? settings.ToggleHotkey ?? HotkeyBinding.ToggleDefault).DisplayText);
    }

    private void SetSelectionShortcutText(string value)
    {
        SelectionShortcutText.Text = value;
        HomeSelectionShortcutText.Text = value;
    }

    private void SetToggleShortcutText(string value)
    {
        ToggleShortcutText.Text = value;
        HomeToggleShortcutText.Text = value;
    }

    private void AutomaticToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (monitor is null || tray is null) return;
        bool active = AutomaticToggle.IsChecked == true;
        monitor.Enabled = active;
        settings.AutomaticCorrectionEnabled = active;
        UpdateActiveState();
        if (!initializing) settings.Save();
        SetStatus(active ? "Automatic correction is on." : "Automatic correction is off. Selection shortcuts remain available.",
            active ? StatusTone.Success : StatusTone.Neutral);
    }

    private void ConversionMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settings is null || ConversionMode.SelectedValue is not SelectionDirection direction) return;
        settings.SelectionMode = (int)direction;
        if (!initializing) settings.Save();
    }

    private void UiLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settings is null || UiLanguageCombo.SelectedValue is not string code || code == localization.Language) return;
        settings.UiLanguage = code;
        localization.SetLanguage(code);
        CrashLogger.Success($"Interface language changed | code={code}");
        ApplyFlowDirection();
        ApplyLocalizedChoices();
        RefreshLocalizedUpdateSummaries();
        PreviewOutput.Text = localization["PreviewEmpty"];
        UpdateActiveState();
        if (!initializing) { settings.Save(); SetStatus(localization["Ready"], StatusTone.Success); }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settings is null || ThemeCombo.SelectedValue is not AppTheme theme) return;
        settings.Theme = theme.ToString();
        AppearanceManager.Apply(theme);
        if (windowHandle != 0) WinApi.TrySetDarkTitleBar(windowHandle, theme == AppTheme.Dark);
        if (!initializing)
        {
            CrashLogger.Success($"Theme changed | theme={theme}");
            settings.Save();
            SetStatus(theme == AppTheme.Dark ? "Dark theme enabled." : "Light theme enabled.", StatusTone.Success);
        }
    }

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (settings is null || initializing) return;
        bool requested = StartWithWindowsCheckBox.IsChecked == true;
        if (StartupManager.SetEnabled(requested))
        {
            settings.StartWithWindows = requested;
            settings.Save();
            SetStatus(requested ? "Klavrio will start with Windows." : "Windows startup disabled.", StatusTone.Success);
        }
        else
        {
            initializing = true;
            StartWithWindowsCheckBox.IsChecked = !requested;
            initializing = false;
            SetStatus("Windows startup could not be changed. See the diagnostic log.", StatusTone.Error);
        }
    }

    private void CloseToTrayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (settings is null) return;
        settings.CloseToTray = CloseToTrayCheckBox.IsChecked == true;
        if (!initializing) settings.Save();
    }

    private void PreviewInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        PreviewWord();
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => PreviewWord();

    private void PreviewWord()
    {
        SelectionDirection direction = ConversionMode.SelectedValue is SelectionDirection selected ? selected : SelectionDirection.Auto;
        string converted = SelectionConverter.Convert(PreviewInput.Text, direction);
        string result = converted != PreviewInput.Text ? converted : "No conversion available";
        PreviewOutput.Text = result;
        bool found = result != "No conversion available";
        PreviewOutput.Foreground = FindBrush(found ? "MaterialDesign.Brush.Secondary" : "Klavrio.WarningBrush");
        SetStatus(found ? "Preview generated locally." : "No safe conversion was found.", found ? StatusTone.Success : StatusTone.Warning);
    }

    private async Task ConvertSelectedTextAsync()
    {
        if (convertingSelection) return;
        convertingSelection = true;
        Forms.IDataObject? previousClipboard = null;
        bool clipboardTouched = false;
        var timer = Stopwatch.StartNew();
        try
        {
            previousClipboard = Forms.Clipboard.GetDataObject();
            nint targetWindow = WinApi.GetForegroundWindow();
            if (targetWindow == windowHandle || targetWindow == 0) { SetStatus("Select text in another app first.", StatusTone.Warning); return; }
            if (WinApi.IsLikelyPasswordField(targetWindow)) { SetStatus("Secure input detected — conversion skipped.", StatusTone.Warning); return; }

            HotkeyBinding activeShortcut = registeredSelection ?? settings.SelectionHotkey ?? HotkeyBinding.SelectionDefault;
            for (int attempt = 0; attempt < 24 && SelectionHotkey.IsPressed(activeShortcut); attempt++)
                await Task.Delay(25);
            if (SelectionHotkey.IsPressed(activeShortcut))
            {
                SetStatus("Release the shortcut keys and try again.", StatusTone.Warning);
                return;
            }
            if (WinApi.GetForegroundWindow() != targetWindow)
            {
                SetStatus("Focus changed, so conversion was cancelled.", StatusTone.Warning);
                return;
            }

            uint version = SelectionHotkey.ClipboardVersion();
            if (!WinApi.CopySelection()) { SetStatus("Could not copy the selected text.", StatusTone.Error); return; }
            clipboardTouched = true;
            for (int attempt = 0; attempt < 10 && SelectionHotkey.ClipboardVersion() == version; attempt++)
                await Task.Delay(35);
            if (SelectionHotkey.ClipboardVersion() == version || !Forms.Clipboard.ContainsText())
            { SetStatus("No text selection was found.", StatusTone.Warning); return; }

            string selected = Forms.Clipboard.GetText();
            if (selected.Length == 0 || selected.Length > 12_000)
            { SetStatus("Selection must contain 1–12,000 characters.", StatusTone.Warning); return; }
            var direction = ConversionMode.SelectedValue is SelectionDirection selectedDirection ? selectedDirection : SelectionDirection.Auto;
            string corrected = SelectionConverter.Convert(selected, direction);
            if (corrected == selected) { SetStatus("The selected text already looks correct.", StatusTone.Neutral); return; }
            if (WinApi.GetForegroundWindow() != targetWindow) { SetStatus("Focus changed, so conversion was cancelled.", StatusTone.Warning); return; }
            if (!WinApi.WriteSelection(corrected))
            {
                CrashLogger.Warning("Selection insertion incomplete");
                SetStatus("Text insertion failed. Use Ctrl+Z if the selection changed.", StatusTone.Error);
                return;
            }

            CrashLogger.Success($"Selection converted | mode={direction} | elapsedMs={timer.ElapsedMilliseconds}");
            SetStatus("Selected text converted successfully.", StatusTone.Success);
        }
        catch (Exception exception)
        {
            CrashLogger.Record("Convert selection", exception);
            SetStatus("Conversion failed. The diagnostic log has the technical details.", StatusTone.Error);
        }
        finally
        {
            if (clipboardTouched && previousClipboard is not null)
            {
                try { Forms.Clipboard.SetDataObject(previousClipboard, true, 3, 100); }
                catch (Exception exception) { CrashLogger.Record("Restore clipboard", exception); }
            }
            convertingSelection = false;
        }
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmationDialog(
            this,
            localization["RestoreTitle"],
            localization["RestoreQuestion"],
            localization["RestoreDetails"],
            localization["RestoreConfirm"],
            localization["Cancel"],
            localization.IsRightToLeft);
        if (dialog.ShowDialog() != true) return;

        CancelShortcutCapture();
        bool selectionChanged = ChangeHotkey(SelectionHotkey.SelectionId, HotkeyBinding.SelectionDefault, true);
        bool toggleChanged = ChangeHotkey(SelectionHotkey.ToggleId, HotkeyBinding.ToggleDefault, false);
        // A current toggle binding can temporarily occupy the selection default.
        if (!selectionChanged && toggleChanged)
            selectionChanged = ChangeHotkey(SelectionHotkey.SelectionId, HotkeyBinding.SelectionDefault, true);

        initializing = true;
        AutomaticToggle.IsChecked = false;
        ConversionMode.SelectedValue = SelectionDirection.Auto;
        CloseToTrayCheckBox.IsChecked = true;
        StartupManager.SetEnabled(false);
        StartWithWindowsCheckBox.IsChecked = false;
        settings.StartWithWindows = false;
        settings.Theme = AppTheme.Dark.ToString();
        settings.GitHubUrl = "https://github.com/";
        settings.CheckUpdatesOnStartup = true;
        settings.UpdateManifestUrl = "";
        settings.LastUpdateCheckUtc = null;
        settings.CheckDictionaryUpdatesOnStartup = true;
        settings.DictionaryManifestUrl = "";
        settings.LastDictionaryUpdateCheckUtc = null;
        AppearanceManager.Apply(AppTheme.Dark);
        ApplyFlowDirection();
        ApplyLocalizedChoices();
        GitHubUrlTextBox.Text = settings.GitHubUrl;
        CheckUpdatesOnStartupCheckBox.IsChecked = true;
        UpdateManifestUrlTextBox.Text = "";
        CheckDictionaryUpdatesOnStartupCheckBox.IsChecked = true;
        DictionaryManifestUrlTextBox.Text = "";
        RefreshLocalizedUpdateSummaries(packageRemainsInstalled: true);
        OpenUpdateButton.Visibility = Visibility.Collapsed;
        PreviewOutput.Text = localization["PreviewEmpty"];
        initializing = false;
        settings.Save();

        RestoreShortcutText();
        if (selectionChanged && toggleChanged)
        {
            CrashLogger.Success("Settings restored to defaults");
            SetStatus(localization["RestoreSuccess"], StatusTone.Success);
        }
        else
        {
            SetStatus(localization["RestoreShortcutWarning"], StatusTone.Warning);
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e) => OpenLogs();

    private void OpenCustomDataButton_Click(object sender, RoutedEventArgs e)
    {
        try { CustomDataManager.OpenFolder(); SetStatus(localization["CustomDataOpened"], StatusTone.Success); }
        catch (Exception exception) { CrashLogger.Record("Open custom dictionary folder", exception); SetStatus(exception.Message, StatusTone.Error); }
    }

    private void ReloadDataButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            engine.Reload();
            CrashLogger.Success("Dictionaries reloaded manually");
            SetStatus(localization["DictionariesReloaded"], StatusTone.Success);
        }
        catch (Exception exception)
        {
            CrashLogger.Record("Reload dictionaries", exception);
            SetStatus("Dictionaries could not be reloaded. The previous data remains active.", StatusTone.Error);
        }
    }

    private void CheckDictionaryUpdatesOnStartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (settings is null) return;
        settings.CheckDictionaryUpdatesOnStartup = CheckDictionaryUpdatesOnStartupCheckBox.IsChecked == true;
        if (!initializing) settings.Save();
    }

    private void DictionaryManifestUrlTextBox_LostFocus(object sender, RoutedEventArgs e) => SaveDictionaryManifestUrl();

    private bool SaveDictionaryManifestUrl()
    {
        string value = DictionaryManifestUrlTextBox.Text.Trim();
        if (value.Length == 0)
        {
            settings.DictionaryManifestUrl = "";
            settings.Save();
            DictionaryUpdateStatusText.Text = "Add the community dictionary manifest URL after publishing your GitHub repository.";
            return true;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            DictionaryManifestUrlTextBox.Text = settings.DictionaryManifestUrl;
            SetStatus("The dictionary manifest must use a complete HTTPS URL.", StatusTone.Warning);
            return false;
        }
        settings.DictionaryManifestUrl = uri.AbsoluteUri;
        settings.Save();
        return true;
    }

    private async void CheckDictionaryUpdatesButton_Click(object sender, RoutedEventArgs e) =>
        await CheckDictionaryUpdatesAsync(manual: true);

    private async Task CheckDictionaryUpdatesAsync(bool manual)
    {
        if (dictionaryUpdateCheckInProgress || !SaveDictionaryManifestUrl()) return;
        if (string.IsNullOrWhiteSpace(settings.DictionaryManifestUrl))
        {
            DictionaryUpdateStatusText.Text = "Add the dictionary manifest URL first. You can provide it after publishing GitHub.";
            if (manual) SetStatus("No dictionary update URL is configured yet.", StatusTone.Neutral);
            return;
        }

        dictionaryUpdateCheckInProgress = true;
        DictionaryUpdateStatusText.Text = "Checking the approved community dictionary feed…";
        try
        {
            DictionaryUpdateResult result = await DictionaryUpdateService.CheckAndInstallAsync(
                settings.DictionaryManifestUrl,
                settings.InstalledDictionaryVersion);
            settings.LastDictionaryUpdateCheckUtc = DateTimeOffset.UtcNow;

            if (result.State == DictionaryUpdateState.Installed)
            {
                try
                {
                    engine.Reload();
                }
                catch
                {
                    if (CommunityDataManager.RestorePrevious()) engine.Reload();
                    throw;
                }
                settings.InstalledDictionaryVersion = result.DataVersion;
                DictionaryUpdateStatusText.Text = $"Community dictionary {result.DataVersion} installed locally ({result.Files} data files).";
                CrashLogger.Success($"Dictionaries reloaded after community update | version={result.DataVersion}");
                SetStatus("Community words were updated and are ready to use.", StatusTone.Success);
            }
            else
            {
                DictionaryUpdateStatusText.Text = $"Community dictionary {result.DataVersion} is up to date.";
                if (manual) SetStatus("Community dictionaries are up to date.", StatusTone.Success);
            }
            settings.Save();
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or
                                           InvalidDataException or IOException or UnauthorizedAccessException or
                                           ArgumentException or System.Text.Json.JsonException)
        {
            DictionaryUpdateStatusText.Text = "Dictionary update failed. Your current local dictionaries were kept.";
            CrashLogger.Warning($"Dictionary update failed | type={exception.GetType().Name} | message={exception.Message}");
            if (manual) SetStatus("Could not update community words. See the diagnostic log.", StatusTone.Warning);
        }
        finally { dictionaryUpdateCheckInProgress = false; }
    }

    private void CheckUpdatesOnStartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (settings is null) return;
        settings.CheckUpdatesOnStartup = CheckUpdatesOnStartupCheckBox.IsChecked == true;
        if (!initializing) settings.Save();
    }

    private void UpdateManifestUrlTextBox_LostFocus(object sender, RoutedEventArgs e) => SaveUpdateManifestUrl();

    private bool SaveUpdateManifestUrl()
    {
        string value = UpdateManifestUrlTextBox.Text.Trim();
        if (value.Length == 0)
        {
            settings.UpdateManifestUrl = "";
            settings.Save();
            UpdateStatusText.Text = "Add the update manifest URL when it is ready.";
            return true;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            UpdateManifestUrlTextBox.Text = settings.UpdateManifestUrl;
            SetStatus("The update manifest must use a complete HTTPS URL.", StatusTone.Warning);
            return false;
        }
        settings.UpdateManifestUrl = uri.AbsoluteUri;
        settings.Save();
        return true;
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (updateCheckInProgress || !SaveUpdateManifestUrl()) return;
        if (string.IsNullOrWhiteSpace(settings.UpdateManifestUrl))
        {
            UpdateStatusText.Text = "Add the manifest URL first. You can provide it later.";
            if (manual) SetStatus("No update URL is configured yet.", StatusTone.Neutral);
            return;
        }

        updateCheckInProgress = true;
        UpdateStatusText.Text = "Checking for updates…";
        OpenUpdateButton.Visibility = Visibility.Collapsed;
        try
        {
            UpdateCheckResult result = await UpdateChecker.CheckAsync(settings.UpdateManifestUrl);
            settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            settings.Save();
            if (result.State == UpdateState.Available)
            {
                availableUpdateUrl = result.DownloadUrl ?? result.ReleaseNotesUrl;
                UpdateStatusText.Text = $"Version {result.LatestVersion} is available (installed: {result.CurrentVersion}).";
                OpenUpdateButton.Visibility = availableUpdateUrl is null ? Visibility.Collapsed : Visibility.Visible;
                CrashLogger.Success($"Update available | current={result.CurrentVersion} | latest={result.LatestVersion}");
                SetStatus("A Klavrio update is available.", StatusTone.Success);
            }
            else
            {
                availableUpdateUrl = null;
                UpdateStatusText.Text = $"Klavrio is up to date ({result.CurrentVersion}).";
                CrashLogger.Success($"Update check completed | current={result.CurrentVersion} | latest={result.LatestVersion}");
                if (manual) SetStatus("Klavrio is up to date.", StatusTone.Success);
            }
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
        {
            UpdateStatusText.Text = "Update check failed. Your app continues to work normally.";
            CrashLogger.Warning($"Update check failed | type={exception.GetType().Name} | message={exception.Message}");
            if (manual) SetStatus("Could not check for updates. See the diagnostic log.", StatusTone.Warning);
        }
        finally { updateCheckInProgress = false; }
    }

    private void OpenUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdateUrl is null) return;
        try { Process.Start(new ProcessStartInfo(availableUpdateUrl) { UseShellExecute = true }); }
        catch (Exception exception) { CrashLogger.Record("Open update URL", exception); SetStatus("The update page could not be opened.", StatusTone.Error); }
    }

    private void GitHubUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string value = GitHubUrlTextBox.Text.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http")
        {
            settings.GitHubUrl = value;
            settings.Save();
        }
        else
        {
            GitHubUrlTextBox.Text = settings.GitHubUrl;
            SetStatus("Enter a complete http:// or https:// project URL.", StatusTone.Warning);
        }
    }

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        GitHubUrlTextBox_LostFocus(GitHubUrlTextBox, e);
        try
        {
            Process.Start(new ProcessStartInfo(settings.GitHubUrl) { UseShellExecute = true });
            CrashLogger.Success("Project link opened");
            SetStatus("Opened the project link.", StatusTone.Success);
        }
        catch (Exception exception)
        {
            CrashLogger.Record("Open project link", exception);
            SetStatus("The project link could not be opened.", StatusTone.Error);
        }
    }

    private void CopyLogPathButton_Click(object sender, RoutedEventArgs e)
    {
        try { Forms.Clipboard.SetText(CrashLogger.LogPath); SetStatus(localization["LogPathCopied"], StatusTone.Success); }
        catch (Exception exception) { CrashLogger.Record("Copy log path", exception); SetStatus(exception.Message, StatusTone.Error); }
    }

    private void OpenLogs()
    {
        try { CrashLogger.OpenFolder(); SetStatus(localization["LogsOpened"], StatusTone.Success); }
        catch (Exception exception) { CrashLogger.Record("Open logs", exception); SetStatus(exception.Message, StatusTone.Error); }
    }

    private void UpdateActiveState()
    {
        bool active = AutomaticToggle.IsChecked == true;
        StateText.Text = active ? localization["Active"] : localization["Standby"];
        StateDot.Fill = FindBrush(active ? "Klavrio.SuccessBrush" : "Klavrio.MutedBrush");
        StateText.Foreground = StateDot.Fill;
        StatePill.Background = active ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 69, 61)) : FindBrush("Klavrio.SurfaceBrush");
        AutomaticCaption.Text = active ? localization["On"] : localization["Off"];
        AutomaticCaption.Foreground = StateDot.Fill;
        tray.Text = active ? "Klavrio — automatic correction on" : "Klavrio — standby";
    }

    private void SetStatus(string message, StatusTone tone)
    {
        StatusText.Text = message;
        string brushKey = tone switch
        {
            StatusTone.Success => "Klavrio.SuccessBrush",
            StatusTone.Warning => "Klavrio.WarningBrush",
            StatusTone.Error => "Klavrio.ErrorBrush",
            _ => "Klavrio.MutedBrush"
        };
        StatusText.Foreground = FindBrush(brushKey);
        StatusIcon.Foreground = StatusText.Foreground;
        StatusIcon.Kind = tone switch
        {
            StatusTone.Success => PackIconKind.CheckCircleOutline,
            StatusTone.Warning => PackIconKind.AlertOutline,
            StatusTone.Error => PackIconKind.CloseCircleOutline,
            _ => PackIconKind.InformationOutline
        };
    }

    private System.Windows.Media.Brush FindBrush(string key) =>
        (TryFindResource(key) as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.White;

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        exiting = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CancelShortcutCapture();
        if (!exiting && CloseToTrayCheckBox.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            if (!trayHintShown)
            {
                tray.ShowBalloonTip(2500, "Klavrio is still running", "Use the tray icon to reopen it or exit.", Forms.ToolTipIcon.Info);
                trayHintShown = true;
            }
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        UnregisterHotkeys();
        windowSource?.RemoveHook(WndProc);
        monitor.Dispose();
        tray.Visible = false;
        tray.Dispose();
        settings.Save();
        System.Windows.Application.Current.Shutdown();
    }

    private void UnregisterHotkeys()
    {
        if (windowHandle == 0) return;
        if (registeredSelection is not null) SelectionHotkey.Unregister(windowHandle, SelectionHotkey.SelectionId);
        if (registeredToggle is not null) SelectionHotkey.Unregister(windowHandle, SelectionHotkey.ToggleId);
        registeredSelection = registeredToggle = null;
    }

    private enum StatusTone { Neutral, Success, Warning, Error }
}
