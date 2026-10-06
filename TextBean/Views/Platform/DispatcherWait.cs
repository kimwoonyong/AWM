using System.Windows.Threading;

namespace TextBean.Views.Platform;

/// <summary>
/// 메시지를 펌프하며 작업이 끝나기를 제한 시간까지 기다린다. <b>PROHIBITED-CODE-01(UI 스레드 블로킹)의 유일한 예외다</b> (D-101).
/// Windows 종료(SessionEnding)와 절전 진입(PowerModeChanged)은 처리기가 돌아오면 바로 진행하는 동기 알림이라
/// await 로는 그 전에 저장을 끝낼 수 없다 — 그 둘에서만 부른다(SessionEndSave · SuspendLock).
/// 펌프하므로 화면이 얼지 않는다. 그 대신 기다리는 사이 다른 디스패처 작업이 돌 수 있다 — 부르는 쪽이 창 요청을 막고
/// 대화상자를 억제한다.
/// </summary>
public static class DispatcherWait
{
    /// 끝났으면 true, 제한 시간을 넘겼으면 false.
    public static bool Until(Task task, TimeSpan timeout)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        // 내리는 중인 디스패처에서는 프레임을 밀 수 없다(PushFrame 이 던진다)
        if (dispatcher.HasShutdownStarted) return task.IsCompleted;

        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(timeout, DispatcherPriority.Send, (_, _) => frame.Continue = false, dispatcher);
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        try
        {
            // ContinueWith 가 프레임을 밀기 전에 끝나 이미 내렸을 수 있다 — 그러면 밀지 않는다
            if (!task.IsCompleted) Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }

        return task.IsCompleted;
    }
}
