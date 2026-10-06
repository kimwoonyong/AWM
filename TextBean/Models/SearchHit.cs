namespace TextBean.Models;

/// <summary>
/// 문서 하나의 검색 결과. **본문 조각을 담지 않는다** (C-03) —
/// 담는 순간 열지도 않은 문서의 평문이 결과 목록을 타고 화면에 늘어선다.
/// 문맥은 문서를 탭으로 열어서 본다.
/// </summary>
/// <param name="IsFolder">
/// 이름 검색은 폴더도 찾는다. 구분이 없으면 폴더를 열었을 때 "읽을 수 없는 문서" 유령 탭이 생긴다.
/// </param>
/// <param name="IsPlainText">
/// 암호화되지 않은 참고 파일(.txt)인가. 결과 목록에는 아이콘이 없으므로 이 값이 없으면
/// 사용자는 열기 전까지 그것이 금고 문서인지 평문인지 알 수 없다.
/// </param>
public sealed record SearchHit(
    string FullPath, string Name, bool IsFolder, int MatchCount, int FirstIndex, bool IsPlainText = false);

/// <summary>
/// 검색 한 번의 결과.
/// <paramref name="UnreadableCount"/> 를 반드시 화면에 띄워야 한다 —
/// 읽지 못한 문서를 조용히 빼면 "일치 없음"과 구분되지 않고,
/// 사용자는 "금고에 없다"고 믿고 키를 새로 발급받는다 (D-005 와 같은 취지).
/// </summary>
/// <param name="UnreadableFolderCount">
/// 목록조차 읽지 못한 폴더 수(권한 없음·IO 오류). 그 안에 문서가 몇 개인지도 알 수 없다 —
/// 세지 않으면 "다 뒤졌고 없다"가 되지만 실제로는 통째로 안 본 영역이 있다.
/// </param>
/// <param name="OtherKeyCount">
/// 지금 키로 열리지 않아 보지 않은 문서 수(다른 키 · 키 없음). 손상과 따로 센다 — 이것은 정상 상태다.
/// 그래도 "다 뒤졌다"로 읽으면 안 된다: 다른 키 문서에 있는 값을 "금고에 없다"고 믿게 된다.
/// </param>
/// <param name="LegacyCount">옛 방식(Windows 계정) 문서라 변환 전에는 보지 못한 수.</param>
public sealed record SearchOutcome(
    IReadOnlyList<SearchHit> Hits,
    int UnreadableCount,
    int ScannedCount,
    bool Canceled,
    int UnreadableFolderCount = 0,
    int OtherKeyCount = 0,
    int LegacyCount = 0)
{
    public static readonly SearchOutcome Empty = new([], 0, 0, false);

    /// 결과를 "없다"로 읽어도 되는가. 하나라도 못 본 게 있으면 그렇게 읽으면 안 된다.
    public bool IsComplete => UnreadableCount == 0 && UnreadableFolderCount == 0 && OtherKeyCount == 0
                              && LegacyCount == 0 && !Canceled;

    /// 읽지 못한 이유별로 한 건을 센 결과 (문서 하나 범위).
    public static SearchOutcome NotRead(DocumentReadStatus status) => status switch
    {
        DocumentReadStatus.DifferentKey or DocumentReadStatus.NoKey => Empty with { OtherKeyCount = 1, ScannedCount = 1 },
        DocumentReadStatus.LegacyDpapi => Empty with { LegacyCount = 1, ScannedCount = 1 },
        _ => Empty with { UnreadableCount = 1, ScannedCount = 1 }
    };

    public int TotalMatches => Hits.Sum(h => h.MatchCount);
}
