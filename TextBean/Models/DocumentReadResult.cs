namespace TextBean.Models;

public enum DocumentReadStatus
{
    Ok,
    NotTextBeanDocument,
    NewerVersion,

    /// 헤더는 멀쩡하고 키 확인값이 지금 키와 다르다. 정상 상태다 — 다른 키로 만든 문서.
    DifferentKey,

    /// 키를 아직 넣지 않았다.
    NoKey,

    /// 헤더 체크섬·반복 횟수 범위·인증 태그 중 하나가 어긋났다. 키 문제가 아니라 파일 문제다.
    /// "다른 키"와 합치면 사용자가 손상된 문서를 키 탓으로 알고 되돌릴 지점을 찾지 않는다.
    Corrupted,

    /// 옛 형식 문서 — TBX1 버전 1(Windows 계정) · 2(서식 이전). 열지도 덮어쓰지도 않는다 (D-073 · D-124).
    LegacyDpapi,

    ReadFailed,

    /// <summary>
    /// 평문 파일인데 문자 인코딩을 판정하지 못했다(이진 파일, BOM 없는 UTF-16 등).
    /// 일부러 실패로 둔다 — 빈 본문을 Ok 로 흘리면 검색이 "이 파일에는 없다"고 답하는데,
    /// 실제로는 읽지 못한 것이라 사용자가 없는 것을 없다고 믿게 된다.
    /// </summary>
    UndecodableText
}

/// <summary>
/// 읽기 결과. 예외 대신 결과 타입을 쓰는 이유는 호출자가 Status 를 반드시 마주하게 하기 위해서다 —
/// "실패했는데 계속 진행"이 구조적으로 어려워진다. 읽기에 실패한 문서의 저장 차단(D-005)이
/// 설계 전체에서 가장 중요한 안전장치라서 타입으로 강제한다.
/// </summary>
public sealed record DocumentReadResult(
    DocumentReadStatus Status,
    string? Text,
    string? Detail,
    DocumentKind Kind = DocumentKind.Encrypted,
    string? EncodingLabel = null,
    bool EncodingIsGuess = false,
    DocumentKeyBinding? Binding = null,
    PlainTextFormat? PlainFormat = null,
    byte[]? Rich = null)
{
    /// <summary>
    /// "읽기에 성공했다"만 뜻한다. "쓸 수 있다"는 Kind 가 정한다.
    /// 이 정의를 건드리면 읽기 실패 문서의 저장 차단(D-005)이 통째로 흔들린다 — 손대지 않는다.
    /// </summary>
    public bool IsOk => Status == DocumentReadStatus.Ok;

    public static DocumentReadResult Success(string text) => new(DocumentReadStatus.Ok, text, null);

    /// 금고 문서를 읽었다. 어느 세대의 어느 키로 열었는지를 싣는다 — 저장이 그 키로만 쓰게 한다.
    /// Text 는 검색용 글자, Rich 는 서식 문서다 (D-123).
    public static DocumentReadResult Success(DocumentBody body, DocumentKeyBinding binding)
        => new(DocumentReadStatus.Ok, body.Text, null, Binding: binding, Rich: body.Rich);

    /// <summary>
    /// 평문 파일을 읽었다. 무엇으로 읽었는지(EncodingLabel)와 그것이 확정인지 추정인지(EncodingIsGuess)를 함께 싣는다 —
    /// 인코딩을 잘못 골라도 예외가 나지 않고 깨진 글자가 조용히 그려지기 때문에,
    /// 화면 표시가 사용자의 유일한 방어 수단이다. 저장은 PlainFormat 그대로 다시 쓴다 (D-118).
    /// </summary>
    public static DocumentReadResult PlainText(string text, string encodingLabel, bool isGuess, PlainTextFormat format)
        => new(DocumentReadStatus.Ok, text, null, DocumentKind.PlainText, encodingLabel, isGuess, PlainFormat: format);

    public static DocumentReadResult Fail(DocumentReadStatus status, string? detail = null) => new(status, null, detail);

    /// 값이나 문서 내용은 절대 넣지 않는다 (PROHIBITED-CUSTOM-04).
    public string UserMessage => Status switch
    {
        DocumentReadStatus.Ok => "",
        DocumentReadStatus.NotTextBeanDocument => "이 파일은 TextBean 문서가 아닙니다.",
        DocumentReadStatus.NewerVersion => "더 최신 버전에서 만든 문서입니다.",

        DocumentReadStatus.DifferentKey =>
            "지금 키로 열리지 않는 문서입니다. 다른 키로 만든 문서일 수 있습니다. "
            + "'키 변경'으로 이 문서를 만든 키를 넣으세요.",

        // 키가 없을 때 버튼 이름은 '키 입력'이다 (D-071)
        DocumentReadStatus.NoKey => "키를 넣어야 열 수 있는 문서입니다. '키 입력'으로 키를 넣으세요.",

        // 손상을 키 문제로 안내하면 사용자가 되돌릴 지점을 시도하지 않는다.
        DocumentReadStatus.Corrupted =>
            "문서가 손상돼 열 수 없습니다. '열었을 때 상태 보기'로 이 문서를 열었을 때의 상태를 확인해보세요.",

        DocumentReadStatus.LegacyDpapi =>
            "옛 형식으로 만든 문서라 이 버전에서는 열 수 없습니다. 보관해 둔 옛 버전 TextBean 으로 여세요.",

        DocumentReadStatus.UndecodableText =>
            "이 파일의 문자 인코딩을 알 수 없어 내용을 보여줄 수 없습니다. "
            + "이진 파일이거나 BOM 없는 UTF-16일 수 있습니다.",

        _ => "문서를 읽을 수 없습니다."
    };
}
