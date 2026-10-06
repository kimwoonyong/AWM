using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Views.Platform;

/// <summary>
/// Windows 종료 · 로그아웃(Application.SessionEnding) 때 App 이 부른다. App 에서 떼어 둔 것은 시험하기 위해서다 —
/// App 은 시험에서 만들 수 없다 (LL-087).
/// </summary>
public static class SessionEndSave
{
    /// <summary>
    /// 기본 처리(Shutdown)는 Closing 의 비동기 저장을 기다리지 않고 버린다 [실측 — 모의 0/2]. 그 전에 대화상자 없는 저장을
    /// 끝까지 기다린다 (D-098). 기다림이 제한 시간 안에 끝났으면 true — 저장 실패는 셸이 경로를 로그에 남긴다.
    /// </summary>
    public static bool Run(MainWindow window, ShellViewModel shell, TimeSpan timeout)
        => Run(window, shell.SaveAllQuietlyAsync, shell.SuppressDialogs, timeout);

    /// 시험이 저장을 붙잡아 "기다리는 중"을 결정적으로 만든다 — 실제 저장은 캐시 때문에 동기로 끝나기도 한다 (LL-079).
    public static bool Run(MainWindow window, Func<Task> saveQuietly, Func<IDisposable> suppressDialogs, TimeSpan timeout)
    {
        // 다시 열지 않는다 — 앱이 끝난다. 기다리는 사이 들어온 숨기기 · 보이기 · 종료 · 닫기를 받지 않는다
        window.AcceptsRequests = false;

        bool done;
        using (suppressDialogs())
        {
            var save = saveQuietly();
            _ = save.ContinueWith(t => AppLog.Error("session-save", null, t.Exception!.InnerException),
                                  TaskContinuationOptions.OnlyOnFaulted);

            done = DispatcherWait.Until(save, timeout);
        }

        if (!done) AppLog.Warn("session-save-timeout", null, null);

        // 기다린 뒤에 세운다 — 먼저 세우면 기다리는 사이 들어온 닫기가 저장 없이 통과한다
        window.ExitImmediately = true;
        return done;
    }
}
