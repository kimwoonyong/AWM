namespace TextBean.Models;

/// <summary>
/// 문서를 잠근 키의 식별자 — 헤더의 KDF 종류 · 반복 횟수 · salt. 같은 키로 만든 문서끼리 공유한다.
/// salt 를 파일마다 두면 문서 1,000개에 키 만들기가 1,000번 돌아 키 입력·검색마다 1분이 넘는다 [실측].
/// 세 값 중 하나라도 다르면 다른 키다: salt 만 같고 반복 횟수가 다른 헤더를 같은 키로 치면,
/// 60만 회로 만든 키로 암호화하면서 헤더에는 다른 반복 횟수를 적어 문서가 영구히 잠긴다.
/// </summary>
public readonly record struct DocumentKeyId(byte KdfId, int Iterations, string SaltHex);

/// <summary>
/// 편집기가 문서를 열 때 받은 키 결속. 저장할 때 그대로 돌려준다.
/// 세대가 바뀌었으면(키 전환·잠그기) 저장을 거부한다 — 옛 키로 연 탭이 새 키로 문서를 조용히 다시 잠그는 길을 막는다.
/// </summary>
public sealed record DocumentKeyBinding(long Generation, DocumentKeyId Key);

public enum DocumentHeaderKind
{
    NotTextBean,
    Legacy,
    Newer,
    Corrupted,
    Valid
}

/// 파일 앞부분만 읽은 판정. 복호하지 않는다 — 트리 표시와 키 판정은 이것만으로 한다.
public sealed record DocumentHeader(DocumentHeaderKind Kind, DocumentKeyId Key = default, byte[]? Check = null);

/// 지금 키로 이 문서가 어떤 상태인가. 트리 표시에 쓴다.
public enum DocumentKeyState
{
    Checking,
    Matches,
    DifferentKey,
    NoKey,
    Legacy,
    Corrupted,
    Unreadable
}
