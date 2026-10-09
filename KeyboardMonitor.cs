using System.Runtime.InteropServices;

namespace Klavrio;

/// <summary>
/// Global low-level hook. No disk I/O, injection or UI work happens inside the callback.
/// Only a short, bounded word is buffered. Corrections run on the WinForms message loop.
/// </summary>
public sealed class KeyboardMonitor : IDisposable
{
    private readonly CorrectionEngine engine;
    private readonly WinApi.HookProc callback;
    private readonly Action<Action> postToUi;
    private nint hook, foreground;
    private string word = "";
    private readonly byte[] keyState = new byte[256];
    private readonly char[] unicodeBuffer = new char[4];
    private volatile bool enabled;
    private volatile bool injecting;
    private bool disposed;
    private int generation;
    public Action<string>? Status { get; set; }

    public KeyboardMonitor(CorrectionEngine engine, Action<Action> postToUi)
    {
        this.engine = engine;
        this.postToUi = postToUi;
        callback = Callback; // Keep delegate alive while native hook is installed.
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            word = "";
            generation++; // Invalidate pending corrections after toggles.
            CrashLogger.Info(value ? "Monitoring enabled" : "Monitoring disabled");
        }
    }

    public bool Start()
    {
        if (disposed) return false;
        hook = WinApi.SetWindowsHookEx(WinApi.WH_KEYBOARD_LL, callback, WinApi.GetModuleHandle(null), 0);
        if (hook == 0)
            CrashLogger.Record("SetWindowsHookEx", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        else CrashLogger.Info("Keyboard hook registered");
        return hook != 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        enabled = false;
        generation++;
        if (hook != 0 && !WinApi.UnhookWindowsHookEx(hook))
            CrashLogger.Record("UnhookWindowsHookEx", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        hook = 0;
    }

    private nint Next(int code, nint wp, nint lp) => WinApi.CallNextHookEx(hook, code, wp, lp);
    private nint Callback(int code, nint wp, nint lp)
    {
        try { return ProcessKey(code, wp, lp); }
        catch (Exception e)
        {
            word = "";
            // Hook failures are rare; diagnostics are best effort.
            CrashLogger.Record("Keyboard hook", e);
            return Next(code, wp, lp);
        }
    }

    private nint ProcessKey(int code, nint wp, nint lp)
    {
        // Disabled path requires zero marshaling or Win32 lookups.
        if (!enabled || disposed || injecting || code < 0 ||
            (wp != WinApi.WM_KEYDOWN && wp != WinApi.WM_SYSKEYDOWN))
            return Next(code, wp, lp);

        var evt = Marshal.PtrToStructure<WinApi.KbdLowLevel>(lp);
        if ((evt.flags & WinApi.LLKHF_INJECTED) != 0) return Next(code, wp, lp);

        int vk = (int)evt.vkCode;
        // Clearing on modifiers avoids Ctrl shortcuts and accidental replacements.
        if (vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
        { word = ""; return Next(code, wp, lp); }
        if ((WinApi.GetAsyncKeyState(0x11) & 0x8000) != 0 ||
            (WinApi.GetAsyncKeyState(0x12) & 0x8000) != 0 ||
            (WinApi.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
            (WinApi.GetAsyncKeyState(0x5C) & 0x8000) != 0)
        { word = ""; return Next(code, wp, lp); }

        nint hwnd = WinApi.GetForegroundWindow();
        if (hwnd == 0) { word = ""; return Next(code, wp, lp); }
        if (foreground != hwnd) { foreground = hwnd; word = ""; generation++; }

        if (vk == WinApi.VK_BACK)
        {
            if (word.Length > 0) word = word[..^1];
            return Next(code, wp, lp);
        }
        if (vk == WinApi.VK_SPACE)
        {
            string original = word;
            word = "";
            if (original.Length < engine.MinimumLength) return Next(code, wp, lp);
            string? replacement = engine.Suggest(original);
            if (replacement is null) return Next(code, wp, lp);
            // Only do potentially blocking focus/password checks for actual candidates.
            if (WinApi.IsLikelyPasswordField(hwnd)) return Next(code, wp, lp);
            int capturedGeneration = generation;
            try
            {
                // Suppress the physical space. The UI action injects backspaces and replacement + space.
                postToUi(() => Correct(hwnd, original, replacement, capturedGeneration));
                return 1;
            }
            catch (Exception e)
            {
                CrashLogger.Record("Queue correction", e);
                return Next(code, wp, lp); // Do not swallow Space if scheduling failed.
            }
        }
        if (vk is 0x0D or 0x09 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2E or 0x1B or 0x24 or 0x23)
        { word = ""; return Next(code, wp, lp); }

        // Only translate plausible character keys; avoid GetKeyboardState on all other keys.
        if (!((vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39) ||
              vk is 0xBA or 0xBB or 0xBC or 0xBD or 0xBE or 0xBF or 0xC0 or 0xDB or 0xDC or 0xDD or 0xDE))
        { word = ""; return Next(code, wp, lp); }

        if (!WinApi.GetKeyboardState(keyState)) { word = ""; return Next(code, wp, lp); }
        uint thread = WinApi.GetWindowThreadProcessId(hwnd, out _);
        int count = WinApi.ToUnicodeEx(evt.vkCode, evt.scanCode, keyState, unicodeBuffer, unicodeBuffer.Length, 0x04, WinApi.GetKeyboardLayout(thread));
        if (count == 1 && CorrectionEngine.IsWordLetter(unicodeBuffer[0]) && word.Length < 32)
            word += unicodeBuffer[0];
        else word = "";
        return Next(code, wp, lp);
    }

    private void Correct(nint hwnd, string original, string replacement, int expectedGeneration)
    {
        if (!enabled || disposed || injecting || generation != expectedGeneration || hwnd != WinApi.GetForegroundWindow())
            return;
        // An application may have changed focus without changing top-level HWND.
        if (WinApi.IsLikelyPasswordField(hwnd)) return;
        injecting = true;
        try
        {
            if (WinApi.Replace(original, replacement))
            {
                CrashLogger.Success("Automatic correction completed");
                Status?.Invoke("Last word corrected successfully.");
            }
            else
            {
                CrashLogger.Warning("Automatic correction insertion incomplete");
                Status?.Invoke("Automatic correction could not be completed.");
            }
        }
        catch (Exception e) { CrashLogger.Record("Deferred correction", e); }
        finally { injecting = false; }
    }
}
