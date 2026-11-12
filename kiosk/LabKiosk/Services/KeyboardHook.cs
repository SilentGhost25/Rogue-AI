using System.Runtime.InteropServices;

namespace LabKiosk.Services;

/// <summary>
/// Low-level Windows keyboard hook (WH_KEYBOARD_LL) blocking task-switching shortcuts while locked,
/// with a secret organizer override chord (Ctrl+Alt+Shift+H).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint LLKHF_ALTDOWN = 0x20;

    private const uint VK_TAB = 0x09;
    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;
    private const uint VK_F4 = 0x73;
    private const uint VK_H = 0x48;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId;

    public bool IsLocked { get; set; } = true;
    public event Action? OnHostComboPressed;

    public KeyboardHook()
    {
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                uint vkCode = info.vkCode;
                bool altPressed = (info.flags & LLKHF_ALTDOWN) != 0;
                bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool shiftPressed = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                // Secret organizer override/toggle: Ctrl + Alt + Shift + H
                if (altPressed && ctrlPressed && shiftPressed && vkCode == VK_H)
                {
                    OnHostComboPressed?.Invoke();
                    return (IntPtr)1;
                }

                if (IsLocked)
                {
                    // Block Alt+Tab, Alt+Esc, Alt+F4, Win keys, Ctrl+Esc, and Ctrl+Shift+Esc
                    if ((altPressed && (vkCode == VK_TAB || vkCode == VK_ESCAPE || vkCode == VK_F4)) ||
                        vkCode == VK_LWIN ||
                        vkCode == VK_RWIN ||
                        (ctrlPressed && vkCode == VK_ESCAPE))
                    {
                        return (IntPtr)1;
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hookId, IntPtr.Zero);
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}

