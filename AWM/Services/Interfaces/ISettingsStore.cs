using AWM.Models;

namespace AWM.Services.Interfaces;

/// <summary>
/// 설정 파일 I/O 의 유일한 통로. 생성할 때마다 Current 를 읽으므로 저장하면 바로 적용된다 (D-012).
/// </summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

    /// <summary>
    /// 처음 한 번. 파일이 없으면 기본값, 깨졌으면 .bak 으로 남기고 기본값 — 그때 알릴 문구를 돌려준다(없으면 null).
    /// </summary>
    string? Load();

    /// <summary>
    /// 실패하면 IOException·UnauthorizedAccessException. 성공했을 때만 Current 가 바뀐다.
    /// </summary>
    Task SaveAsync(AppSettings settings);
}
