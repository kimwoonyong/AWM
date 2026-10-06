using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using AWM.Services.Interfaces;

namespace AWM.Views.Platform;

public sealed class ClipboardService : IClipboardService
{
    private const string FragmentStart = "<html><body><!--StartFragment-->";
    private const string FragmentEnd = "<!--EndFragment--></body></html>";
    private const string HeaderTemplate =
        "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public bool TrySetText(string text) => TrySet(() => Clipboard.SetDataObject(text, copy: true));

    public bool TrySetHtml(string htmlFragment, string plainText)
    {
        var data = new DataObject();
        // 문자열로 넣으면 한글이 깨진 적이 있다 [실측]. 위치 값을 UTF-8 바이트로 계산한 바이트를 그대로 넣는다 (D-004)
        data.SetData(DataFormats.Html, new MemoryStream(BuildCfHtml(htmlFragment)));
        data.SetData(DataFormats.UnicodeText, plainText);
        return TrySet(() => Clipboard.SetDataObject(data, copy: true));
    }

    public bool TrySetFiles(IReadOnlyList<string> paths)
    {
        var files = new System.Collections.Specialized.StringCollection();
        foreach (var path in paths)
            files.Add(path);
        return TrySet(() => Clipboard.SetFileDropList(files));
    }

    /// <summary>
    /// Windows HTML 클립보드 형식(CF_HTML). 머리의 네 위치 값은 문서 처음부터의 UTF-8 바이트 수다.
    /// </summary>
    private static byte[] BuildCfHtml(string fragment)
    {
        var headerLength = Utf8NoBom.GetByteCount(string.Format(HeaderTemplate, 0, 0, 0, 0));
        var startHtml = headerLength;
        var startFragment = startHtml + Utf8NoBom.GetByteCount(FragmentStart);
        var endFragment = startFragment + Utf8NoBom.GetByteCount(fragment);
        var endHtml = endFragment + Utf8NoBom.GetByteCount(FragmentEnd);

        var document = string.Format(HeaderTemplate, startHtml, endHtml, startFragment, endFragment)
                       + FragmentStart + fragment + FragmentEnd;
        return Utf8NoBom.GetBytes(document);
    }

    private static bool TrySet(Action set)
    {
        try
        {
            // 다른 프로그램이 클립보드를 잡고 있으면 실패한다. 실패는 화면 상태 줄로 알린다
            set();
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}
