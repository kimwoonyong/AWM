namespace TextBean.Models;

/// <summary>
/// 금고 문서 본문 (D-123). Text 는 검색 · 찾기용 글자, Rich 는 서식 문서(XamlPackage).
/// Rich 가 비면 서식 없는 문서다 — 화면은 Text 를 문단으로 나눠 보인다(새 빈 문서 · 시험).
/// 코덱 · 저장소는 WPF 를 모른다 — 두 값을 바이트 · 문자열로만 나른다 (PROHIBITED-ARCH-02).
/// </summary>
public sealed record DocumentBody(string Text, byte[] Rich)
{
    public static DocumentBody Empty { get; } = new("", []);

    public static DocumentBody Plain(string text) => new(text, []);

    public bool HasRich => Rich.Length > 0;
}
