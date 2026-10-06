namespace AWM.Services.Interfaces;

public interface IClipboardService
{
    /// <summary>
    /// 다른 프로그램이 클립보드를 잡고 있어 끝내 쓰지 못하면 false.
    /// </summary>
    bool TrySetText(string text);

    /// <summary>
    /// 서식(HTML)과 일반 텍스트를 한 번에 넣는다. HTML 을 받는 곳(네이버 에디터)은 서식으로, 메모장은 텍스트로 받는다.
    /// </summary>
    bool TrySetHtml(string htmlFragment, string plainText);

    /// <summary>
    /// 파일 목록(탐색기에서 파일을 복사한 것과 같다). 네이버에 붙이면 원본 그대로 사진으로 올라간다 [실측].
    /// </summary>
    bool TrySetFiles(IReadOnlyList<string> paths);
}
