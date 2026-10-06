using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// .history 동반 처리가 파일 경로에만 구현돼 있던 반쪽 상태(E-1·E-2·E-3)에 대한 회귀 테스트.
/// opened.tbx 는 D-016이 "유일한 되돌릴 지점"으로 지정한 파일이라 사라지면 복구 수단이 없다.
/// </summary>
public class HistoryIntegrityTests
{
    private static byte[] FakeDoc => [(byte)'T', (byte)'B', (byte)'X', (byte)'1', 1, 9, 9, 9];

    [Fact]
    public void 같은_이름으로_이름변경해도_복구지점이_사라지지_않는다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("정보.tbx", FakeDoc);
        vault.WriteRaw(@".history\정보.tbx\opened.tbx", FakeDoc);
        var service = new TreeService(vault.Root);

        // 이름 입력창에서 이름을 안 바꾸고 확인만 누른 상황.
        // 예전에는 이력을 먼저 지운 뒤 Move 가 실패해 복구지점이 사라졌다 [실측].
        service.Rename(doc, "정보");

        Assert.True(File.Exists(Path.Combine(vault.Root, ".history", "정보.tbx", "opened.tbx")));
        Assert.True(File.Exists(doc));
    }

    [Fact]
    public void 폴더_이름변경은_그_안_문서들의_이력을_데려간다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"폴더1\정보.tbx", FakeDoc);
        vault.WriteRaw(@".history\폴더1\정보.tbx\opened.tbx", FakeDoc);
        var service = new TreeService(vault.Root);

        service.Rename(Path.Combine(vault.Root, "폴더1"), "폴더2");

        Assert.True(File.Exists(Path.Combine(vault.Root, ".history", "폴더2", "정보.tbx", "opened.tbx")));

        // 고아로 남으면 바꾸기 전 값이 앱에 안 보이는 채 복호화 가능한 상태로 남는다
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history", "폴더1")));
    }

    [Fact]
    public void 폴더_삭제는_그_안_문서들의_이력도_함께_버린다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"폴더1\정보.tbx", FakeDoc);
        vault.WriteRaw(@".history\폴더1\정보.tbx\opened.tbx", FakeDoc);
        var service = new TreeService(vault.Root);

        service.MoveToTrash(Path.Combine(vault.Root, "폴더1"), "20260921-120000");

        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history", "폴더1")));
    }

    [Fact]
    public void 목적지에_이력이_있으면_덮지_않는다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("A.tbx", FakeDoc);
        vault.WriteRaw(@".history\A.tbx\opened.tbx", FakeDoc);
        vault.WriteRaw("B.tbx", FakeDoc);
        vault.WriteRaw(@".history\B.tbx\opened.tbx", "B의 복구지점"u8.ToArray());
        var service = new TreeService(vault.Root);

        // 이름 충돌이므로 본체 이동 단계에서 이미 실패해야 하고,
        // 그 전에 B의 이력이 지워져서는 안 된다
        Assert.ThrowsAny<IOException>(() => service.Rename(doc, "B"));

        Assert.Equal("B의 복구지점",
            File.ReadAllText(Path.Combine(vault.Root, ".history", "B.tbx", "opened.tbx")));
    }
}
