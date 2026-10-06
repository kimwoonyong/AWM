using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TextBean.Views.Dialogs;

namespace TextBean.Tests;

/// <summary>
/// 화면 XAML 이 실제로 파싱되는지. 빌드는 XAML 런타임 오류를 잡지 않는다 (LL-005 · LL-065) —
/// 이번에 키 입력창을 새로 만들고 트리 항목 스타일을 바꿨다.
/// </summary>
[Collection(WpfScreenCollection.Name)]
public class XamlSmokeTests
{
    [Fact]
    public void 키_입력창이_열리고_두_번_입력칸을_가린다()
    {
        var result = RunOnSta(() =>
        {
            var once = new KeyPromptDialog("키 입력", "안내", confirm: false);
            var twice = new KeyPromptDialog("처음 키 정하기", "안내", confirm: true);
            return (once.Entry.Confirmation, twice.Entry.Confirmation, once.Title);
        });

        Assert.Null(result.Item1);                  // 한 번 입력이면 확인값이 없다
        Assert.Equal("", result.Item2);
        Assert.Equal("키 입력", result.Item3);
    }

    /// <summary>
    /// 눈 버튼 (사용자 요청 2026-09-30). 보이는 칸도 PasswordBox 와 같은 성질이어야 한다 — 실행취소 기록 없음 · 한글 입력기 끔 ·
    /// 복사 막음. 한 번에 한 칸만 키를 든다.
    /// 한글 입력기를 실제로 켜고 쳐 보는 것은 자동 시험 밖이다(사용자 데스크톱의 입력기를 바꾸게 된다) — 속성까지만 본다.
    /// </summary>
    [Fact]
    public void 눈_버튼을_누르면_친_키가_보이고_다시_누르면_가려진다()
    {
        var result = RunOnSta(() =>
        {
            var dialog = new KeyPromptDialog("처음 키 정하기", "안내", confirm: true);
            var hidden = (PasswordBox)dialog.FindName("First");
            var shown = (TextBox)dialog.FindName("FirstShown");
            var eye = (ToggleButton)dialog.FindName("FirstEye");
            var otherShown = (TextBox)dialog.FindName("SecondShown");

            hidden.Password = "abc-1234";
            eye.IsChecked = true;
            var whileShown = (dialog.Entry.Key, shown.Text, hidden.Password, shown.Visibility, hidden.Visibility,
                              otherShown.Visibility);

            var copying = new DataObjectCopyingEventArgs(new DataObject("abc-1234"), isDragDrop: false)
            {
                RoutedEvent = DataObject.CopyingEvent
            };
            shown.RaiseEvent(copying);
            var safeguards = (copying.CommandCancelled, shown.IsUndoEnabled, InputMethod.GetIsInputMethodEnabled(shown));

            shown.Text = "abc-12345";                                  // 보이는 채로 고쳐 친다
            eye.IsChecked = false;
            var afterHide = (dialog.Entry.Key, shown.Text, hidden.Password, shown.Visibility);

            eye.IsChecked = true;
            dialog.ClearSecrets();
            var cleared = (shown.Text, hidden.Password);

            return (whileShown, safeguards, afterHide, cleared);
        });

        Assert.Equal(("abc-1234", "abc-1234", "", Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed),
                     result.whileShown);                                // 누른 칸만 보인다 · 가린 칸은 비운다
        Assert.Equal((true, false, false), result.safeguards);         // 복사 막음 · 실행취소 기록 없음 · 한글 입력기 끔
        Assert.Equal(("abc-12345", "", "abc-12345", Visibility.Collapsed), result.afterHide);
        Assert.Equal(("", ""), result.cleared);
    }

    [Fact]
    public void 메인_창_XAML이_파싱된다()
        => RunOnSta(() =>
        {
            // 변환기 리소스는 App.xaml 에 있다. App 을 만들지 않고 빈 Application 에 같은 키를 올린다 (LL-087)
            TestApplication.Ensure();
            return new MainWindow().Title;
        });

    private static T RunOnSta<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        return result;
    }
}
