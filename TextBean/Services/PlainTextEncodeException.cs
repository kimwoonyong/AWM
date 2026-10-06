namespace TextBean.Services;

/// <summary>
/// 평문 파일의 인코딩으로 담을 수 없는 글자가 있다 (D-119). 인코딩을 바꾸지 않고 저장을 멈춘다.
/// 메시지는 대화상자에 그대로 보인다 — 줄 번호만 넣고 글자 자체는 넣지 않는다 (PROHIBITED-CUSTOM-04).
/// </summary>
public sealed class PlainTextEncodeException(int line, string encoding)
    : InvalidOperationException($"{line}번째 줄에 {encoding} 로 저장할 수 없는 글자가 있습니다. 그 글자를 지우면 저장됩니다.")
{
    public int Line { get; } = line;
}
