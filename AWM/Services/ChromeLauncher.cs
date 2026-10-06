using System.Diagnostics;
using System.IO;
using AWM.Services.Interfaces;
using Microsoft.Win32;

namespace AWM.Services;

/// <summary>
/// 사용자가 크롬을 골랐다(add-naver-blog-format D-001). 기본 브라우저로 대신 열지 않는다.
/// </summary>
public sealed class ChromeLauncher : IBrowserLauncher
{
    // 사용자 입력이 들어가지 않는 고정 주소 하나만 넘긴다 (D-006)
    public const string NaverWriteUrl = "https://blog.naver.com/GoBlogWrite.naver";

    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";

    public void OpenNaverWrite()
    {
        var chrome = FindChrome()
                     ?? throw new InvalidOperationException(
                         $"크롬을 찾지 못했습니다. 찾아본 곳: HKCU·HKLM\\{AppPathsKey}");

        // 이름(chrome)으로 부르지 않고 전체 경로로 — 다른 같은 이름 파일이 대신 실행되지 않게
        var start = new ProcessStartInfo(chrome) { UseShellExecute = false };
        start.ArgumentList.Add(NaverWriteUrl);
        using var _ = Process.Start(start);
    }

    /// <summary>
    /// 설치 프로그램이 등록한 App Paths 값. 사용자 설치(HKCU)를 먼저 본다. 실제 파일이 있을 때만.
    /// </summary>
    public static string? FindChrome()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(AppPathsKey);
            if (key?.GetValue(null) is not string value)
                continue;

            var path = value.Trim().Trim('"');
            if (Path.IsPathFullyQualified(path) && File.Exists(path))
                return path;
        }
        return null;
    }
}
