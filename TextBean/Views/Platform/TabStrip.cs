using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace TextBean.Views.Platform;

/// <summary>
/// 문서 탭 머리글. 탭이 넘쳐도 한 줄로 두고, 넘친 탭은 휠 · ◀ ▶ · ▾ 목록으로 찾는다 (make-tab-strip-single-row, D-074~D-083).
/// 기본 템플릿의 TabPanel 은 넘치면 여러 줄로 접고, 선택한 탭이 든 줄을 맨 아래로 옮겨 탭 위치가 뒤섞였다 [실측].
/// <para>
/// 템플릿은 MainWindow.xaml 에 인라인으로 있다. TabItem 템플릿은 건드리지 않는다 — 머리글 ContentPresenter 가
/// 그 안에 있어, 빠뜨리면 상태 아이콘·제목·✕ 가 통째로 사라지고 빌드는 그대로 통과한다 (LL-014).
/// 부품이 하나라도 없으면 <see cref="OnApplyTemplate"/> 가 던진다 — 조용히 빈 화면이 되지 않게.
/// </para>
/// <para>
/// 규칙: <b>왼쪽 끝은 늘 탭 경계</b>(D-076). 왼쪽이 잘린 탭은 보이는 조각 대부분이 ✕ 라 고르려다 닫는다.
/// 넘김은 모두 <see cref="ScrollTo"/> 를 거친다 — 휠 · ◀▶ · 보이게 넘김 · WPF 가 포커스 때 부르는 BringIntoView(가로챔).
/// 업무 로직은 두지 않는다 — 오프셋 · 버튼 · 선택 전달만 (PROHIBITED-UI-03).
/// </para>
/// </summary>
public sealed class TabStrip : TabControl
{
    /// 탭 끌기 포맷. 트리와 같은 포맷을 쓰면 탭을 트리에 떨굴 때 금고에서 문서가 실제로 옮겨진다.
    public const string DragFormat = "TextBean.Tab";

    /// 끄는 중 가장자리 구역 폭.
    public const double EdgeZone = 24;

    public static readonly DependencyProperty TabListItemTemplateProperty = DependencyProperty.Register(
        nameof(TabListItemTemplate), typeof(DataTemplate), typeof(TabStrip));

    /// ▾ 목록 한 줄의 모양. MainWindow.xaml 에 두어 머리글과 같은 상태 아이콘(TabStateIcon)을 함께 쓴다.
    public DataTemplate? TabListItemTemplate
    {
        get => (DataTemplate?)GetValue(TabListItemTemplateProperty);
        set => SetValue(TabListItemTemplateProperty, value);
    }

    /// ▾ 를 열 때마다 부른다. 셸이 만든 스냅숏을 돌려준다 — 탭 컬렉션을 두 번째 컨트롤에 묶지 않는다 (LL-007).
    public Func<IEnumerable>? TabListProvider { get; set; }

    /// ▾ 목록에서 고른 항목(TabListProvider 가 준 것).
    public event EventHandler<object>? TabPicked;

    /// 끄는 중 자동 넘김 간격.
    public TimeSpan DragScrollInterval { get; set; } = TimeSpan.FromMilliseconds(350);

    private FrameworkElement? _area;
    private ScrollViewer? _scroll;
    private TabPanel? _panel;
    private FrameworkElement? _spacer;
    private ButtonBase? _left;
    private ButtonBase? _right;
    private ButtonBase? _list;

    private int _start;               // 맨 왼쪽 탭(기준 탭)의 순번
    private object? _anchor;          // 그 탭의 항목 — 폭이 바뀌어도 이 탭을 왼쪽 끝에 둔다
    private int _lastStart;           // 끝까지 넘겼을 때의 맨 왼쪽 탭
    private int _wheel;               // 정밀 휠의 작은 Delta 를 모은다

    private DispatcherTimer? _dragTimer;
    private int _dragDirection;
    private Point _lastDragPoint;
    private bool _leavePending;       // DragLeave 를 받았다 — 줄 안 다른 자식의 DragEnter 가 오지 않으면 줄을 떠난 것
    private TabInsertionAdorner? _insertion;

    // 안쪽 ScrollViewer 가 Home/End · 화살표를 가로채지 않게 한다. 가로채면 End 가 선택은 그대로 둔 채 줄만 끝으로 보낸다 [실측].
    protected override bool HandlesScrolling => true;

