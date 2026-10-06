using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

public sealed class EditorFactory(IDocumentStore store, IClipboardService clipboard, IDialogService dialogs)
    : IEditorFactory
{
    // 타이머는 탭마다 새로 만든다. 탭 20개를 열어도 동시에 도는 것은 1~2개다 [실측] —
    // 공용 타이머를 키로 나눠 쓰는 복잡한 설계를 도입할 근거가 없다.
    public EditorViewModel Create() => new(store, clipboard, dialogs, new AutoSaveTimer());
}
