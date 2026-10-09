using MaterialDesignThemes.Wpf;

namespace Klavrio;

internal enum AppTheme { Dark, Light }

internal static class AppearanceManager
{
    internal static void Apply(AppTheme mode)
    {
        var palette = new PaletteHelper();
        Theme theme = palette.GetTheme();
        theme.SetBaseTheme(mode == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);
        palette.SetTheme(theme);

        bool dark = mode == AppTheme.Dark;
        SetBrush("Klavrio.WindowBrush", dark ? "#090E17" : "#F4F7FC");
        SetBrush("Klavrio.SurfaceBrush", dark ? "#121A28" : "#FFFFFFFF");
        SetBrush("Klavrio.SurfaceRaisedBrush", dark ? "#1A2536" : "#EDF2F9");
        SetBrush("Klavrio.SurfaceHoverBrush", dark ? "#223049" : "#E3EAF5");
        SetBrush("Klavrio.BorderBrush", dark ? "#2A3850" : "#D7DFEB");
        SetBrush("Klavrio.MutedBrush", dark ? "#A8B5C9" : "#58667B");
        SetBrush("Klavrio.AccentSoftBrush", dark ? "#253462" : "#E1E7FF");
        SetBrush("Klavrio.AccentGlowBrush", dark ? "#334FC4FF" : "#264B62FF");
        SetBrush("Klavrio.StatusBarBrush", dark ? "#101827" : "#EEF3FA");
    }

    private static void SetBrush(string key, string color) =>
        System.Windows.Application.Current.Resources[key] = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
