using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TextBean.Services;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// MainWindow.xaml 의 편집기 바인딩을 실제 WPF TextBox 로 재현한다.
/// Text="{Binding Editor.Text, UpdateSourceTrigger=PropertyChanged}" (TextBox 는 기본이 TwoWay)
/// 문서를 로드한 직후 TextBox 가 값을 되돌려 써서 IsDirty 가 다시 켜지는지가 쟁점이다.
/// </summary>
public class EditorBindingEchoTests
{
    [Fact]
    public void 문서를_로드한_직후_IsDirty가_다시_켜지지_않는다()
    {
        var result = RunOnSta(() =>
        {
            using var vault = new TempVault();
            var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
            var editor = new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());

            var path = Path.Combine(vault.Root, "a.tbx");
            store.CreateAsync(path).GetAwaiter().GetResult();
            store.SaveAsync(path, "저장된 내용").GetAwaiter().GetResult();

            // MainWindow.xaml 과 같은 바인딩을 실제 TextBox 에 건다
            var box = new TextBox();
            BindingOperations.SetBinding(box, TextBox.TextProperty, new Binding("Text")
            {
                Source = editor,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            editor.LoadAsync(path).GetAwaiter().GetResult();
            PumpDispatcher();

            return (editor.Text, box.Text, editor.IsDirty);
        });

        Assert.Equal("저장된 내용", result.Item1);
        Assert.Equal("저장된 내용", result.Item2);   // TextBox 에 실제로 표시되는가
        Assert.False(result.Item3);                  // 갓 연 문서가 수정됨이면 매번 저장을 묻는다
    }

    [Fact]
    public void 저장_직후에도_IsDirty가_다시_켜지지_않는다()
    {
        var isDirty = RunOnSta(() =>
        {
            using var vault = new TempVault();
            var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
            var editor = new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer());

            var path = Path.Combine(vault.Root, "a.tbx");
            store.CreateAsync(path).GetAwaiter().GetResult();

            var box = new TextBox();
            BindingOperations.SetBinding(box, TextBox.TextProperty, new Binding("Text")
            {
                Source = editor,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            editor.LoadAsync(path).GetAwaiter().GetResult();
            PumpDispatcher();

            // 사용자가 타이핑한 것과 같은 경로: TextBox 가 소스를 갱신한다
            box.Text = "사용자가 입력한 값";
            PumpDispatcher();
            Assert.True(editor.IsDirty, "타이핑하면 수정됨이 되어야 한다");

            editor.TrySaveAsync().GetAwaiter().GetResult();
            PumpDispatcher();

            return editor.IsDirty;
        });

        Assert.False(isDirty);   // 저장했는데 수정됨이 남으면 저장 버튼이 계속 켜져 있고 또 묻는다
    }

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

    /// 바인딩 갱신이 디스패처 큐에 쌓였다면 여기서 처리된다.
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
