using System.Runtime.CompilerServices;
using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// 시험 어셈블리가 처음 불릴 때 한 번 돈다. 앱 로그를 사용자의 실제 로그(%APPDATA%\TextBean\logs)가 아니라
/// 임시 폴더로 보낸다 — 시험이 일부러 낸 실패(저장 실패 · 링크 거부)가 실제 앱 로그에 섞였다 (D-045 · D-046 · LL-087).
/// </summary>
internal static class TestAssemblySetup
{
    private static readonly string LogsRoot = Path.Combine(Path.GetTempPath(), "TextBeanTests");

    /// <summary>
    /// 돌린 직후의 앱 로그 폴더. 시험은 지금 값이 아니라 이 값을 본다 — AppLogTests 가 같은 static 을 잠깐 다른 폴더로
    /// 바꾸므로, 함께 도는 시험이 그 틈에 지금 값을 읽으면 코드는 맞는데 실패한다 [실측 — 2차 검토].
    /// </summary>
    internal static string LogDirectory { get; private set; } = "";

    [ModuleInitializer]
    internal static void RedirectAppLog()
    {
        // 지난 실행이 끝 정리를 못 하고 죽었으면 남는다
        foreach (var stale in StaleLogFolders()) TryDelete(stale);

        var logs = Path.Combine(LogsRoot, "logs-" + Guid.NewGuid().ToString("N"));
        AppLog.Directory = logs;
        LogDirectory = AppLog.Directory;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(logs);
    }

    private static IEnumerable<string> StaleLogFolders()
    {
        try
        {
            return Directory.Exists(LogsRoot) ? Directory.GetDirectories(LogsRoot, "logs-*") : [];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
