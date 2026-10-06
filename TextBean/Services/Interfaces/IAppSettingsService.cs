using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// settings.json 접근의 유일한 통로. 다른 곳에서 설정 파일에 직접 I/O 금지 (PROHIBITED-CONFIG-02).
/// </summary>
public interface IAppSettingsService
{
    AppSettings Current { get; }

    /// 최초 실행에서 폴더 선택 대화상자에 미리 채워 보여줄 경로.
    string SuggestedDefaultRoot { get; }

    Task LoadAsync();

    Task SaveAsync();
}
