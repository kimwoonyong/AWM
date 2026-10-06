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
}
