using System.IO;

namespace AWM.Services;

/// <summary>
/// 열어 둔 초안의 폴더가 그새 지워지거나 옮겨졌다. 호출자가 새로 저장할지 묻는다.
/// </summary>
public sealed class DraftFolderMissingException(string folder)
    : IOException($"초안 폴더를 찾을 수 없습니다: {folder}")
{
    public string Folder { get; } = folder;
}
