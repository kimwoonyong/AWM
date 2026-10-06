namespace TextBean.Models;

/// <param name="Unreadable">
/// 이 폴더의 목록을 읽지 못했다(권한 없음·IO 오류). 그 안의 문서는 트리에도 검색에도 나타나지 않으므로,
/// 세어서 알리지 않으면 "금고에 없다"는 거짓 답이 된다 — 사용자는 키를 새로 발급받는다.
/// </param>
public sealed record TreeNode(
    string FullPath,
    string Name,
    bool IsFolder,
    IReadOnlyList<TreeNode> Children,
    bool Unreadable = false,

    /// 암호화되지 않은 참고 파일(.txt). 트리 아이콘이 이 값을 보고 갈린다.
    /// 뒤에 기본값으로 붙여야 기존 생성 지점이 그대로 컴파일된다.
    bool IsPlainText = false);
