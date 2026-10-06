namespace AWM.Services.Interfaces;

/// <summary>
/// ViewModel 이 WPF 창을 모르게 하는 경계 (D-010). 구현은 Views/Platform/DialogService.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 저장 안 한 수정이 있을 때 「예/아니요/취소」.
    /// </summary>
    SaveChoice AskSaveChanges();

    /// <summary>
    /// 열어 둔 초안의 폴더가 사라졌을 때 새 폴더로 저장할지.
    /// </summary>
    bool AskSaveAsNew(string missingFolderName);

    /// <summary>
    /// 목록 창을 띄워 고른 초안 폴더를 돌려준다. 닫으면 null.
    /// </summary>
    string? PickDraft();
}
