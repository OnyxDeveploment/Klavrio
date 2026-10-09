using System.Runtime.InteropServices;
using System.Text;

namespace Klavrio;

internal static class WinApi
{
    public const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    public const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    public const uint INPUT_KEYBOARD = 1, LLKHF_INJECTED = 0x10;
    public const int VK_BACK = 0x08, VK_SPACE = 0x20;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLowLevel
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort vk;
        public ushort scan;
        public uint flags;
        public uint time;
        public nuint extraInfo;
    }

    // INPUTs union MUST accommodate MOUSEINPUT, even for keyboard only injection.
    // sizeof(INPUT) is 40 on x64 and 28 on x86. An undersized union makes SendInput fail with ERROR_INVALID_PARAMETER.
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint extraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput ki;
        [FieldOffset(0)] public MouseInput mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint cbSize, flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public Rect rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wp, nint lp);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern int ToUnicodeEx(uint vk, uint scan, byte[] keyState, [Out] char[] buffer, int length, uint flags, nint layout);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder className, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern nint GetModuleHandle(string? name);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    public static void TrySetDarkTitleBar(nint hwnd, bool dark)
    {
        try
        {
            int enabled = dark ? 1 : 0;
            // Attribute 20 is supported by current Windows 10/11 builds; 19 covers older Windows 10.
            if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
    public static bool IsLikelyPasswordField(nint foreground)
    {
        var thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(thread, ref info) || info.hwndFocus == 0) return false;
        var name = new StringBuilder(128);
        GetClassName(info.hwndFocus, name, name.Capacity);
        if (name.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase))
        {
            return SendMessageTimeout(info.hwndFocus, 0x00D2, 0, 0, 0x0002, 30, out var passwordChar) != 0 && passwordChar != 0;
        }
        return false; // Browser/custom controls cannot be reliably identified here.
    }
    public static Input Key(ushort value, bool up = false, bool unicode = false) => new()
    {
        type = INPUT_KEYBOARD,
        data = new InputUnion { ki = new KeyboardInput { vk = unicode ? (ushort)0 : value, scan = unicode ? value : (ushort)0, flags = (up ? KEYEVENTF_KEYUP : 0) | (unicode ? KEYEVENTF_UNICODE : 0) } }
    };
    /// <summary>Send a modifier shortcut, without invoking the normal word-correction path.</summary>
    public static bool CopySelection()
    {
        const ushort VK_CONTROL = 0x11, VK_C = 0x43;
        Input[] inputs = { Key(VK_CONTROL), Key(VK_C), Key(VK_C, up: true), Key(VK_CONTROL, up: true) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }
    /// <summary>Replace the current selection with Unicode keyboard events. No clipboard paste.</summary>
    public static bool WriteSelection(string value)
    {
        if (value.Length > 12000)
            return false;

        var inputs = new Input[value.Length * 2];
        for (int i = 0; i < value.Length; i++)
        {
            inputs[2 * i] = Key(value[i], unicode: true);
            inputs[2 * i + 1] = Key(value[i], up: true, unicode: true);
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }
    public static bool Replace(string original, string replacement)
    {
        var seq = new List<Input>();
        foreach (char _ in original)
        {
            seq.Add(Key(VK_BACK));
            seq.Add(Key(VK_BACK, up: true));
        }

        foreach (char c in replacement + " ")
        {
            seq.Add(Key(c, unicode: true));
            seq.Add(Key(c, up: true, unicode: true));
        }

        var inputs = seq.ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            CrashLogger.Warning($"SendInput incomplete | inserted={sent} | requested={inputs.Length} | win32Error={Marshal.GetLastWin32Error()}");
            return false;
        }
        return true;
    }
    public static void ValidateInterop()
    {
        int expected = IntPtr.Size == 8 ? 40 : 28;
        int actual = Marshal.SizeOf<Input>();
        if (actual != expected)
            throw new InvalidOperationException($"Native INPUT size mismatch: {actual}; expected {expected}");

        int hookSize = Marshal.SizeOf<KbdLowLevel>();
        int expectedHookSize = IntPtr.Size == 8 ? 24 : 20;
        if (hookSize != expectedHookSize)
            throw new InvalidOperationException($"KBDLLHOOKSTRUCT size mismatch: {hookSize}");

        CrashLogger.Info($"Native interop verified | inputSize={actual} | hookSize={hookSize} | pointerBytes={IntPtr.Size}");
    }
}
