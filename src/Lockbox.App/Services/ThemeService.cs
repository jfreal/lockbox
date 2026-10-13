using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Lockbox.App.Services;

/// <summary>Follows the Windows light/dark app setting and paints the title bar to match.</summary>
public static class ThemeService
{
    private static readonly Uri LightUri = new("Theme/Colors.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Theme/Colors.Dark.xaml", UriKind.Relative);

    public static bool IsDark { get; private set; }

    /// <summary>Set by the snapshot tool to force a theme.</summary>
    public static bool? Override { get; set; }

    public static void Initialize()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                Application.Current.Dispatcher.BeginInvoke(Apply);
        };
    }

    public static void Apply()
    {
        IsDark = Override ?? SystemUsesDarkTheme();
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = IsDark ? DarkUri : LightUri };
        foreach (Window window in Application.Current.Windows)
            PaintTitleBar(window);
    }

    public static void PaintTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
            return;

        var dark = IsDark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (Application.Current.Resources["Color.WindowBg"] is Color bg)
        {
            var caption = bg.R | (bg.G << 8) | (bg.B << 16);
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        }

        var text = IsDark ? 0xF7F5F5 : 0x1F1D1D;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
