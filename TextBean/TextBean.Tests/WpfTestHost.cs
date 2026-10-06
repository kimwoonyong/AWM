using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TextBean.Tests;

/// <summary>
/// 시험 프로세스의 Application. <b>App 이 아닌 빈 Application</b> 에 앱 리소스를 손으로 올린다.
/// App 을 만들면, 메시지 루프를 돌리는 스레드에서 WPF 가 App.OnStartup 을 불러 실제 설정을 다시 쓰고
/// 실제 금고 창을 띄운다 [실측 — LL-087]. App.xaml(BAML)도 읽지 않는다 — LoadComponent 는 App 을 만든다 [실측 — 계획 검토].
/// App.xaml 에 키가 늘면 여기도 늘린다. 빠뜨리면 그 키를 쓰는 화면이 배치될 때 시끄럽게 실패한다.
/// </summary>
internal static class TestApplication
{
    private static readonly Lock Gate = new();

    /// STA 스레드에서 부른다.
    public static void Ensure()
    {
        lock (Gate)
        {
            if (Application.Current is null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
            }

            if (Application.Current!.GetType() != typeof(Application))
                throw new InvalidOperationException(
                    $"시험 프로세스의 Application 이 {Application.Current.GetType().Name} 이다 — 실제 조립 루트가 돌 수 있다 (LL-087)");
        }
    }
}

/// <summary>
/// MainWindow 를 만드는 시험 클래스는 함께 돌리지 않는다. 시험 스레드마다 창을 만들면 프로세스에 하나뿐인 Application 의 리소스와
/// XAML 공유 캐시를 동시에 고쳐 "non-concurrent collections" 로 파싱이 가끔 깨졌다 [실측 — 적대 검토]. 실제 앱은 UI 스레드 하나라 상관없다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfScreenCollection
{
    public const string Name = "WPF 화면";
}

/// <summary>
/// 화면 시험 호스트. 시험마다 전용 STA 스레드에서 본문을 돌리고, 필요할 때만 메시지를 펌프한다.
/// <para>
/// 지키는 것 (D-082 · LL-087):
/// ① 모달(중첩 메시지 루프 · 스레드 모달)이 생기면 곧바로 디스패처를 내리고 실패한다. 대화상자는 뜨기 전에 막히지 않고 뜬 직후 내려간다 —
///    그래서 시험은 셸의 가짜 대화상자만 쓴다(TabScene 이 확인).
/// ② 창은 화면 밖 · 활성화하지 않음. 시험 본문은 Focus() · Activate() · Popup.IsOpen 을 부르지 않는다
///    (키보드 포커스 경로는 Win32 SetFocus 로 시험 창을 앞 창으로 만들고, 팝업은 화면 안으로 끌려와 뜬다 [실측 — IL]).
/// ③ 시간 제한을 넘으면 디스패처를 내리고 실패한다.
/// ④ PNG 는 환경 변수 TEXTBEAN_TEST_PNG 가 있을 때만 쓴다 — 평소에는 파일을 남기지 않는다.
/// </para>
/// </summary>
internal static class WpfTestHost
{
    private static readonly FieldInfo FrameDepth =
        typeof(Dispatcher).GetField("_frameDepth", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Dispatcher._frameDepth 를 찾지 못했다 — 중첩 루프 걸쇠가 동작하지 않는다");

    [ThreadStatic] private static string? _violation;

    public static void Run(Action body, int timeoutSeconds = 30) => Run(() => { body(); return 0; }, timeoutSeconds);

    public static T Run<T>(Func<T> body, int timeoutSeconds = 30)
    {
        T result = default!;
        Exception? failure = null;
        string? violation = null;
        Dispatcher? dispatcher = null;

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _violation = null;

            ComponentDispatcher.EnterThreadModal += OnThreadModal;
            dispatcher.Hooks.OperationStarted += OnOperationStarted;
            try
            {
                TestApplication.Ensure();
                result = body();
                if (CountWindows() != 0) Fail("시험이 창을 닫지 않았다");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                violation = _violation;
                ComponentDispatcher.EnterThreadModal -= OnThreadModal;
                dispatcher.Hooks.OperationStarted -= OnOperationStarted;
                dispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            thread.Join(TimeSpan.FromSeconds(5));
            throw new TimeoutException($"화면 시험이 {timeoutSeconds}초 안에 끝나지 않았다");
        }

        // 본문이 던진 뒤 창이 남으면 걸쇠도 걸린다 — 진짜 원인을 가리지 않게 안에 넣는다
        if (violation is not null) throw new InvalidOperationException("화면 시험 걸쇠: " + violation, failure);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static void OnThreadModal(object? sender, EventArgs e) => Fail("스레드 모달(대화상자)이 열렸다");

    private static void OnOperationStarted(object? sender, DispatcherHookEventArgs e)
    {
        // 본문은 깊이 0 에서 돌고 Pump 가 1 을 만든다. 2 이상이면 누군가 ShowDialog 류의 중첩 루프를 돌린 것이다.
        if ((int)FrameDepth.GetValue(e.Dispatcher)! >= 2) Fail("중첩 메시지 루프가 생겼다");
    }

    private static void Fail(string why)
    {
        _violation ??= why;
        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    /// 지금 쌓인 일을 <paramref name="priority"/> 까지 처리한다. 배치(Loaded·Render)까지 끝내려면 기본값을 쓴다.
    public static void Pump(DispatcherPriority priority = DispatcherPriority.ContextIdle)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(priority, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        ThrowIfViolated();
    }

    /// 작업이 끝날 때까지 펌프한다. 셸의 async 메서드는 이 스레드의 동기화 문맥으로 돌아온다.
    public static T Wait<T>(Task<T> task)
    {
        Wait((Task)task);
        return task.GetAwaiter().GetResult();
    }

    public static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        ThrowIfViolated();
        task.GetAwaiter().GetResult();
    }

    private static void ThrowIfViolated()
    {
        if (_violation is not null) throw new InvalidOperationException("화면 시험 걸쇠: " + _violation);
    }

    /// 화면 밖에 활성화하지 않고 띄운다.
    public static void ShowOffscreen(Window window, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = width;
        window.Height = height;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Pump();
    }

    /// 창의 닫기 절차(저장 → 다시 닫기, LL-080)가 끝날 때까지 펌프한다.
    public static void Close(Window window)
    {
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Close();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!closed && DateTime.UtcNow < deadline) Pump(DispatcherPriority.Background);
        if (!closed) throw new InvalidOperationException("창이 닫히지 않았다");
    }

    /// 이 스레드의 창 수. Application.Windows 는 Application 을 만든 스레드에서만 읽힌다 [실측 — 계획 검토].
    public static int CountWindows()
        => PresentationSource.CurrentSources.OfType<HwndSource>()
                             .Count(s => s.Dispatcher == Dispatcher.CurrentDispatcher && s.RootVisual is Window);

    /// 환경 변수 TEXTBEAN_TEST_PNG 폴더가 있을 때만 PNG 를 남긴다 — 사람이 눈으로 확인할 때 쓴다.
    public static void Snapshot(Visual visual, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TEXTBEAN_TEST_PNG");
        if (string.IsNullOrEmpty(folder) || visual is not FrameworkElement element) return;

        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
                                            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    public static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit && (match is null || match(hit))) return hit;
            if (FindDescendant(child, match) is { } deeper) return deeper;
        }
        return null;
    }

    public static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in FindDescendants<T>(child)) yield return deeper;
        }
    }
}