    public ScrollViewer HeaderScroll => _scroll ?? throw new InvalidOperationException("템플릿이 아직 적용되지 않았다");

    public TabPanel HeaderPanel => _panel ?? throw new InvalidOperationException("템플릿이 아직 적용되지 않았다");

    /// 지금 맨 왼쪽 탭의 순번.
    public int StartIndex => _start;

    /// 끝까지 넘겼을 때의 맨 왼쪽 탭 순번.
    public int LastStartIndex => _lastStart;

    public bool IsOverflowing { get; private set; }

    public Adorner? InsertionAdorner => _insertion;

    public override void OnApplyTemplate()
    {
        Detach();
        base.OnApplyTemplate();

        _area = Part<FrameworkElement>("PART_HeaderArea");
        _scroll = Part<ScrollViewer>("PART_HeaderScroll");
        _panel = Part<TabPanel>("PART_HeaderPanel");
        _spacer = Part<FrameworkElement>("PART_EndSpacer");
        _left = Part<ButtonBase>("PART_ScrollLeft");
        _right = Part<ButtonBase>("PART_ScrollRight");
        _list = Part<ButtonBase>("PART_TabList");
        Part<ContentPresenter>("PART_SelectedContentHost");

        _area.SizeChanged += OnHeaderSizeChanged;
        _scroll.SizeChanged += OnHeaderSizeChanged;
        _panel.SizeChanged += OnPanelSizeChanged;
        _scroll.PreviewMouseWheel += OnWheel;
        _panel.RequestBringIntoView += OnRequestBringIntoView;
        _left.Click += OnLeftClick;
        _right.Click += OnRightClick;
        _list.Click += OnListClick;

        RevealSelected();
    }

    private T Part<T>(string name) where T : class
        => GetTemplateChild(name) as T
           ?? throw new InvalidOperationException($"TabStrip 템플릿에 {name}({typeof(T).Name}) 이 없다 — 머리글이나 내용 칸이 빈다 (LL-014)");

    private void Detach()
    {
        if (_area is not null) _area.SizeChanged -= OnHeaderSizeChanged;
        if (_scroll is not null)
        {
            _scroll.SizeChanged -= OnHeaderSizeChanged;
            _scroll.PreviewMouseWheel -= OnWheel;
        }
        if (_panel is not null)
        {
            _panel.SizeChanged -= OnPanelSizeChanged;
            _panel.RequestBringIntoView -= OnRequestBringIntoView;
        }
        if (_left is not null) _left.Click -= OnLeftClick;
        if (_right is not null) _right.Click -= OnRightClick;
        if (_list is not null) _list.Click -= OnListClick;
    }

    private void OnLeftClick(object sender, RoutedEventArgs e) => Step(-1);

    private void OnRightClick(object sender, RoutedEventArgs e) => Step(+1);

    private void OnListClick(object sender, RoutedEventArgs e) => OpenTabList();

    // ── 경계 · 넘김 ──────────────────────────────────────────────────────────

    private TabItem? Container(int index)
        => ItemContainerGenerator.ContainerFromIndex(index) as TabItem;

    /// <summary>
    /// 탭마다 왼쪽 경계(스크롤 내용 좌표). 첫 탭은 0 — 패널 여백까지 보이게.
    /// 탭 좌표가 아니라 배치 칸으로 잰다 — 선택 탭은 마진 −2 로 2px 앞 · 4px 넓게 그려져 선택에 따라 흔들린다 [실측 — 계획 검토].
    /// </summary>
    public IReadOnlyList<double> TabBoundaries()
    {
        var bounds = new List<double>(Items.Count);
        if (_panel is null || _scroll?.Content is not UIElement content) return bounds;

        var panelX = _panel.TranslatePoint(new Point(0, 0), content).X;
        for (var i = 0; i < Items.Count; i++)
        {
            if (i == 0) { bounds.Add(0); continue; }

            // 아직 컨테이너가 없는 탭(방금 추가)은 앞 탭 경계를 쓴다 — 배치 뒤 다시 잰다
            bounds.Add(Container(i) is { } item ? panelX + LayoutInformation.GetLayoutSlot(item).X : bounds[^1]);
        }
        return bounds;
    }

