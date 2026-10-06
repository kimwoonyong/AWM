using System.Diagnostics;

namespace TextBean.Tests;

/// <summary>
/// 시험용 정션. 이 PC 에서는 심볼릭 링크를 만들 권한이 없지만 정션은 권한 없이 된다 [실측].
///
/// 정리는 <b>링크만</b> 지운다 — 대상 내용은 건드리지 않는다. 만든 링크뿐 아니라
/// <paramref name="sweepRoots"/> 아래에서 찾은 링크도 지운다: 시험 대상 코드가 링크를 옮겨 놓았을 수 있다.
/// 링크를 남긴 채 금고를 재귀 삭제해도 따라가지 않는 것은 실측했지만, 시험 정리를 그 성질에 걸지 않는다.
/// using 선언은 역순으로 해제되므로 TempVault 들보다 <b>뒤에</b> 선언한다.
/// </summary>
public sealed class Junctions(params string[] sweepRoots) : IDisposable
{
    private readonly List<string> _links = [];

    /// 폴더를 지우고 같은 이름의 정션으로 바꿔치기한다 — "스캔 뒤 밖에서 바뀐 폴더"를 결정적으로 만든다.
    public void Replace(string folder, string target)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Create(folder, target);
    }

    public void Create(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // DirectoryInfo.Attributes 로 확인하지 않는다 — 없는 경로가 -1 이라 링크로 보인다
        if (process.ExitCode != 0 || !IsLink(link))
            throw new InvalidOperationException($"정션을 만들지 못했다: {link} ({error.Trim()})");

        _links.Add(link);
    }

    public static bool IsLink(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public void Dispose()
    {
        foreach (var link in _links) RemoveLink(link);

        foreach (var root in sweepRoots) SweepLinks(root);
    }

    // 링크 안으로 들어가지 않고 걷는다. 링크를 만나면 그 링크만 지운다.
    private static void SweepLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string[] children;
            try { children = Directory.GetDirectories(pending.Pop()); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var child in children)
            {
                if (IsLink(child)) RemoveLink(child);
                else pending.Push(child);
            }
        }
    }

    // 정션에 비재귀 삭제는 링크 자체를 지운다. 대상이 비어 있지 않아도 된다.
    private static void RemoveLink(string link)
    {
        try
        {
            if (IsLink(link)) Directory.Delete(link, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
