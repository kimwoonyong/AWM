using AWM.Models;

namespace AWM.Services.Interfaces;

public interface IDraftService
{
    /// <summary>
    /// 사진이 있으면 Claude 가 사진을 보고 그 내용으로 쓰며 「[사진: 설명 | 파일]」 줄로 배치한다.
    /// </summary>
    Task<BlogDraft> CreateAsync(DraftRequest request, IReadOnlyList<ImageInput> photos, CancellationToken ct);

    /// <summary>
    /// 이미 쓴 글은 그대로 두고 사진 위치만 받는다 (D-015).
    /// </summary>
    Task<IReadOnlyList<PhotoPlacement>> PlacePhotosAsync(string body, IReadOnlyList<ImageInput> photos, CancellationToken ct);
}
