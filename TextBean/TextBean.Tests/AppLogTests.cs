using TextBean.Services;

namespace TextBean.Tests;

public class AppLogTests : IDisposable
{
    // AppLog.Directory 는 static 이다. 테스트가 바꾼 값을 되돌려 놓지 않으면
    // 이후 테스트의 로그가 엉뚱한 곳으로 가거나 조용히 버려진다.
    private readonly string _originalDirectory = AppLog.Directory;

    public void Dispose() => AppLog.Directory = _originalDirectory;

    [Fact]
    public void 로그에_예외_메시지가_들어가지_않는다()
    {
        using var vault = new TempVault();
        AppLog.Directory = vault.Root;

        // 예외 메시지에 복호화된 값이 섞여 들어오는 상황을 흉내낸다.
        // 메시지를 그대로 찍는 코드가 값이 새는 경로다 (PROHIBITED-CUSTOM-04).
        AppLog.Error("save", @"C:\Vault\a.tbx", new InvalidOperationException("DB_PASS=8Qm!vT2r"));

        var text = string.Join("\n", Directory.EnumerateFiles(vault.Root, "*.log").Select(File.ReadAllText));
        Assert.DoesNotContain("8Qm!vT2r", text);
        Assert.Contains("InvalidOperationException", text);   // 타입은 남아야 진단이 된다
        Assert.Contains(@"C:\Vault\a.tbx", text);             // 경로도 남아야 한다
    }

    [Fact]
    public void 로그_디렉터리를_못_만들어도_던지지_않는다()
    {
        AppLog.Directory = @"Z:\없는드라이브\logs";

        AppLog.Warn("x", null, null);   // 로그 실패가 앱 동작을 막으면 안 된다
    }
}
