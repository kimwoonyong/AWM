namespace TextBean.Models;

/// <summary>
/// 클립보드로 보낼 것 (D-126). 글자는 늘 있다 — 다른 프로그램은 이것만 받는다.
/// Rtf 는 워드 같은 밖의 프로그램용, XamlPackage 는 앱 안 붙여넣기용 서식이다. 서식 없는 복사는 둘 다 null.
/// </summary>
public sealed record ClipboardPayload(string Text, string? Rtf = null, byte[]? XamlPackage = null);