    /// 탭 i 의 오른쪽 끝(스크롤 내용 좌표).
    private double RightEdge(int index)
    {
        if (_panel is null || _scroll?.Content is not UIElement content || Container(index) is not { } item) return 0;
        return _panel.TranslatePoint(new Point(0, 0), content).X + LayoutInformation.GetLayoutSlot(item).Right;
    }

    /// 탭 전체 폭 — 끝 여백은 넣지 않는다(여백이 넘침을 만들지 않게).
    private double TabsWidth()
    {
        if (_panel is null || _scroll?.Content is not UIElement content) return 0;
        return _panel.TranslatePoint(new Point(0, 0), content).X + _panel.ActualWidth + _panel.Margin.Right;
    }

    /// 넘침 · 끝 여백 · 버튼을 다시 잰다. 탭 폭이나 머리글 폭이 바뀔 때만 부른다 — 여백 자체가 바뀌어도 다시 부르지 않아 반복이 없다.
    private void Measure(IReadOnlyList<double> bounds)
    {
        if (_area is null || _scroll is null || _spacer is null || _left is null || _right is null || _list is null) return;

        var tabs = TabsWidth();

        // 버튼을 뺀 머리글 전체 폭으로 판정한다. 뷰포트로 판정하면 같은 폭에서 버튼이 있을 때도 없을 때도 스스로 맞는다 [실측 — D-083]
        IsOverflowing = Items.Count > 0 && tabs > _area.ActualWidth + 0.5;
        var visibility = IsOverflowing ? Visibility.Visible : Visibility.Collapsed;
        if (_left.Visibility != visibility)
        {
            _left.Visibility = visibility;
            _right.Visibility = visibility;
            _list.Visibility = visibility;
        }

        var viewport = _scroll.ViewportWidth;
        _lastStart = 0;
        var spacer = 0.0;

        if (IsOverflowing && viewport > 0 && bounds.Count > 0)
        {
            // 끝까지 넘겼을 때 맨 왼쪽에 올 탭: 그 뒤가 줄에 다 들어가는 첫 탭. 없으면(마지막 탭이 줄보다 넓다) 마지막 탭.
            _lastStart = bounds.Count - 1;
            for (var i = 0; i < bounds.Count; i++)
            {
                if (tabs - bounds[i] <= viewport + 0.5)
                {
                    _lastStart = i;
                    break;
                }
            }

            // 그 탭을 왼쪽 끝에 둘 수 있게 모자란 만큼 뒤에 빈 칸을 둔다
            spacer = Math.Max(0, viewport - (tabs - bounds[_lastStart]));
        }

        if (Math.Abs(_spacer.Width - spacer) > 0.5 || double.IsNaN(_spacer.Width)) _spacer.Width = spacer;
    }

    private void UpdateButtons()
    {
        if (_left is null || _right is null) return;

        _left.IsEnabled = _start > 0;
        _right.IsEnabled = _start < _lastStart;
    }

    /// 맨 왼쪽 탭을 <paramref name="index"/> 로 둔다. 모든 넘김이 여기를 거친다.
    private void ScrollTo(int index, IReadOnlyList<double>? bounds = null)
    {
        if (_scroll is null) return;

        bounds ??= TabBoundaries();
        if (bounds.Count == 0)
        {
            _start = 0;
            _anchor = null;
            _scroll.ScrollToHorizontalOffset(0);
            UpdateButtons();
            return;
        }

        _start = Math.Clamp(index, 0, _lastStart);
        _anchor = Items[_start];
        _scroll.ScrollToHorizontalOffset(bounds[_start]);
        UpdateButtons();
    }

    /// <summary>
    /// 탭 n 개만큼 넘긴다. 지금 오프셋이 아니라 <b>목표</b>(맨 왼쪽 탭 순번)에서 센다 —
    /// 오프셋 적용은 배치까지 미뤄져, 한 처리기에서 두 번 부르면 한 칸만 갔다 [실측 — 계획 검토].
    /// </summary>
    public void Step(int tabs)
    {
        if (!IsOverflowing) return;
        ScrollTo(_start + tabs);
    }

