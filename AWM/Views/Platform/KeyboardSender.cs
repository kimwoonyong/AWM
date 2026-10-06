using System.Diagnostics;
using System.Runtime.InteropServices;
using AWM.Services.Interfaces;

namespace AWM.Views.Platform;

/// <summary>
/// 키 입력 P/Invoke 는 이 파일과 GlobalHotkey 에만 둔다 (PROHIBITED-CUSTOM-08).
/// </summary>
public sealed class KeyboardSender : IKeyboardSender
{
    private const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkEscape = 0x1B, VkV = 0x56, VkLWin = 0x5B, VkRWin = 0x5C;
    private const uint InputKeyboard = 1, KeyEventKeyUp = 0x0002;

    public IntPtr ForegroundWindow() => GetForegroundWindow();

    public string? ProcessNameOf(IntPtr window)
    {
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var processId) == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // 그새 끝난 프로세스
            return null;
        }
    }

    public bool AreModifiersDown() =>
        IsDown(VkShift) || IsDown(VkControl) || IsDown(VkMenu) || IsDown(VkLWin) || IsDown(VkRWin);

    public bool IsEscapeDown() => IsDown(VkEscape);

    public void SendPaste()
    {
        INPUT[] inputs =
        [
            Key(VkControl, down: true), Key(VkV, down: true), Key(VkV, down: false), Key(VkControl, down: false),
        ];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new InvalidOperationException($"키 입력을 보내지 못했습니다 (오류 {Marshal.GetLastPInvokeError()}).");
    }

    private static bool IsDown(ushort key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static INPUT Key(ushort key, bool down) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = down ? 0 : KeyEventKeyUp } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    // 공용체 크기는 가장 큰 MOUSEINPUT 에 맞아야 SendInput 이 받는다
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] INPUT[] inputs, int size);
}
