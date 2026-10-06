namespace TextBean.Models;

/// <summary>
/// 읽어 들인 파일의 종류. "읽기에 성공했다"와 "쓸 수 있다"를 가르기 위해 존재한다.
///
/// 이 필드가 없을 때는 규칙이 "읽기 성공 ⇒ 쓰기 가능" 하나뿐이었다. 그래서 평문을 화면에
/// 흐르게 하는 유일한 통로(DocumentReadResult.IsOk)를 타는 순간 저장까지 함께 열렸고,
/// 한 글자만 입력하면 1.5초 뒤 자동 저장이 평문 파일을 DPAPI 암호문으로 덮었다.
/// </summary>
public enum DocumentKind
{
    /// .tbx — 사용자가 넣은 키로 잠긴 금고 문서. 읽고 쓴다.
    Encrypted,

    /// .txt — 앱이 암호화하지 않는 참고 파일. 보기만 한다.
    PlainText,
}
