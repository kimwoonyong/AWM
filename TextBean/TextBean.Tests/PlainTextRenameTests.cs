using TextBean.Services;

namespace TextBean.Tests;

/// <summary>
/// 이름 변경이 원본 확장자를 보존하는지 고정한다.
/// 예전 코드는 파일이면 무조건 .tbx 를 붙였다. 평문 .txt 를 고르고 아무것도 고치지 않고
/// 확인만 눌러도 .tbx 가 됐고, 그 파일은 매직 검사에 걸려 앱 안에서 영영 못 열렸다.
/// 내용은 평문 그대로인데 트리에는 자물쇠 아이콘이 붙어, 사용자는 내용이 사라졌다고 읽는다.
/// </summary>
public class PlainTextRenameTests
{
    private const string Stamp = "20260923-143000";

    private static byte[] FakeDoc => [(byte)'T', (byte)'B', (byte)'X', (byte)'1', 1, 9, 9, 9];

    private static byte[] Plain => "서버 접속 정보"u8.ToArray();

    [Fact]
    public void 평문_이름변경은_확장자를_보존한다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("메모.txt", Plain);

        var renamed = new TreeService(vault.Root).Rename(doc, "메모2");

        Assert.Equal(Path.Combine(vault.Root, "메모2.txt"), renamed);
        Assert.Equal(Plain, File.ReadAllBytes(renamed));
    }

    /// <summary>
    /// 대화상자 초기값이 확장자를 뗀 이름이라, 사용자가 아무것도 고치지 않고 확인만 누르면
    /// 원래 이름 그대로가 들어온다. 그때도 확장자가 바뀌면 안 된다 — 이것이 실제 파괴 경로였다.
    /// </summary>
    [Fact]
    public void 이름을_안_고치고_확인만_눌러도_평문이_살아남는다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("메모.txt", Plain);

        var renamed = new TreeService(vault.Root).Rename(doc, "메모");

        Assert.Equal(doc, renamed);
        Assert.True(File.Exists(doc));
        Assert.False(File.Exists(Path.Combine(vault.Root, "메모.tbx")));
        Assert.Equal(Plain, File.ReadAllBytes(doc));
    }

    /// 금고 문서 쪽 동작은 달라지지 않아야 한다.
    [Fact]
    public void 금고문서_이름변경은_여전히_tbx다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("정보1.tbx", FakeDoc);

        var renamed = new TreeService(vault.Root).Rename(doc, "정보2");

        Assert.Equal(Path.Combine(vault.Root, "정보2.tbx"), renamed);
    }

    [Fact]
    public void 대소문자가_다른_확장자도_그대로_보존한다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("메모.TXT", Plain);

        var renamed = new TreeService(vault.Root).Rename(doc, "메모2");

        Assert.Equal(Path.Combine(vault.Root, "메모2.TXT"), renamed);
    }

    /// <summary>
    /// 이름에 확장자를 직접 치면 두 번 붙는 것은 이 변경 전부터 있던 동작이다.
    /// 이번 태스크 범위 밖이므로 "바뀌지 않았다"를 고정해 둔다 — 조용히 손대면
    /// "확장자를 앱이 마음대로 뗀다"가 되어 더 나쁜 혼동을 만든다.
    /// </summary>
    [Fact]
    public void 이름에_확장자를_직접_치면_두_번_붙는_것은_그대로다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw("메모.tbx", FakeDoc);

        var renamed = new TreeService(vault.Root).Rename(doc, "메모.txt");

        Assert.Equal(Path.Combine(vault.Root, "메모.txt.tbx"), renamed);
    }

    [Fact]
    public void 폴더_이름변경은_안의_평문을_그대로_데려간다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"폴더1\메모.txt", Plain);

        var renamed = new TreeService(vault.Root).Rename(Path.Combine(vault.Root, "폴더1"), "폴더2");

        Assert.Equal(Path.Combine(vault.Root, "폴더2"), renamed);
        Assert.Equal(Plain, File.ReadAllBytes(Path.Combine(vault.Root, "폴더2", "메모.txt")));
    }

    [Fact]
    public void 이동과_삭제는_평문_확장자를_보존한다()
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);
        var target = vault.Dir("폴더1");
        var doc = vault.WriteRaw("메모.txt", Plain);

        var moved = service.Move(doc, target);
        Assert.Equal(Path.Combine(target, "메모.txt"), moved);

        service.MoveToTrash(moved, Stamp);
        Assert.True(File.Exists(Path.Combine(vault.Root, ".trash", Stamp, @"폴더1\메모.txt")));
    }
}
