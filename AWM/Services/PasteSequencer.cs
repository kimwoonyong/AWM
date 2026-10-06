using System.Diagnostics;
using AWM.Models;
using AWM.Services.Interfaces;

namespace AWM.Services;

public enum PasteOutcome
{
    Completed,
    NotTargetWindow,
    ModifiersHeld,
    WindowChanged,
    Escaped,
    ClipboardFailed,
}

public sealed record PasteResult(PasteOutcome Outcome, int Done, int Total);

/// <summary>
/// 조각마다 기다리는 시간. 그림은 업로드가 끝나기 전에 다음 조각을 붙이면 순서가 꼬일 수 있어 넉넉히 둔다 [추정] (D-008).
/// </summary>
public sealed record PasteTimings(TimeSpan Text, TimeSpan ImageBase, TimeSpan ImagePerFile, TimeSpan ModifierTimeout, TimeSpan Poll)
{
    public static PasteTimings Default { get; } = new(
        TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50));
}

/// <summary>
/// 사용자가 단축키를 누른 창(크롬 네이버 글쓰기)에 글·그림 조각을 차례로 붙인다 (D-003).
/// 앱은 화면을 읽거나 로그인·발행하지 않는다 — 클립보드에 넣고 Ctrl+V 를 보낼 뿐이다.
/// UI 스레드에서 부른다 — WPF 클립보드는 STA 에서만 되므로 기다린 뒤에도 같은 스레드로 돌아와야 한다(ConfigureAwait 를 붙이지 않는다).
/// </summary>
public sealed class PasteSequencer(IClipboardService clipboard, IKeyboardSender keyboard, PasteTimings timings,
    string targetProcess = "chrome")
{
    public async Task<PasteResult> RunAsync(IReadOnlyList<PasteSegment> segments, string title)
    {
        // 단축키를 누른 그 창에만 붙인다 (D-009)
        var target = keyboard.ForegroundWindow();
        if (!string.Equals(keyboard.ProcessNameOf(target), targetProcess, StringComparison.OrdinalIgnoreCase))
            return new PasteResult(PasteOutcome.NotTargetWindow, 0, segments.Count);

        // Ctrl·Alt 가 아직 눌려 있으면 Ctrl+V 가 Ctrl+Alt+V 로 들어간다 (R-2)
        var held = Stopwatch.StartNew();
        while (keyboard.AreModifiersDown())
        {
            if (held.Elapsed > timings.ModifierTimeout)
                return new PasteResult(PasteOutcome.ModifiersHeld, 0, segments.Count);
            await Task.Delay(timings.Poll);
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var (set, wait) = segments[i] switch
            {
                TextSegment text => (clipboard.TrySetHtml(text.Body.Html, text.Body.PlainText), timings.Text),
                ImageSegment images => (clipboard.TrySetFiles(images.Paths),
                    timings.ImageBase + timings.ImagePerFile * images.Paths.Count),
                _ => throw new UnreachableException(),
            };
            if (!set)
                return new PasteResult(PasteOutcome.ClipboardFailed, i, segments.Count);

            // 사용자가 다른 창으로 갔으면 그 창에 붙이지 않는다
            if (keyboard.ForegroundWindow() != target)
                return new PasteResult(PasteOutcome.WindowChanged, i, segments.Count);

            keyboard.SendPaste();

            if (!await WaitAsync(wait))
                return new PasteResult(PasteOutcome.Escaped, i + 1, segments.Count);
        }

        // 제목 칸은 커서를 옮길 수 없어 사람이 붙인다
        clipboard.TrySetText(title);
        return new PasteResult(PasteOutcome.Completed, segments.Count, segments.Count);
    }

    /// <summary>
    /// 기다리는 동안 Esc 를 누르면 false.
    /// </summary>
    private async Task<bool> WaitAsync(TimeSpan duration)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < duration)
        {
            if (keyboard.IsEscapeDown())
                return false;
            await Task.Delay(timings.Poll);
        }
        return !keyboard.IsEscapeDown();
    }
}
