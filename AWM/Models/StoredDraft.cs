using System.IO;

namespace AWM.Models;

/// <summary>
/// 디스크에 있는 초안 하나 — 폴더 경로와 그 안의 draft.json 내용.
/// </summary>
public sealed record StoredDraft(string Folder, SavedDraft Draft)
{
    public string FolderName => Path.GetFileName(Folder);
}
