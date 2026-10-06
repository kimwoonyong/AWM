namespace TextBean.Services;

public enum KeyUnavailableReason
{
    /// 키를 아직 넣지 않았다.
    NoKey,

    /// 문서를 연 뒤 키가 바뀌었다(전환·잠그기). 옛 키로 연 탭은 새 키로 쓰지 않는다.
    KeyChanged,

    /// 디스크의 파일이 지금 키로 잠긴 것이 아니다 — 다른 키 · 옛 방식 · 손상.
    FileLockedWithOtherKey,

    /// 새 문서를 만들려는데 지금 키가 새 키 규칙(8자 이상 등)을 통과하지 못한다.
    WeakKeyForNewDocument
}

/// <summary>
/// 암호화에 쓸 키가 없어 멈췄다. 조용히 다른 키로 쓰는 대신 이것을 던진다 —
/// 저장이 실패하는 것은 알 수 있지만, 다른 키로 다시 잠기는 것은 사용자가 알 길이 없다.
/// 메시지는 대화상자에 그대로 보인다. 문서 내용과 키는 넣지 않는다 (PROHIBITED-CUSTOM-04).
/// </summary>
public sealed class KeyUnavailableException(KeyUnavailableReason reason) : InvalidOperationException(MessageFor(reason))
{
    public KeyUnavailableReason Reason { get; } = reason;

    private static string MessageFor(KeyUnavailableReason reason) => reason switch
    {
        KeyUnavailableReason.NoKey => "키를 넣지 않아 저장할 수 없습니다. '키 입력'으로 키를 넣으세요.",
        KeyUnavailableReason.KeyChanged =>
            "이 문서를 연 뒤 키가 바뀌어 저장하지 않았습니다. 다른 키로 다시 잠그지 않기 위해서입니다.",
        KeyUnavailableReason.FileLockedWithOtherKey =>
            "이 파일은 지금 키로 잠긴 문서가 아니어서 덮어쓰지 않았습니다. 밖에서 다른 파일로 바뀌었을 수 있습니다.",
        KeyUnavailableReason.WeakKeyForNewDocument =>
            $"지금 키는 새 문서에 쓸 수 없습니다. 새 문서의 키는 {KeyRules.MinimumLength}자 이상이어야 합니다.",
        _ => "키를 쓸 수 없습니다."
    };
}
