namespace AWM.Models;

/// <summary>
/// draft.json 형식 v1 (D-008). 그림이 들어가며 모양이 바뀌면 Version 을 올린다.
/// </summary>
public sealed record SavedDraft
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DraftRequest Request { get; init; } = new("", "", "");
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public string Model { get; init; } = "";

    /// <summary>
    /// 다음 차수(그림)를 위해 자리만 둔다. v1 에서는 늘 비어 있다.
    /// </summary>
    public List<string> Images { get; init; } = [];
}
