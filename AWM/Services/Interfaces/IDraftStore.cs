using AWM.Models;

namespace AWM.Services.Interfaces;

/// <summary>
/// 초안 파일 I/O 의 유일한 통로. 실패는 IOException·UnauthorizedAccessException 으로 올린다.
/// </summary>
public interface IDraftStore
{
    string Root { get; }

    /// <summary>
    /// 새 폴더 「yyyy-MM-dd HHmm (제목)」을 만들어 저장한다. 이름이 겹치면 " (2)" 부터 붙인다.
    /// </summary>
    Task<StoredDraft> CreateAsync(SavedDraft draft);

    /// <summary>
    /// 같은 폴더에 덮어쓴 뒤, 제목이 바뀌었으면 폴더 이름을 바꾼다. 이름 변경이 막히면 내용만 저장하고 RenameFailed.
    /// 폴더가 없으면 <see cref="DraftFolderMissingException"/>.
    /// </summary>
    Task<(StoredDraft Stored, bool RenameFailed)> SaveAsync(string folder, SavedDraft draft);

    /// <summary>
    /// 마지막 저장 최신순.
    /// </summary>
    Task<DraftListResult> ListAsync();

    Task<StoredDraft> OpenAsync(string folder);

    /// <summary>
    /// 사진을 초안 폴더 images\NN.확장자 로 바이트 그대로 복사하고 새 파일 이름을 돌려준다 (D-005).
    /// </summary>
    Task<string> AddImageAsync(string folder, string sourcePath);

    /// <summary>
    /// 초안 사진의 전체 경로. 파일이 실제로 없으면 null.
    /// </summary>
    string? FindImage(string folder, string fileName);

    /// <summary>
    /// 사진 원본 바이트(Claude 에게 보낼 사본을 만들 때). 초안 사진이든 아직 붙이기 전 원본이든.
    /// </summary>
    Task<byte[]> ReadImageAsync(string path);
}
