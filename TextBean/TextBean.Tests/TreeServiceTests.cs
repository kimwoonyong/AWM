using TextBean.Services;

namespace TextBean.Tests;

public class TreeServiceTests
{
    private const string Stamp = "20260921-143000";

    private static byte[] FakeDoc => [(byte)'T', (byte)'B', (byte)'X', (byte)'1', 1, 9, 9, 9];

    /// <summary>
    /// 예전 규칙은 "폴더와 .tbx 만 싣는다" 였고 그 문장이 테스트 이름에 그대로 박혀 있었다.
    /// add-txt-readonly 에서 평문 참고 파일(.txt)을 허용 목록에 더하면서 규칙이 바뀌었다.
    /// 바뀐 것은 허용 목록의 내용뿐이고, "허용 목록이지 제외 목록이 아니다"는 그대로다 —
    /// .bak 과 그 밖의 확장자는 여전히 빠진다.
    /// </summary>
    [Fact]
    public async Task 스캔은_폴더와_tbx와_txt만_싣는다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"폴더1\정보1.tbx", FakeDoc);
        vault.WriteRaw(@"폴더1\메모.txt", [1]);
        vault.WriteRaw(@"폴더1\대문자.TXT", [1]);
        vault.WriteRaw(@"폴더1\정보1.tbx.bak", FakeDoc);
        vault.WriteRaw(@"폴더1\노트.md", [1]);
        vault.WriteRaw(@"폴더1\임시.txt.tmp", [1]);
        vault.WriteRaw(@".trash\20260101-000000\옛문서.tbx", FakeDoc);
        vault.WriteRaw(@".history\폴더1\정보1.tbx\20260101-000000-000.tbx", FakeDoc);

        var root = await new TreeService(vault.Root).ScanAsync();

        var folder = Assert.Single(root.Children, c => c.IsFolder);
        Assert.Equal("폴더1", folder.Name);

        // 확장자를 감추는 규칙은 금고에 한 종류만 있을 때 성립했다. 평문은 드러낸다.
        Assert.Equal(["대문자.TXT", "메모.txt", "정보1"], folder.Children.Select(c => c.Name).Order().ToArray());

        Assert.DoesNotContain(root.Children, c => c.Name == ".trash");
        Assert.DoesNotContain(root.Children, c => c.Name == ".history");
    }

    [Fact]
    public async Task 평문_노드만_평문으로_표시된다()
    {
        using var vault = new TempVault();
        vault.WriteRaw("문서.tbx", FakeDoc);
        vault.WriteRaw("메모.txt", [1]);

        var root = await new TreeService(vault.Root).ScanAsync();

        Assert.True(Assert.Single(root.Children, c => c.Name == "메모.txt").IsPlainText);
        Assert.False(Assert.Single(root.Children, c => c.Name == "문서").IsPlainText);
    }

    /// <summary>
    /// '메모.txt'(평문)와 '메모.txt.tbx'(암호화)는 표시 이름이 둘 다 '메모.txt' 다.
    /// 2차 정렬 키가 없으면 비교자가 0을 돌려주고 List.Sort 는 불안정 정렬이라
    /// 새로고침마다 두 행의 위아래가 바뀐다.
    /// </summary>
    [Fact]
    public async Task 이름이_같아도_정렬_순서가_흔들리지_않는다()
    {
        using var vault = new TempVault();
        vault.WriteRaw("메모.txt", [1]);
        vault.WriteRaw("메모.txt.tbx", FakeDoc);
        var service = new TreeService(vault.Root);

        var first = (await service.ScanAsync()).Children.Select(c => c.FullPath).ToArray();
        var second = (await service.ScanAsync()).Children.Select(c => c.FullPath).ToArray();

        Assert.Equal(["메모.txt", "메모.txt"], (await service.ScanAsync()).Children.Select(c => c.Name).ToArray());
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task 재파스_포인트는_따라가지_않는다()
    {
        using var vault = new TempVault();
        // 링크를 먼저 끊는다. 루트를 가리키는 정션을 남긴 채 TempVault 가 재귀 삭제하면
        // 첫 시도가 예외로 끝나 '안쪽' 폴더가 실행마다 하나씩 남았다 [실측].
        using var links = new Junctions(vault.Root);
        var inner = vault.Dir("안쪽");
        var linkPath = Path.Combine(inner, "위로");

        // 심볼릭 링크는 개발자 모드나 관리자 권한이 필요해 이 PC에서는 만들 수 없다.
        // 정션은 일반 권한으로 만들어지고 똑같이 ReparsePoint 플래그가 붙으므로 정션으로 검증한다.
        // (조용히 건너뛰면 보호 장치가 미검증인 채 통과한다 — LL-010. Create 가 만들어졌는지 확인한다)
        links.Create(linkPath, vault.Root);

        // 따라가면 루트 → 안쪽 → 위로 → 루트 … 로 무한 재귀한다. 끝나는 것 자체가 판정이다.
        var root = await new TreeService(vault.Root).ScanAsync();

        var innerNode = Assert.Single(root.Children);
        Assert.Empty(innerNode.Children);
    }

    [Fact]
    public void 삭제는_trash로_원래_상대경로를_보존해_옮긴다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw(@"폴더1\폴더1-1\정보1.tbx", FakeDoc);

        new TreeService(vault.Root).MoveToTrash(doc, Stamp);

        Assert.False(File.Exists(doc));
        Assert.True(File.Exists(Path.Combine(vault.Root, ".trash", Stamp, @"폴더1\폴더1-1\정보1.tbx")));
    }

    [Fact]
    public void 삭제는_그_문서의_이력도_함께_버린다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw(@"폴더1\정보1.tbx", FakeDoc);
        vault.WriteRaw(@".history\폴더1\정보1.tbx\20260921-100000-000.tbx", FakeDoc);

        new TreeService(vault.Root).MoveToTrash(doc, Stamp);

        // 남겨두면 지운 문서의 값이 .history 에 계속 복호화 가능한 상태로 남는다
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history", "폴더1", "정보1.tbx")));
    }

    [Fact]
    public void 같은_이름을_두_번_지워도_서로_덮지_않는다()
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);

        service.MoveToTrash(vault.WriteRaw(@"정보.tbx", [1]), "20260921-100000");
        service.MoveToTrash(vault.WriteRaw(@"정보.tbx", [2]), "20260921-110000");

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(vault.Root, ".trash", "20260921-100000", "정보.tbx")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(vault.Root, ".trash", "20260921-110000", "정보.tbx")));
    }

    [Fact]
    public void 폴더_삭제는_하위_전체를_통째로_옮긴다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"폴더2\폴더2-1\정보.tbx", FakeDoc);

        new TreeService(vault.Root).MoveToTrash(Path.Combine(vault.Root, "폴더2"), Stamp);

        Assert.False(Directory.Exists(Path.Combine(vault.Root, "폴더2")));
        Assert.True(File.Exists(Path.Combine(vault.Root, ".trash", Stamp, @"폴더2\폴더2-1\정보.tbx")));
    }

    [Fact]
    public void 이름변경은_이력을_함께_데려간다()
    {
        using var vault = new TempVault();
        var doc = vault.WriteRaw(@"정보1.tbx", FakeDoc);
        vault.WriteRaw(@".history\정보1.tbx\20260921-100000-000.tbx", FakeDoc);

        var renamed = new TreeService(vault.Root).Rename(doc, "정보2");

        Assert.Equal(Path.Combine(vault.Root, "정보2.tbx"), renamed);
        Assert.True(File.Exists(Path.Combine(vault.Root, ".history", "정보2.tbx", "20260921-100000-000.tbx")));

        // 고아 이력이 남으면 안 된다 — 이름을 바꾸기 전의 값이 앱에 안 보이는 채로 남는다
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history", "정보1.tbx")));
    }

    [Fact]
    public void 중복검사는_트리가_아니라_디스크를_본다()
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);

        // 스캔 이후 외부에서 복사해 넣은 상황을 흉내낸다
        vault.WriteRaw(@"API키.tbx", FakeDoc);

        Assert.True(service.NameTaken(vault.Root, "API키.tbx"));
    }

    [Fact]
    public void 루트_밖_경로는_거부한다()
    {
        using var vault = new TempVault();
        var service = new TreeService(vault.Root);

        Assert.Throws<UnauthorizedAccessException>(() => service.EnsureInsideRoot(Path.Combine(vault.Root, @"..\밖.tbx")));
    }

    [Fact]
    public void SetRoot는_같은_인스턴스의_루트를_바꾼다()
    {
        using var a = new TempVault();
        using var b = new TempVault();
        var service = new TreeService(a.Root);
        var docInB = b.WriteRaw("b문서.tbx", FakeDoc);

        // 인스턴스를 새로 만들어 갈아끼우면 DocumentStore가 붙잡은 옛 인스턴스가 그대로 남는다
        service.SetRoot(b.Root);

        Assert.Equal(PathRules.NormalizeFull(b.Root), service.Root);
        service.EnsureInsideRoot(docInB);                                     // 던지지 않아야 한다
        Assert.Throws<UnauthorizedAccessException>(() => service.EnsureInsideRoot(Path.Combine(a.Root, "a.tbx")));
    }

    [Fact]
    public async Task 루트가_사라지면_빈_트리가_아니라_예외다()
    {
        var vault = new TempVault();
        var service = new TreeService(vault.Root);
        vault.Dispose();                                                      // 밖에서 폴더를 지운 상황

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.ScanAsync());
    }

    [Fact]
    public void 휴지통_비우기는_trash만_지운다()
    {
        using var vault = new TempVault();
        vault.WriteRaw(@"남을문서.tbx", FakeDoc);
        vault.WriteRaw($@".trash\{Stamp}\지울문서.tbx", FakeDoc);
        var service = new TreeService(vault.Root);

        Assert.Equal(1, service.CountTrashItems());
        service.EmptyTrash();

        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".trash")));
        Assert.True(File.Exists(Path.Combine(vault.Root, "남을문서.tbx")));
        Assert.Equal(0, service.CountTrashItems());
    }
}
