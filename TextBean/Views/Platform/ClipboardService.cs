using System.Windows;
using System.Windows.Threading;
using TextBean.Services.Interfaces;

namespace TextBean.Views.Platform;

/// <summary>
/// WPF에 의존하므로 Services/ 가 아니라 Views/ 아래 둔다 (D-013, PROHIBITED-ARCH-02).
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    // J-04 확정: 30초 상수. 설정으로 노출하지 않는다.
    private static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(30);

    // 클립보드 API는 STA 전용. 풀 스레드 타이머로 깨우면 ThreadStateException이
    // 조용히 나고 자동 비움이 영영 동작하지 않는다.
    private readonly DispatcherTimer _timer;
    private string? _lastCopied;

    public ClipboardService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ClearAfter };
        _timer.Tick += (_, _) => { _timer.Stop(); ClearIfOurs(); };
    }

    public void Copy(string text)
    {
        var data = new DataObject();
        data.SetText(text);

        // 이 플래그가 없으면 복호화한 평문이 Win+V 기록과 클라우드 클립보드에 남아
        // 암호화가 통째로 무의미해진다 (D-007). 값 타입은 Task 9에서 실측 확정한다.
        data.SetData("CanIncludeInClipboardHistory", false);
        data.SetData("CanUploadToCloudClipboard", false);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", false);

        if (!TryClipboard(() => Clipboard.SetDataObject(data, copy: true))) return;

        _lastCopied = text;
        _timer.Stop();
        _timer.Start();
    }

    public void ClearIfOurs()
    {
        if (_lastCopied is null) return;

        TryClipboard(() =>
        {
            // 값을 확인하지 않고 지우면 그 사이 사용자가 복사한 남의 내용을 날린다
            if (Clipboard.ContainsText() && Clipboard.GetText() == _lastCopied) Clipboard.Clear();
        });

        _lastCopied = null;
    }

    private static bool TryClipboard(Action action)
    {
        // 다른 앱이 클립보드를 점유하면 예외가 난다. 짧게 재시도하되 UI를 동기 대기로 막지 않는다.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception) when (attempt < 2)
            {
                Thread.Yield();
            }
            catch (Exception)
            {
                // 조용히 삼키지 않는다 — 사용자는 복사됐다고 믿고 붙여넣는다
                MessageBox.Show("클립보드를 다른 프로그램이 사용 중입니다. 잠시 후 다시 시도해주세요.",
                    "복사 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
        return false;
    }

    public void Dispose() => _timer.Stop();
}
