using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

/// <summary>
/// 본문에서 찾은 자리를 전부 칠한다.
///
/// TextBox 는 선택 영역이 하나뿐이라 "모두 강조"를 기본 기능으로는 못 한다.
/// 게다가 검색창에 포커스가 가는 순간 본문의 선택 강조가 화면에서 사라져서 [실측],
/// 강조가 없으면 사용자는 어디를 찾았는지 볼 방법이 아예 없다.
///
/// ControlTemplate 을 다시 쓰지 않는다 — ContentPresenter 를 빠뜨리면 본문이 통째로
/// 비고 빌드는 그대로 통과한다 (LL-014). Adorner 는 본문 XAML 을 한 줄도 안 건드린다.
/// </summary>
public sealed class MatchHighlightAdorner(TextBox box) : Adorner(box)
{
    private static readonly Brush Fill = Frozen(Color.FromArgb(0x66, 0xEF, 0x9F, 0x27));
    private static readonly Brush CurrentFill = Frozen(Color.FromArgb(0x99, 0x1D, 0x9E, 0x75));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Adorner 는 별도 레이어라 **가려진 탭 위에도 계속 그린다** [실측].
        // 이 검사가 없으면 열어 둔 모든 탭의 강조가 한 화면에 겹쳐 찍히고,
        // 다른 문서의 일치 위치라 이 문서에서는 빈 칸에 박스가 뜬다.
        if (!box.IsVisible) return;

        if (box.DataContext is not EditorViewModel vm) return;
        if (vm.MatchLength <= 0 || vm.Matches.Count == 0) return;

        var viewport = new Rect(0, 0, box.ActualWidth, box.ActualHeight);

        for (var i = 0; i < vm.Matches.Count; i++)
        {
            var start = vm.Matches[i];
            var end = start + vm.MatchLength;
            if (end > box.Text.Length) continue;          // 본문이 줄어든 직후

            // 좌표는 뷰포트 기준이고 Rect 의 폭은 항상 0(캐럿 사각형)이라
            // 시작·끝 두 번 불러 그 차이로 폭을 만든다 [실측].
            var from = box.GetRectFromCharacterIndex(start);
            var to = box.GetRectFromCharacterIndex(end);
            if (from.IsEmpty || to.IsEmpty) continue;

            // 줄이 바뀌면 끝 좌표가 다음 줄로 내려간다. 그 경우는 줄 끝까지만 칠한다.
            var width = Math.Abs(to.Y - from.Y) < 0.5 ? to.X - from.X : box.ActualWidth - from.X;
            if (width <= 0) continue;

            var rect = new Rect(from.X, from.Y, width, from.Height);

            // 화면 밖 좌표도 그대로 돌려주므로 여기서 거른다 [실측]
            if (!viewport.IntersectsWith(rect)) continue;

            drawingContext.DrawRectangle(i == vm.CurrentMatch ? CurrentFill : Fill, null,
                                         Rect.Intersect(rect, viewport));
        }
    }
}
