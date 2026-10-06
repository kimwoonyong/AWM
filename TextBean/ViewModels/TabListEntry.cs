namespace TextBean.ViewModels;

/// <summary>
/// ▾ 열린 탭 목록의 한 줄. 상태 기호는 탭의 기존 속성을 그대로 바인딩한다(머리글과 같은 스타일).
/// </summary>
/// <param name="Folder">같은 제목의 탭이 둘 이상일 때만 금고 안 폴더. 아니면 null.</param>
public sealed record TabListEntry(EditorViewModel Tab, string? Folder);
