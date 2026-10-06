using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TextBean.Views.Platform;

/// <summary>
/// 탭을 끌 때 놓일 자리를 세로선으로 보여준다.
/// 표시가 없으면 어디에 놓이는지 알 수 없어 놓아보고 다시 옮기게 된다.
///
/// Adorner 로 그린다 — 머리글 안에 선을 끼워 넣으려면 TabItem 의 ControlTemplate 을
/// 다시 써야 하는데, ContentPresenter 를 빠뜨리면 문서명·상태 기호·닫기 버튼이 통째로
/// 사라지고 빌드는 그대로 통과한다 (LL-014). 바깥 틀(TabControl 템플릿)은 TabStrip 이 다시 쓴다 —
/// 이 선은 TabStrip 이 탭 패널(스크롤 영역 안쪽 층)에 붙여, 탭 줄 밖에는 그려지지 않는다 (D-074 · plan §3-10).
/// </summary>
public sealed class TabInsertionAdorner : Adorner
{
    public TabInsertionAdorner(UIElement layerHost) : base(layerHost)
    {
        // 놓을 자리를 마우스 밑에서 찾는다 — 선이 그 판정을 가로막으면 안 된다
        IsHitTestVisible = false;
    }

    private static readonly Pen Line = CreatePen();

    private Rect _target;
    private bool _after;
    private bool _visible;

    private static Pen CreatePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x1D, 0x9E, 0x75)), 2.5);
        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// 선을 그릴 자리를 정한다. 높이·Y 는 대상 탭에서 그대로 받는다 —
    /// 선택된 탭만 2px 크고 2px 위에 있어 상수로 잡으면 선이 어긋난다 [실측].
    /// </summary>
    public void Show(Rect tabBounds, bool after)
    {
        if (_visible && _after == after && _target == tabBounds) return;

        _target = tabBounds;
        _after = after;
        _visible = true;
        InvalidateVisual();
    }

    public void Hide()
    {
        if (!_visible) return;

        _visible = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_visible) return;

        var x = _after ? _target.Right : _target.Left;
        drawingContext.DrawLine(Line, new Point(x, _target.Top), new Point(x, _target.Bottom));
    }
}
