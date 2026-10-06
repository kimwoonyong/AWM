namespace AWM.Models;

/// <summary>
/// 읽지 못한 폴더(깨진 json·다른 버전·손으로 만든 폴더)는 목록에서 빼고 개수만 남긴다 (D-009).
/// </summary>
public sealed record DraftListResult(IReadOnlyList<StoredDraft> Drafts, int UnreadableCount);
