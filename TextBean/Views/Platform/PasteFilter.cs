using System.Windows;

namespace TextBean.Views.Platform;

public enum PasteChoice { Rich, PlainText, Reject }

/// <summary>
/// 서식 본문에 무엇을 붙여넣을지 (D-126). 앱 안에서 복사한 서식(표지 + XamlPackage)만 서식째 받고,
/// 밖에서 온 것은 글자만 받는다 — 웹 · 워드의 글꼴 · 크기 · 배경이 문서에 섞이지 않게.
/// 글자도 없으면(그림 · 파일) 받지 않는다. 그림은 ③단계에서 다룬다.
/// </summary>
public static class PasteFilter
{
    public static PasteChoice Choose(IDataObject data)
    {
        if (data.GetDataPresent(ClipboardService.RichMarkerFormat, autoConvert: false)
            && data.GetDataPresent(DataFormats.XamlPackage, autoConvert: false))
            return PasteChoice.Rich;

        return data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true) ? PasteChoice.PlainText : PasteChoice.Reject;
    }
}
