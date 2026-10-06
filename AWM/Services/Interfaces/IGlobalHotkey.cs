namespace AWM.Services.Interfaces;

/// <summary>
/// 다른 창(크롬)에 있을 때도 받는 단축키. 켜 둔 동안만 등록한다 (D-007).
/// </summary>
public interface IGlobalHotkey
{
    /// <summary>
    /// 사람이 읽는 키 이름(예: Ctrl+Alt+V).
    /// </summary>
    string Gesture { get; }

    bool IsArmed { get; }

    /// <summary>
    /// 지정한 시간 동안 켠다. 다른 프로그램이 이미 쓰는 단축키라 등록하지 못하면 false.
    /// </summary>
    bool Arm(TimeSpan duration);

    void Disarm();

    event EventHandler? Pressed;

    /// <summary>
    /// 켜 둔 시간이 지나 저절로 꺼졌다.
    /// </summary>
    event EventHandler? Expired;
}
