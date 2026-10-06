namespace AWM.Models;

/// <summary>
/// Claude 가 만든 초안. 태그에는 # 이 붙어 있지 않다.
/// </summary>
public sealed record BlogDraft(string Title, string Body, IReadOnlyList<string> Tags);
