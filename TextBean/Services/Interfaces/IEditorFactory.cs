using TextBean.ViewModels;

namespace TextBean.Services.Interfaces;

/// <summary>
/// 탭 하나당 편집기 한 벌(자동 저장 타이머 포함)을 만든다.
/// ViewModel 은 Views/ 를 참조할 수 없으므로(D-013) 생성은 조립 루트가 준 팩터리가 맡는다.
/// </summary>
public interface IEditorFactory
{
    EditorViewModel Create();
}
