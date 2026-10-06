using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// 금고를 뒤진다. 두 경로가 성격이 완전히 다르다:
/// 이름 검색은 복호화가 0회라 입력 즉시 돌릴 수 있고,
/// 본문 검색은 대상 문서를 전부 복호화하므로 명시적으로 실행하고 취소할 수 있어야 한다 [실측].
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// 본문 검색. <paramref name="scopeFolder"/> 가 null 이면 금고 전체.
    /// <paramref name="openTabs"/> 는 열려 있는 탭의 메모리 본문 — 자동 저장(1.5초) 전이면
    /// 디스크와 다르므로, 이게 없으면 방금 친 값이 "없음"으로 보고된다 [실측].
    /// </summary>
    Task<SearchOutcome> SearchAsync(string query, string? scopeFolder,
                                    IReadOnlyDictionary<string, string> openTabs,
                                    CancellationToken ct, bool matchCase = false);

    /// <summary>
    /// 문서 하나만 뒤진다. 열려 있지 않아도 된다 —
    /// 열린 탭 중에서만 고를 수 있으면 "금고에 없다"는 거짓 답이 나온다.
    /// <paramref name="liveText"/> 를 주면 디스크를 읽지 않는다(저장 전 내용).
    /// 예약 영역(.trash/.history)이면 아무것도 찾지 않는다.
    /// </summary>
    Task<SearchOutcome> SearchDocumentAsync(string fullPath, string query, string? liveText,
                                            CancellationToken ct, bool matchCase = false);

    /// 이름 검색. 폴더명·문서명은 평문이라(D-002) 복호화하지 않는다.
    Task<IReadOnlyList<SearchHit>> SearchNamesAsync(string query, string? scopeFolder, CancellationToken ct,
                                                    bool matchCase = false);
}
