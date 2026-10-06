using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AWM.Services.Interfaces;

namespace AWM.Views.Platform;

/// <summary>
/// Win32 RegisterHotKey 로 다른 창에서도 받는다. 켜 둔 동안만 등록해 다른 프로그램의 같은 키를 오래 막지 않는다 (D-007).
/// </summary>
public sealed class GlobalHotkey : IGlobalHotkey, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA11;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000;
    private const uint VkV = 0x56;

    private readonly IntPtr _window;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _expiry = new();

    public GlobalHotkey(Window window)
    {
        _window = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_window);
        _source.AddHook(OnMessage);
        _expiry.Tick += (_, _) =>
        {
            Disarm();
            Expired?.Invoke(this, EventArgs.Empty);
        };
    }

    // Ctrl+Shift+V 는 크롬 「서식 없이 붙여넣기」와 겹친다 (D-006)
    public string Gesture => "Ctrl+Alt+V";

    public bool IsArmed { get; private set; }

    public event EventHandler? Pressed;

    public event EventHandler? Expired;

    public bool Arm(TimeSpan duration)
    {
        if (!IsArmed)
        {
            if (!RegisterHotKey(_window, HotkeyId, ModControl | ModAlt | ModNoRepeat, VkV))
                return false;
            IsArmed = true;
        }
        _expiry.Stop();
        _expiry.Interval = duration;
        _expiry.Start();
        return true;
    }

    public void Disarm()
    {
        _expiry.Stop();
        if (!IsArmed)
            return;
        UnregisterHotKey(_window, HotkeyId);
        IsArmed = false;
    }

    public void Dispose()
    {
        Disarm();
        _source.RemoveHook(OnMessage);
    }

    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
