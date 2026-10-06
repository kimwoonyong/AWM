using System.Globalization;
using System.Text;

namespace TextBean.Services;

public enum KeyRuleResult
{
    Ok,
    Empty,
    TooShort,
    EdgeWhitespace,
    ContainsHangul
}

/// <summary>
/// 키 규칙. 판정은 NFC 로 정규화한 뒤의 문자열로 한다 — 붙여넣은 조합형 글자는 정규화 전후 길이가 다르고,
/// 같은 키라도 NFC 와 NFD 는 다른 키가 된다 [실측].
/// 화면은 새 키를 정할 때도, 있는 키를 넣을 때도 <see cref="CheckNew"/> 로 본다 — 이 앱은 규칙을 못 넘는 키로
/// 문서를 잠그지 않아, 그런 키로 열리는 문서는 없다. 받아 주면 아무것도 안 열리는 키가 조용히 켜진다
/// (사용자 확인 2026-09-30 — 1234 가 받아들여졌다). <see cref="CheckEntered"/> 는 키 서비스의 최소 검사다.
/// </summary>
public static class KeyRules
{
    /// 사용자 결정 D-049. 파일이 새면 짧은 키는 분 단위로 풀린다 [실측 — 4자리 약 83초].
    public const int MinimumLength = 8;

    public static string Normalize(string key) => key.Normalize(NormalizationForm.FormC);

    public static KeyRuleResult CheckEntered(string normalized)
        => string.IsNullOrEmpty(normalized) ? KeyRuleResult.Empty : KeyRuleResult.Ok;

    public static KeyRuleResult CheckNew(string normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return KeyRuleResult.Empty;

        // 앞뒤 공백은 다른 PC 에서 다시 칠 때 빠뜨리기 쉽다. 빠뜨리면 다른 키가 된다.
        if (char.IsWhiteSpace(normalized[0]) || char.IsWhiteSpace(normalized[^1])) return KeyRuleResult.EdgeWhitespace;

        // 비밀번호 입력칸(PasswordBox)은 한글 입력기를 강제로 끈다 [실측]. 붙여넣어 만든 한글 키는
        // 다른 PC 에서 칠 수 없어, 붙여넣기 없이는 영영 못 연다 (D-051).
        foreach (var c in normalized)
        {
            if (IsHangul(c)) return KeyRuleResult.ContainsHangul;
        }

        return new StringInfo(normalized).LengthInTextElements < MinimumLength
            ? KeyRuleResult.TooShort
            : KeyRuleResult.Ok;
    }

    public static string MessageFor(KeyRuleResult result) => result switch
    {
        KeyRuleResult.Empty => "키를 입력하세요.",
        KeyRuleResult.TooShort => $"키는 {MinimumLength}자 이상이어야 합니다. 짧은 키는 파일이 새면 몇 분 안에 풀립니다.",
        KeyRuleResult.EdgeWhitespace => "키의 처음과 끝에는 공백을 둘 수 없습니다.",
        KeyRuleResult.ContainsHangul => "키에는 한글을 쓸 수 없습니다. 영문·숫자·기호로 정하세요.",
        _ => ""
    };

    /// 있는 키를 넣을 때. 새로 정하라는 말이 아니라, 그런 키로 잠긴 문서가 없다는 말이다.
    public static string MessageForEntered(KeyRuleResult result) => result switch
    {
        KeyRuleResult.TooShort => $"키는 {MinimumLength}자 이상입니다. 이 앱은 그보다 짧은 키로 문서를 잠그지 않아, 열리는 문서가 없습니다.",
        KeyRuleResult.EdgeWhitespace => "키의 처음과 끝에는 공백이 들어가지 않습니다. 앞뒤 공백을 지우고 다시 입력하세요.",
        KeyRuleResult.ContainsHangul => "키에는 한글이 들어가지 않습니다. 한/영 전환을 확인하세요.",
        _ => MessageFor(result)
    };

    private static bool IsHangul(char c)
        => c is >= '가' and <= '힣'      // 음절
            or >= 'ᄀ' and <= 'ᇿ'       // 자모
            or >= '㄰' and <= '㆏'       // 호환 자모
            or >= 'ꥠ' and <= '꥿'       // 확장 A
            or >= 'ힰ' and <= '퟿';      // 확장 B
}
