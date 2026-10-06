using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TextBean.Views.Platform;

/// <summary>
/// 서식 6종 (D-127): 굵게 · 기울임 · 밑줄 · 취소선 · 글자색 · 형광펜, 그리고 서식 지우기.
/// 밑줄과 취소선은 같은 TextDecorations 칸을 쓴다. WPF 기본 밑줄(Ctrl+U)은 그 칸을 통째로 갈아 끼워
/// 취소선을 지운다 — 그래서 밑줄도 여기 명령으로 받는다. 굵게 · 기울임은 WPF 기본 명령을 쓴다.
/// </summary>
public static class RichFormat
{
    public static readonly RoutedUICommand ToggleUnderline = new("밑줄", nameof(ToggleUnderline), typeof(RichFormat));
    public static readonly RoutedUICommand ToggleStrikethrough = new("취소선", nameof(ToggleStrikethrough), typeof(RichFormat));

    /// 매개변수: 색 이름(Palette 키). "" 는 기본색.
    public static readonly RoutedUICommand SetForeground = new("글자색", nameof(SetForeground), typeof(RichFormat));

    /// 매개변수: 색 이름(Palette 키). "" 는 형광펜 없음.
    public static readonly RoutedUICommand SetHighlight = new("형광펜", nameof(SetHighlight), typeof(RichFormat));

    public static readonly RoutedUICommand ClearFormatting = new("서식 지우기", nameof(ClearFormatting), typeof(RichFormat));

    /// 고정 색 목록 [제안 — D-127]. 문서에는 색 값이 저장되므로 나중에 이름을 바꿔도 옛 문서는 그대로다.
    public static readonly IReadOnlyDictionary<string, Color> TextColors = new Dictionary<string, Color>
    {
        ["빨강"] = Color.FromRgb(0xE2, 0x4B, 0x4A),
        ["주황"] = Color.FromRgb(0xD8, 0x5A, 0x30),
        ["초록"] = Color.FromRgb(0x3B, 0x6D, 0x11),
        ["파랑"] = Color.FromRgb(0x18, 0x5F, 0xA5),
        ["보라"] = Color.FromRgb(0x53, 0x4A, 0xB7),
        ["회색"] = Color.FromRgb(0x88, 0x87, 0x80),
    };

    public static readonly IReadOnlyDictionary<string, Color> HighlightColors = new Dictionary<string, Color>
    {
        ["노랑"] = Color.FromRgb(0xFA, 0xC7, 0x75),
        ["연두"] = Color.FromRgb(0xC0, 0xDD, 0x97),
        ["하늘"] = Color.FromRgb(0xB5, 0xD4, 0xF4),
        ["분홍"] = Color.FromRgb(0xF4, 0xC0, 0xD1),
    };

    /// 서식 본문에 명령을 붙인다. 키: Ctrl+U(밑줄 — 기본 동작을 갈아 끼움) · Ctrl+Shift+X(취소선).
    public static void Attach(RichTextBox box)
    {
        box.CommandBindings.Add(new CommandBinding(ToggleUnderline,
            (_, _) => ToggleDecoration(box.Selection, TextDecorationLocation.Underline), CanFormat));
        box.CommandBindings.Add(new CommandBinding(ToggleStrikethrough,
            (_, _) => ToggleDecoration(box.Selection, TextDecorationLocation.Strikethrough), CanFormat));
        box.CommandBindings.Add(new CommandBinding(SetForeground,
            (_, e) => box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brush(TextColors, e.Parameter) ?? box.Foreground), CanFormat));
        box.CommandBindings.Add(new CommandBinding(SetHighlight,
            (_, e) => box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brush(HighlightColors, e.Parameter)), CanFormat));
        box.CommandBindings.Add(new CommandBinding(ClearFormatting,
            (_, _) => box.Selection.ClearAllProperties(), CanFormat));

        box.InputBindings.Add(new KeyBinding(ToggleUnderline, Key.U, ModifierKeys.Control));
        box.InputBindings.Add(new KeyBinding(ToggleStrikethrough, Key.X, ModifierKeys.Control | ModifierKeys.Shift));
    }

    private static void CanFormat(object sender, CanExecuteRoutedEventArgs e)
        => e.CanExecute = sender is RichTextBox { IsReadOnly: false };

    private static SolidColorBrush? Brush(IReadOnlyDictionary<string, Color> palette, object? name)
        => name is string key && palette.TryGetValue(key, out var color) ? Frozen(color) : null;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 선택 안의 글자 조각마다 그 줄을 켜거나 끈다. 조각 하나라도 없으면 모두 켠다(워드와 같은 규칙).
    /// 선택 전체에 한 값을 칠하면 밑줄 · 취소선이 섞인 선택에서 한쪽이 지워진다.
    /// </summary>
    public static void ToggleDecoration(TextSelection selection, TextDecorationLocation location)
    {
        if (selection.IsEmpty)
        {
            // 빈 선택은 다음에 칠 글자의 서식이다
            var current = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, With(current, location, !Has(current, location)));
            return;
        }

        var pieces = Pieces(selection);
        var add = !pieces.All(piece => Has(Decorations(piece), location));
        foreach (var piece in pieces)
            piece.ApplyPropertyValue(Inline.TextDecorationsProperty, With(Decorations(piece), location, add));
    }

    private static TextDecorationCollection? Decorations(TextRange range)
        => range.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;

    private static bool Has(TextDecorationCollection? decorations, TextDecorationLocation location)
        => decorations?.Any(d => d.Location == location) == true;

    private static TextDecorationCollection With(TextDecorationCollection? decorations, TextDecorationLocation location, bool on)
    {
        var result = new TextDecorationCollection((decorations ?? []).Where(d => d.Location != location));
        if (on) result.Add(location == TextDecorationLocation.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough);
        result.Freeze();
        return result;
    }

    /// 선택 안의 글자 조각들. 조각을 먼저 다 모은다 — 서식을 칠하면 Run 이 쪼개진다(TextPointer 는 살아 있다).
    private static List<TextRange> Pieces(TextRange selection)
    {
        var pieces = new List<TextRange>();
        for (var at = selection.Start; at is not null && at.CompareTo(selection.End) < 0; at = at.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (at.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;

            var runEnd = at.GetPositionAtOffset(at.GetTextRunLength(LogicalDirection.Forward))!;
            var end = runEnd.CompareTo(selection.End) < 0 ? runEnd : selection.End;
            pieces.Add(new TextRange(at, end));
        }
        return pieces;
    }
}
