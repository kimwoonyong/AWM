using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

/// <summary>
/// WPF TextBox 의 기본 복사는 프레임워크가 직접 클립보드에 쓴다.
/// 가로채지 않으면 Ctrl+C / 우클릭 / 드래그가 기록 제외 플래그와 자동 비움을 전부 우회해
/// 복호화된 평문이 Win+V 기록으로 나간다 (D-007, PROHIBITED-CUSTOM-02).
///
/// 탭마다 TextBox 가 새로 만들어지므로 코드비하인드에서 이름으로 붙일 수 없다 —
/// x:Name 은 DataTemplate 안에서 필드를 만들지 않는다 (F-2). 첨부 속성이라야 규칙이 따라간다.
/// </summary>
public static class EditorBehavior
{
    public static readonly DependencyProperty InterceptCopyProperty =
        DependencyProperty.RegisterAttached("InterceptCopy", typeof(bool), typeof(EditorBehavior),
            new PropertyMetadata(false, OnInterceptCopyChanged));

    public static void SetInterceptCopy(DependencyObject element, bool value)
        => element.SetValue(InterceptCopyProperty, value);

    public static bool GetInterceptCopy(DependencyObject element)
        => (bool)element.GetValue(InterceptCopyProperty);

    private static void OnInterceptCopyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;

        if ((bool)e.NewValue) DataObject.AddCopyingHandler(box, OnCopying);
        else DataObject.RemoveCopyingHandler(box, OnCopying);
    }

    private static void OnCopying(object sender, DataObjectCopyingEventArgs e)
    {
        if (sender is not TextBox box) return;

        var isCut = !box.IsReadOnly
                    && box.SelectionLength > 0
                    && Keyboard.Modifiers == ModifierKeys.Control
                    && Keyboard.IsKeyDown(Key.X);
        var selected = box.SelectedText;

        // 먼저 취소한다. 아래에서 무엇이 어긋나 일찍 빠져나가더라도
        // 프레임워크 기본 복사로 넘어가면 그 경로로 평문이 그대로 나간다.
        e.CancelCommand();

        if (e.IsDragDrop) return;                                  // 드래그로 평문이 새는 경로
        if (box.DataContext is not EditorViewModel vm) return;

        vm.Copy(selected);                                         // 기록 제외 + 30초 자동 비움 경로

        if (isCut) box.SelectedText = "";
    }

    // ── 검색창 전용 복사 ─────────────────────────────────────────────────────
    // 검색어 자체가 비밀값이다("내 API 키가 어디 있지?"를 찾으려면 키를 친다).
    // 그런데 맨 TextBox 는 기록 제외 플래그도 자동 비움도 타지 않는다 [실측] — 붙여야 한다.
    // 본문용 InterceptCopy 를 그대로 쓰면 안 된다: 그쪽은 선택이 없을 때 **문서 전체**를 복사한다.

    public static readonly DependencyProperty InterceptCopySelectionProperty =
        DependencyProperty.RegisterAttached("InterceptCopySelection", typeof(bool), typeof(EditorBehavior),
            new PropertyMetadata(false, OnInterceptCopySelectionChanged));

    public static void SetInterceptCopySelection(DependencyObject element, bool value)
        => element.SetValue(InterceptCopySelectionProperty, value);

    public static bool GetInterceptCopySelection(DependencyObject element)
        => (bool)element.GetValue(InterceptCopySelectionProperty);

    private static void OnInterceptCopySelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;

        if ((bool)e.NewValue) DataObject.AddCopyingHandler(box, OnCopyingSelection);
        else DataObject.RemoveCopyingHandler(box, OnCopyingSelection);
    }

    private static void OnCopyingSelection(object sender, DataObjectCopyingEventArgs e)
    {
        if (sender is not TextBox box) return;

        var isCut = !box.IsReadOnly
                    && box.SelectionLength > 0
                    && Keyboard.Modifiers == ModifierKeys.Control
                    && Keyboard.IsKeyDown(Key.X);
        var selected = box.SelectedText;

        e.CancelCommand();

        if (e.IsDragDrop) return;
        if (box.DataContext is not EditorViewModel vm) return;

        // 선택한 글자만. 선택이 없으면 아무것도 복사하지 않는다.
        vm.CopyPlain(selected);

        if (isCut) box.SelectedText = "";
    }
}
