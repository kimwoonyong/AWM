using TextBean.Models;

namespace TextBean.Services.Interfaces;

/// <summary>
/// Clipboard 직접 호출은 이 계약의 구현체 밖에서 금지 (PROHIBITED-CUSTOM-02).
/// WPF TextBox의 기본 복사 동작도 가로채 여기로 편입한다.
/// </summary>
public interface IClipboardService : IDisposable
{
    void Copy(string text);

    /// 글자 · 서식 형식을 DataObject 하나에 담는다. 기록 제외 · 30초 비움은 모든 형식에 같이 걸린다 (D-126).
    void Copy(ClipboardPayload payload);

    /// 현재 클립보드가 우리가 넣은 값과 같을 때만 비운다.
    /// 확인 없이 지우면 그 사이 사용자가 복사한 남의 내용을 날린다.
    void ClearIfOurs();
}
