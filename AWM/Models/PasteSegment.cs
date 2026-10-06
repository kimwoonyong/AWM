namespace AWM.Models;

/// <summary>
/// 네이버에 차례로 붙일 조각. 글과 그림은 한 번에 붙일 수 없어 번갈아 붙인다 [실측 — add-draft-images research 관찰 1·4].
/// </summary>
public abstract record PasteSegment;

public sealed record TextSegment(FormattedBody Body) : PasteSegment;

/// <summary>
/// 연달아 있는 사진은 파일 목록 하나로 한 번에 붙는다 [실측 — 관찰 3].
/// </summary>
public sealed record ImageSegment(IReadOnlyList<string> Paths) : PasteSegment;
