namespace TextBean.Models;

/// <summary>
/// 검색 범위. **실행 시점에 고정한다** — 결과를 보는 중 트리 선택이 바뀌었다고
/// 목록이 갈아끼워지면 방금 본 항목이 사라진다.
/// 범위를 착각하면 "금고에 없다"는 거짓 결론이 나오므로 화면에 항상 드러내야 한다.
/// </summary>
public enum SearchScope
{
    /// 금고 전체
    All,

    /// 트리에서 고른 폴더 아래 (하위 폴더 포함). 대상은 TargetFolder 규칙을 따른다
    Folder,

    /// 지금 보고 있는 탭 하나
    Document
}
