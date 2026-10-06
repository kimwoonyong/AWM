namespace TextBean.Tests;

/// <summary>
/// 테스트마다 격리된 임시 루트 폴더를 만들고 끝나면 지운다.
/// </summary>
public sealed class TempVault : IDisposable
{
    public string Root { get; }

    public TempVault()
    {
        Root = Path.Combine(Path.GetTempPath(), "TextBeanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Dir(params string[] parts)
    {
        var path = Path.Combine([Root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteRaw(string relativePath, byte[] bytes)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    public void Dispose()
    {
        // 정리 실패는 테스트 결과에 영향을 주지 않는다
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