    /// <summary>
    /// 이 탭이 다 보이게 넘긴다. 왼쪽에 잘렸으면 그 탭을 왼쪽 끝에, 오른쪽에 잘렸으면 다 보일 때까지 가장 적게.
    /// 다 보여도 오프셋이 경계가 아니면 맞춘다. 줄보다 넓은 탭은 그 탭을 왼쪽 끝에.
    /// </summary>
    public void Reveal(TabItem item)
    {
        if (_scroll is null) return;

        var bounds = TabBoundaries();
        Measure(bounds);

        var index = ItemContainerGenerator.IndexFromContainer(item);
        if (index < 0) return;

        if (!IsOverflowing)
        {
            ScrollTo(0, bounds);
            return;
        }

        var viewport = _scroll.ViewportWidth;
        var target = _start;

        if (index < _start) target = index;
        else if (viewport > 0)
        {
            var right = RightEdge(index);
            while (target < index && right - bounds[target] > viewport + 0.5) target++;
        }

        ScrollTo(target, bounds);
    }

    /// 선택된 탭이 보이게 넘긴다 — 배치가 끝난 뒤에. 새 탭은 추가 직후 폭이 0 이다 [실측].
    public void RevealSelected()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (SelectedItem is not null && ItemContainerGenerator.ContainerFromItem(SelectedItem) is TabItem item) Reveal(item);
            else
            {
                Measure(TabBoundaries());
                UpdateButtons();
            }
        });
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        RevealSelected();
    }

    /// <summary>
    /// WPF 는 탭이 포커스를 받으면(클릭 · Home/End · 화살표) 스스로 BringIntoView 를 불러 <b>픽셀 단위로 가장 적게</b> 넘긴다 —
    /// 왼쪽 탭이 잘린 채 남는다 [실측 — 계획 검토]. 패널에서 막고 경계 맞춤으로 대신한다.
    /// </summary>
    private void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
        if (FindTabItem(e.TargetObject) is not { } item) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Reveal(item));
    }

    private TabItem? FindTabItem(DependencyObject? node)
    {
        while (node is not null and not TabItem) node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        return node is TabItem item && ItemContainerGenerator.IndexFromContainer(item) >= 0 ? item : null;
    }

    /// 머리글 폭이 바뀌었다(창 크기 · 버튼이 보이거나 숨음). 다시 재고 선택 탭을 보이게.
    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        RevealSelected();
    }

    /// <summary>
    /// 탭 폭이 바뀌었다(제목 · 열기 · 닫기). 맨 왼쪽 탭(기준 탭)을 그대로 왼쪽 끝에 둔다 —
    /// 화면 밖 왼쪽 탭이 넓어지면 보이는 탭이 모두 밀리고 앞 탭 ✕ 조각이 드러났다 [실측 — 계획 검토, 그때는 제목 뒤 ● 도 폭을 바꿨다].
    /// 기준 탭이 닫혔으면 같은 자리의 탭.
    /// </summary>
    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var bounds = TabBoundaries();
        Measure(bounds);

        var index = _anchor is null ? -1 : Items.IndexOf(_anchor);
        ScrollTo(index >= 0 ? index : Math.Min(_start, Math.Max(0, Items.Count - 1)), bounds);
    }

    /// <summary>
    /// 탭 순서가 바뀌었다(끌어 놓기 — Tabs.Move). 폭의 합도 선택도 그대로라 위의 어느 훅도 돌지 않는다.
    /// 맨 왼쪽 탭 앞뒤로 탭이 옮겨지면 오프셋이 경계에서 벗어나 앞 탭 ✕ 조각이 드러나고, 기준 탭이 옮긴 탭에 남아
    /// 다음 폭 변화(이름 바꾸기 · 탭 열기) 때 줄이 몇 칸 뛰었다 [실측 — 적대 검토].
    /// 맨 왼쪽 순번은 그대로 두고, 그 자리의 새 탭으로 경계와 기준 탭을 다시 맞춘다 — 옛 기준 탭을 따라가면 방금 놓은 탭이 밀려난다.
    /// 끝 여백과 끝까지 넘겼을 때의 자리도 다시 잰다 — 둘은 폭의 합이 아니라 순서에 따른 경계로 정해져, 그대로 두면
    /// 끝까지 넘길 때 왼쪽 끝이 경계를 벗어나거나 마지막 탭이 잘린 채 ▶ 가 꺼졌다 [실측 — 2차 검토].
    /// </summary>
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (e.Action != NotifyCollectionChangedAction.Move) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var bounds = TabBoundaries();
            Measure(bounds);
            ScrollTo(_start, bounds);
        });
    }

    /// <summary>
    /// 휠 한 칸 = 탭 하나. 정밀 휠의 작은 값은 120 이 될 때까지 모은다. 빠른 휠(240)은 두 칸.
    /// 넘침이 없으면 손대지 않는다 — 안쪽 ScrollViewer 가 받고 아무 일도 하지 않는다(지금과 같음) [실측].
    /// </summary>
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsOverflowing) return;

        if (_wheel != 0 && Math.Sign(_wheel) != Math.Sign(e.Delta)) _wheel = 0;
        _wheel += e.Delta;

        var steps = _wheel / Mouse.MouseWheelDeltaForOneLine;
        if (steps != 0)
        {
            _wheel -= steps * Mouse.MouseWheelDeltaForOneLine;
            Step(-steps);                    // 위로 굴리면 왼쪽
        }

        e.Handled = true;
    }

    // ── ▾ 목록 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ▾ 목록 메뉴를 만든다(열지 않는다). 제목은 템플릿으로 그린다 — 문자열 Header 는 '_' 를 단축키로 먹는다 [실측].
    /// 고르면 <see cref="TabPicked"/>. MenuItem 의 Click 은 메뉴가 닫힌 뒤로 미뤄져 올라온다 [실측 — IL].
    /// </summary>
    public ContextMenu BuildTabListMenu(IEnumerable entries)
    {
        var menu = new ContextMenu
        {
            ItemsSource = entries,
            ItemTemplate = TabListItemTemplate,
            PlacementTarget = _list,
            Placement = PlacementMode.Custom,
        };
        menu.CustomPopupPlacementCallback = (popup, target, _) =>
            [new CustomPopupPlacement(RightAlignedPlacement(popup, target), PopupPrimaryAxis.Horizontal)];
        menu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { DataContext: { } entry }) TabPicked?.Invoke(this, entry);
        }));
        return menu;
    }

    /// ▾ 버튼 오른쪽 끝에 메뉴 오른쪽 끝을 맞춘다(버튼 바로 아래).
    public static Point RightAlignedPlacement(Size popup, Size target) => new(target.Width - popup.Width, target.Height);

    private void OpenTabList()
    {
        var entries = TabListProvider?.Invoke() ?? Items;
        BuildTabListMenu(entries).IsOpen = true;
    }

    // ── 끌기 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 이 자리(TabStrip 좌표)에 놓으면 어느 탭의 앞·뒤인가. 탭 위가 아니면 null(빈 칸 · 버튼).
    /// 좌표는 캐시하지 않는다 — 넘기면 탭이 움직인다.
    /// </summary>
    public (TabItem Item, bool After)? InsertionTargetAt(Point point)
    {
        if (InputHitTest(point) is not DependencyObject hit || FindTabItem(hit) is not { } item) return null;

        var x = TranslatePoint(point, item).X;
        return (item, x > item.ActualWidth / 2);
    }

    /// <summary>
    /// 끄는 중 커서가 탭 줄 위에 있다. 탭 끌기일 때만, 가장자리(양 끝 24px · 버튼 위)에 머물면 0.35초마다 탭 하나 넘긴다.
    /// 타이머가 이미 돌면 다시 시작하지 않는다 — DragOver 마다 다시 세면 커서를 움직이는 동안 한 번도 넘어가지 않는다 [추정 — 계획 검토].
    /// </summary>
    public void DragHover(Point point, IDataObject data)
    {
        _leavePending = false;

        if (!data.GetDataPresent(DragFormat))
        {
            StopDragTimer();
            return;
        }

        _lastDragPoint = point;
        _dragDirection = EdgeDirection(point);

        if (_dragDirection == 0)
        {
            StopDragTimer();
            return;
        }

        if (_dragTimer is { IsEnabled: true }) return;

        _dragTimer ??= new DispatcherTimer(DispatcherPriority.Input, Dispatcher);
        _dragTimer.Interval = DragScrollInterval;
        _dragTimer.Tick -= OnDragTimerTick;
        _dragTimer.Tick += OnDragTimerTick;
        _dragTimer.Start();
    }

    public bool IsDragScrolling => _dragTimer is { IsEnabled: true };

    private int EdgeDirection(Point point)
    {
        if (_scroll is null || !IsOverflowing) return 0;

        var view = BoundsOf(_scroll);
        if (point.Y < view.Top || point.Y > view.Bottom) return 0;

        // ◀ 위면 왼쪽 — 버튼 묶음을 통째로 오른쪽 끝으로 보면 ◀ 위에서 반대로 갔다 (사용자 판정 J-B).
        // 세로는 버튼이 아니라 머리글 높이로 본다 — 버튼 위 마진 2px 띠에서는 오른쪽으로 넘겼다 [실측 — 2차 검토]
        if (_left is { IsVisible: true } && BoundsOf(_left) is var left && point.X >= left.Left && point.X < left.Right) return -1;
        if (point.X >= view.Left && point.X < view.Left + EdgeZone) return -1;
        if (point.X > view.Right - EdgeZone) return +1;      // 오른쪽 24px 과 ▶ · ▾ 위
        return 0;
    }

    private Rect BoundsOf(FrameworkElement element) => element.TransformToVisual(this).TransformBounds(new Rect(element.RenderSize));

    private void OnDragTimerTick(object? sender, EventArgs e) => DragTick();

    /// <summary>
    /// 자동 넘김 한 번. 넘긴 뒤(배치 뒤) 마지막 커서 자리로 삽입선을 다시 그린다 — 삽입선은 탭을 따라 움직이므로
    /// 그대로 두면 커서 밑이 아니라 옛 탭을 가리킨다 [추정 — 계획 검토]. 시험은 이것을 직접 부른다.
    /// </summary>
    public void DragTick()
    {
        if (_dragDirection == 0) return;

        Step(_dragDirection);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_dragDirection == 0) return;      // 그 사이 끌기가 끝났다
            if (InsertionTargetAt(_lastDragPoint) is { } target) ShowInsertion(target.Item, target.After);
            else HideInsertion();
        });
    }

    /// <summary>
    /// DragLeave. 줄 안 자식 사이를 지날 때도 올라온다(Bubble) [실측 — IL] — 커서가 줄을 떠났을 때만 멈춘다.
    /// 이 이벤트의 좌표로는 가를 수 없다 — 창 밖으로 나가면 직전 대상의 (0,0), 다른 놓을 곳으로 바로 넘어가면 그 대상 기준 좌표가 와
    /// 줄 안으로 읽혔다 [실측 — 적대 검토, 실제 OleDropTarget]. 그래서 미뤄 판정한다: 줄 안 다른 자식으로 넘어갔으면
    /// 같은 OLE 호출 안에서 그 자식의 DragEnter 가 올라와 취소한다(<see cref="DragHover"/>). 취소되지 않았으면 줄을 떠난 것이다.
    /// </summary>
    public void DragLeft()
    {
        _leavePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_leavePending) return;

            _leavePending = false;
            StopDragTimer();
            HideInsertion();
        });
    }

    /// 끌기가 끝났다(놓음 · 취소). DoDragDrop 의 finally 에서도 부른다 — 놓친 경로가 있어도 타이머가 계속 넘기지 않게.
    public void DragEnded()
    {
        StopDragTimer();
        HideInsertion();
    }

    private void StopDragTimer()
    {
        _dragTimer?.Stop();
        _dragDirection = 0;
    }

    /// <summary>
    /// 삽입선을 탭 줄 <b>안쪽 층</b>(스크롤 영역의 AdornerLayer)에 붙인다 — 창 전체 층에 붙이면 반쯤 가려진 탭의 선이
    /// 버튼·트리 위에 그려진다 [실측]. 선의 높이·Y 는 대상 탭에서 읽는다(선택 탭만 2px 크다).
    /// </summary>
    public void ShowInsertion(TabItem target, bool after)
    {
        if (_panel is null || AdornerLayer.GetAdornerLayer(_panel) is not { } layer) return;

        if (_insertion is null)
        {
            _insertion = new TabInsertionAdorner(_panel);
            layer.Add(_insertion);
        }

        var origin = target.TranslatePoint(new Point(0, 0), _panel);
        _insertion.Show(new Rect(origin, new Size(target.ActualWidth, target.ActualHeight)), after);
    }

    public void HideInsertion()
    {
        if (_insertion is null) return;

        AdornerLayer.GetAdornerLayer(_insertion.AdornedElement)?.Remove(_insertion);
        _insertion = null;
    }
}
