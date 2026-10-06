namespace AWM.Services.Interfaces;

/// <summary>
/// 앞 창 확인과 붙여넣기 키. 구현(SendInput 등)은 Views/Platform/KeyboardSender 한 곳 (PROHIBITED-CUSTOM-08).
/// </summary>
public interface IKeyboardSender
{
    IntPtr ForegroundWindow();

    /// <summary>
    /// 창을 가진 프로세스 이름(확장자 없이, 예: chrome). 알 수 없으면 null.
    /// </summary>
    string? ProcessNameOf(IntPtr window);

    /// <summary>
    /// Ctrl·Alt·Shift·Win 중 하나라도 눌려 있는가 — 단축키를 누른 손이 아직 떨어지지 않았는가.
    /// </summary>
    bool AreModifiersDown();

    bool IsEscapeDown();

    /// <summary>
    /// 앞 창에 Ctrl+V 를 보낸다.
    /// </summary>
    void SendPaste();
}
