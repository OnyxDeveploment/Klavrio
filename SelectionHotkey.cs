using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Klavrio;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

public sealed record HotkeyBinding(HotkeyModifiers Modifiers, Keys Key)
{
    public static HotkeyBinding SelectionDefault { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.Space);
    public static HotkeyBinding ToggleDefault { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.Space);

    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
            parts.Add(Key switch
            {
                Keys.Space => "Space",
                Keys.Oemtilde => "`",
                Keys.Oemcomma => ",",
                Keys.OemPeriod => ".",
                _ => Key.ToString()
            });
            return string.Join(" + ", parts);
        }
    }

    public bool IsValid => Key is >= Keys.F1 and <= Keys.F24 ||
        (Key != Keys.None && (Modifiers & ~HotkeyModifiers.NoRepeat) != HotkeyModifiers.None);
}

internal static class SelectionHotkey
{
    internal const int Message = 0x0312;
    internal const int SelectionId = 0x4E53;
    internal const int ToggleId = 0x4E54;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    internal static void Register(nint hwnd, int id, HotkeyBinding binding)
    {
        uint modifiers = (uint)(binding.Modifiers | HotkeyModifiers.NoRepeat);
        if (!RegisterHotKey(hwnd, id, modifiers, (uint)binding.Key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{binding.DisplayText} is already in use. Try another shortcut.");
    }
    internal static void Unregister(nint hwnd, int id) => UnregisterHotKey(hwnd, id);
    internal static uint ClipboardVersion() => GetClipboardSequenceNumber();

    internal static bool IsPressed(HotkeyBinding binding)
    {
        static bool Down(int virtualKey) => (WinApi.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        if (Down((int)binding.Key)) return true;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control) && Down(0x11)) return true;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt) && Down(0x12)) return true;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift) && Down(0x10)) return true;
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Windows) && (Down(0x5B) || Down(0x5C))) return true;
        return false;
    }
}
