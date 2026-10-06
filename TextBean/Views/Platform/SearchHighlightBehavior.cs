using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

/// <summary>
/// 본문 TextBox 에 "찾은 자리 모두 강조"와 "그 자리로 데려가기"를 붙인다.
///
/// 탭마다 TextBox 가 새로 만들어지므로 코드비하인드에서 이름으로 붙일 수 없다 —
/// x:Name 은 DataTemplate 안에서 필드를 만들지 않는다 (F-2). 첨부 속성이라야 규칙이 따라간다.
/// </summary>
public static class SearchHighlightBehavior
{
    public static readonly DependencyProperty ShowMatchesProperty =
        DependencyProperty.RegisterAttached("ShowMatches", typeof(bool), typeof(SearchHighlightBehavior),
            new PropertyMetadata(false, OnShowMatchesChanged));

    public static void SetShowMatches(DependencyObject element, bool value)
        => element.SetValue(ShowMatchesProperty, value);

    public static bool GetShowMatches(DependencyObject element)
        => (bool)element.GetValue(ShowMatchesProperty);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(Hookup), typeof(SearchHighlightBehavior));

    private static void OnShowMatchesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBoxBase box) return;

        (box.GetValue(StateProperty) as Hookup)?.Detach();
        box.SetValue(StateProperty, (bool)e.NewValue ? new Hookup(box) : null);
    }

    /// <summary>
    /// TextBox 하나에 붙는 배선. 좌표가 뷰포트 기준이라 스크롤·크기·본문이 바뀔 때마다
    /// 다시 그려야 한다 — 빠뜨리면 강조가 엉뚱한 줄 위에 남아 "이 값이 일치했다"고
    /// 잘못 알려준다. 비밀 보관함에서는 단순 표시 오류가 아니다.
    /// </summary>
    private sealed class Hookup
    {
        private readonly TextBoxBase _box;
        private MatchHighlightAdorner? _adorner;
        private EditorViewModel? _vm;

        public Hookup(TextBoxBase box)
        {
            _box = box;

            _box.Loaded += OnLoaded;
            _box.Unloaded += OnUnloaded;
            _box.SizeChanged += OnRedrawNeeded;
            _box.TextChanged += OnRedrawNeeded;
            _box.DataContextChanged += OnDataContextChanged;
            _box.IsVisibleChanged += OnVisibilityChanged;
            _box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnRedrawNeeded));

            Bind(_box.DataContext as EditorViewModel);
            if (_box.IsLoaded) Attach();
        }

        public void Detach()
        {
            _box.Loaded -= OnLoaded;
            _box.Unloaded -= OnUnloaded;
            _box.SizeChanged -= OnRedrawNeeded;
            _box.TextChanged -= OnRedrawNeeded;
            _box.DataContextChanged -= OnDataContextChanged;
            _box.IsVisibleChanged -= OnVisibilityChanged;
            _box.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnRedrawNeeded));

            Bind(null);
            Remove();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

        private void OnUnloaded(object sender, RoutedEventArgs e) => Remove();

        private void OnRedrawNeeded(object sender, EventArgs e) => _adorner?.InvalidateVisual();

        /// 탭을 바꾸면 보이는 상태가 바뀐다. 다시 그리지 않으면 가려진 탭의 강조가 화면에 남는다.
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
            => _adorner?.InvalidateVisual();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Bind(e.NewValue as EditorViewModel);
            _adorner?.InvalidateVisual();
        }

        /// 편집기가 바뀌면 구독을 옮긴다. 안 떼면 닫힌 탭의 편집기가 살아남는다.
        private void Bind(EditorViewModel? vm)
        {
            if (ReferenceEquals(_vm, vm)) return;

            if (_vm is not null)
            {
                _vm.PropertyChanged -= OnVmChanged;
                _vm.RevealRequested -= OnReveal;
            }

            _vm = vm;

            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVmChanged;
                _vm.RevealRequested += OnReveal;
            }
        }

        private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(EditorViewModel.Matches)
                               or nameof(EditorViewModel.CurrentMatch)
                               or nameof(EditorViewModel.MatchLength))
                _adorner?.InvalidateVisual();
        }

        /// <summary>
        /// 찾은 자리로 데려간다. Select() 만으로는 **본문에 키보드 포커스가 없으면
        /// 화면이 안 따라간다** [실측] — 검색창에 커서가 있는 동안이 정확히 그 상태다.
        /// BringIntoView(Rect) 는 TextBox 내부 ScrollViewer 를 못 움직이므로 쓰지 않는다 [실측].
        /// </summary>
        private void OnReveal(object? sender, int index)
        {
            if (_box is RichTextBox rich)
            {
                RevealRich(rich, index);
                return;
            }

            if (_vm is null || _box is not TextBox box || index < 0 || index > box.Text.Length) return;
            RevealPlain(box, index);
        }

        /// 서식 본문은 줄 바꿈이 켜져 있어(D-130) 가로 스크롤이 없다 — 세로만 맞춘다.
        private void RevealRich(RichTextBox rich, int index)
        {
            if (_vm is null || RichBodyBehavior.GetMap(rich) is not { } map || index < 0 || index > map.Text.Length) return;

            var start = map.PointerAt(index);
            var end = map.PointerAt(Math.Min(index + _vm.MatchLength, map.Text.Length));
            if (start is null || end is null) return;

            rich.Selection.Select(start, end);

            var rect = start.GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty && (rect.Top < 0 || rect.Bottom > rich.ActualHeight))
                rich.ScrollToVerticalOffset(Math.Max(0, rich.VerticalOffset + rect.Top - rich.ActualHeight / 3));

            _adorner?.InvalidateVisual();
        }

        private void RevealPlain(TextBox box, int index)
        {
            var length = Math.Min(_vm!.MatchLength, box.Text.Length - index);
            box.Select(index, Math.Max(length, 0));

            var line = box.GetLineIndexFromCharacterIndex(index);
            if (line >= 0) box.ScrollToLine(line);

            // 본문은 TextWrapping 이 없어(NoWrap) 긴 줄이 가로로 흐른다 —
            // 세로만 맞추면 일치가 화면 오른쪽 밖에 있다 [실측].
            var rect = box.GetRectFromCharacterIndex(index);
            if (!rect.IsEmpty && (rect.X < 0 || rect.X > box.ActualWidth - 40))
                box.ScrollToHorizontalOffset(Math.Max(0, box.HorizontalOffset + rect.X - box.ActualWidth / 3));

            _adorner?.InvalidateVisual();
        }

        private void Attach()
        {
            if (_adorner is not null) return;

            var layer = AdornerLayer.GetAdornerLayer(_box);
            if (layer is null) return;

            _adorner = new MatchHighlightAdorner(_box) { IsHitTestVisible = false };
            layer.Add(_adorner);
        }

        private void Remove()
        {
            if (_adorner is null) return;

            AdornerLayer.GetAdornerLayer(_box)?.Remove(_adorner);
            _adorner = null;
        }
    }
}
