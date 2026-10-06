using TextBean.Services;

namespace TextBean.Tests;

public class AppSettingsServiceTests
{
    [Fact]
    public async Task 저장하고_다시_읽으면_루트가_유지된다()
    {
        using var vault = new TempVault();
        var file = Path.Combine(vault.Root, "settings.json");

        var first = new AppSettingsService(file);
        await first.LoadAsync();
        first.Current.RootPath = @"D:\Secrets";
        await first.SaveAsync();

        var second = new AppSettingsService(file);
        await second.LoadAsync();

        Assert.Equal(@"D:\Secrets", second.Current.RootPath);
    }

    [Fact]
    public async Task 파일이_없으면_기본값으로_시작한다()
    {
        using var vault = new TempVault();

        var service = new AppSettingsService(Path.Combine(vault.Root, "없음.json"));
        await service.LoadAsync();

        Assert.Null(service.Current.RootPath);
    }

    [Fact]
    public async Task 깨진_json이어도_기동한다()
    {
        using var vault = new TempVault();
        var file = vault.WriteRaw("settings.json", "{ 이건 json이 아님 "u8.ToArray());

        var service = new AppSettingsService(file);
        await service.LoadAsync();      // 던지면 앱이 아예 못 뜬다

        Assert.Null(service.Current.RootPath);
    }

    [Fact]
    public void 제안_기본_루트는_문서폴더_아래_TextBean()
    {
        var service = new AppSettingsService(Path.Combine(Path.GetTempPath(), "x.json"));

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TextBean"),
            service.SuggestedDefaultRoot);
    }

    /// 트레이 첫 안내를 봤는지(D-096). 이 키가 생기기 전 파일은 안 본 것으로 읽고, 다른 값은 그대로 둔다.
    [Fact]
    public async Task 트레이_안내_기록이_유지되고_이_키가_없던_파일은_안_본_것으로_읽는다()
    {
        using var vault = new TempVault();
        var file = vault.WriteRaw("settings.json", "{ \"RootPath\": \"D:\\\\Secrets\" }"u8.ToArray());

        var old = new AppSettingsService(file);
        await old.LoadAsync();
        Assert.False(old.Current.TrayNoticeShown);

        old.Current.TrayNoticeShown = true;
        await old.SaveAsync();

        var again = new AppSettingsService(file);
        await again.LoadAsync();
        Assert.True(again.Current.TrayNoticeShown);
        Assert.Equal(@"D:\Secrets", again.Current.RootPath);
    }
}
