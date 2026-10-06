using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 금고 안 폴더가 링크(정션)로 바뀌어도 금고 밖에 쓰거나 옮기거나 지우지 않는다 (block-reparse-escape).
/// 모든 시험은 공격을 실제로 재현한다 (LL-084) — 진짜 정션을 만들어 금고 밖 임시 폴더를 가리키게 하고,
/// 명령 뒤에 금고 밖 표식이 그대로인지 본다.
///
/// 원인은 두 갈래다. ① 스캔 뒤 밖에서 폴더가 정션으로 바뀜(트리가 오래됨)
/// ② .history · .trash 는 트리에 없어서 링크인지 아무도 보지 않음 — F5 직후여도 금고 밖이 지워졌다 [실측].
/// </summary>
public class ReparseEscapeTests
{
    private const string LinkStop = "링크로 바뀌어 있어 멈췄습니다";
    private const string AppAreaHint = "새로고침으로는 풀리지 않습니다";

    private static readonly byte[] Marker = [0x54, 0x42, 0x21];

    private static DocumentStore StoreFor(string root) => new(new TreeService(root), TestKeys.Codec());

    private static async Task<(ShellViewModel Shell, FakeDialogs Dlg)> Build(TempVault vault)
    {
        var (shell, dlg, _) = await ShellFixture.BuildAsync(vault);
        await shell.RefreshAsync();
        return (shell, dlg);
    }

    private static async Task Select(ShellViewModel shell, string name)
    {
        shell.Selected = ShellFixture.FindNode(shell.Roots, name);
        await ShellFixture.Settle(shell);      // 문서면 여기서 열린다
    }

    private static string[] Everything(string folder)
        => Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories);

    // ── ① 스캔 뒤 폴더가 정션으로 바뀜 ────────────────────────────────────────

    [Fact]
    public async Task 정션으로_바뀐_폴더에_새_폴더를_만들지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        var (shell, dlg) = await Build(vault);
        await Select(shell, "업무");

        links.Replace(work, outside.Root);
        dlg.PromptTextResult = "새폴더";
        shell.NewFolderCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Empty(Everything(outside.Root));
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
        Assert.Contains("'업무'", dlg.LastErrorMessage);
        Assert.Contains("F5", dlg.LastErrorMessage);
    }

    [Fact]
    public async Task 정션으로_바뀐_폴더에_새_문서를_만들지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        var (shell, dlg) = await Build(vault);
        await Select(shell, "업무");

        links.Replace(work, outside.Root);
        dlg.PromptTextResult = "새문서";
        shell.NewDocumentCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Empty(Everything(outside.Root));
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    [Fact]
    public async Task 정션으로_바뀐_폴더의_열린_문서를_저장하지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        await StoreFor(vault.Root).CreateAsync(Path.Combine(work, "문서.tbx"));
        var outsideDoc = outside.WriteRaw("문서.tbx", Marker);
        var (shell, dlg) = await Build(vault);
        await Select(shell, "문서");
        var tab = Assert.Single(shell.Tabs);

        tab.Text = "바뀐 내용";
        links.Replace(work, outside.Root);
        shell.SaveCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Equal(Marker, File.ReadAllBytes(outsideDoc));
        Assert.Single(Everything(outside.Root));                 // .tmp 도 남기지 않았다
        Assert.True(tab.IsDirty);                                // 저장됐다고 속이지 않는다
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    [Fact]
    public async Task 정션으로_바뀐_폴더의_문서를_열지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        await StoreFor(vault.Root).CreateAsync(Path.Combine(work, "문서.tbx"));

        // 금고 밖에 같은 이름의 유효한 문서가 있다 — 열리면 금고 밖 내용이 탭에 뜬다
        var outsideDoc = Path.Combine(outside.Root, "문서.tbx");
        var outsideStore = StoreFor(outside.Root);
        await outsideStore.CreateAsync(outsideDoc);
        await outsideStore.SaveAsync(outsideDoc, "금고 밖 값");

        var (shell, dlg) = await Build(vault);
        links.Replace(work, outside.Root);
        await Select(shell, "문서");

        Assert.Empty(shell.Tabs);
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    [Fact]
    public async Task 정션으로_바뀐_폴더로_드래그해도_옮기지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await StoreFor(vault.Root).CreateAsync(doc);
        var (shell, dlg) = await Build(vault);

        links.Replace(work, outside.Root);
        await shell.DropAsync(doc, work);

        Assert.True(File.Exists(doc));
        Assert.Empty(Everything(outside.Root));
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    // ── ② 트리에 없는 .history · .trash ───────────────────────────────────────

    /// 가장 위험한 경로. 예전에는 폴더를 휴지통으로 보낸 뒤 이력 .history\업무 를 재귀 삭제했고,
    /// .history 가 정션이면 금고 밖의 같은 이름 폴더가 통째로 지워졌다 [실측].
    [Fact]
    public async Task history가_정션이면_폴더를_지워도_금고_밖_폴더가_살아있다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        var outsideMarker = outside.WriteRaw(@"업무\표식.bin", Marker);
        links.Create(Path.Combine(vault.Root, ".history"), outside.Root);
        var (shell, dlg) = await Build(vault);
        await Select(shell, "업무");

        shell.DeleteCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Equal(Marker, File.ReadAllBytes(outsideMarker));
        // 반쪽 상태가 없다 — 본체도 휴지통으로 가지 않았다
        Assert.True(Directory.Exists(work));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".trash")));
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
        Assert.Contains("'.history'", dlg.LastErrorMessage);
        Assert.Contains(AppAreaHint, dlg.LastErrorMessage);        // F5 로는 안 풀린다고 알린다
    }

    [Fact]
    public async Task history가_정션이면_이름을_바꾸지_않고_금고_밖_이력도_그대로다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await StoreFor(vault.Root).CreateAsync(doc);
        var outsideMarker = outside.WriteRaw(@"문서.tbx\표식.bin", Marker);
        links.Create(Path.Combine(vault.Root, ".history"), outside.Root);
        var (shell, dlg) = await Build(vault);
        await Select(shell, "문서");

        dlg.PromptTextResult = "새이름";
        shell.RenameCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.True(File.Exists(doc));
        Assert.False(File.Exists(Path.Combine(vault.Root, "새이름.tbx")));
        Assert.Equal(Marker, File.ReadAllBytes(outsideMarker));
        Assert.False(Directory.Exists(Path.Combine(outside.Root, "새이름.tbx")));
        Assert.Equal("이름 변경 실패", dlg.LastErrorTitle);
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    /// .history 자체가 아니라 그 아래 구간이 정션이어도 잡는다 — 조상 어느 구간이든 본다.
    [Fact]
    public void history_아래_구간이_정션이어도_휴지통으로_보내지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var tree = new TreeService(vault.Root);
        var doc = vault.WriteRaw(@"업무\문서.tbx", Marker);
        var outsideMarker = outside.WriteRaw(@"문서.tbx\표식.bin", Marker);
        vault.Dir(".history");
        links.Create(Path.Combine(vault.Root, ".history", "업무"), outside.Root);

        var ex = Assert.Throws<LinkedPathException>(() => tree.MoveToTrash(doc, "20260929-120000"));

        Assert.True(File.Exists(doc));
        Assert.Equal(Marker, File.ReadAllBytes(outsideMarker));
        Assert.Contains(@"'.history\업무'", ex.Message);
    }

    [Fact]
    public async Task trash가_정션이면_삭제해도_금고_밖으로_옮기지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await StoreFor(vault.Root).CreateAsync(doc);
        links.Create(Path.Combine(vault.Root, ".trash"), outside.Root);
        var (shell, dlg) = await Build(vault);
        await Select(shell, "문서");

        shell.DeleteCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.True(File.Exists(doc));
        Assert.Empty(Everything(outside.Root));
        Assert.Contains("'.trash'", dlg.LastErrorMessage);
        Assert.Contains(AppAreaHint, dlg.LastErrorMessage);
    }

    [Fact]
    public async Task trash가_정션이면_휴지통을_비워도_금고_밖이_그대로다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var outsideMarker = outside.WriteRaw("표식.bin", Marker);
        links.Create(Path.Combine(vault.Root, ".trash"), outside.Root);
        var (shell, dlg) = await Build(vault);

        shell.EmptyTrashCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Equal(Marker, File.ReadAllBytes(outsideMarker));
        // 금고 밖 파일 수로 "N개 영구 삭제" 확인 창을 띄우지 않는다 — 세기 전에 막는다
        Assert.Equal(0, dlg.ConfirmCount);
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    /// 금고 밖이 비어 있으면 예전에는 "휴지통이 비어 있습니다" 로 끝나 링크라는 사실을 끝내 몰랐다.
    [Fact]
    public async Task trash가_빈_폴더를_가리키는_정션이어도_링크라고_알린다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        links.Create(Path.Combine(vault.Root, ".trash"), outside.Root);
        var (shell, dlg) = await Build(vault);

        shell.EmptyTrashCommand.Execute(null);

        Assert.Equal(0, dlg.ConfirmCount);
        Assert.Contains(AppAreaHint, dlg.LastErrorMessage);
    }

    // ── 금고 밖 파일을 금고 안으로 끌어오지 않는다 ───────────────────────────────

    /// 옮길 원본이 링크 너머에 있으면 금고 밖 파일이 금고 안으로 옮겨져 금고 밖에서 사라진다.
    [Fact]
    public async Task 정션_너머_문서를_드래그해도_금고_밖_파일을_끌어오지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        var other = vault.Dir("다른폴더");
        await StoreFor(vault.Root).CreateAsync(Path.Combine(work, "문서.tbx"));
        var outsideDoc = outside.WriteRaw("문서.tbx", Marker);
        var (shell, dlg) = await Build(vault);

        links.Replace(work, outside.Root);
        await shell.DropAsync(Path.Combine(work, "문서.tbx"), other);

        Assert.Equal(Marker, File.ReadAllBytes(outsideDoc));
        Assert.False(File.Exists(Path.Combine(other, "문서.tbx")));
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    /// 휴지통으로 끌어온 뒤 비우면 금고 밖 파일이 영구히 지워진다.
    [Fact]
    public async Task 정션_너머_문서를_삭제해도_금고_밖_파일을_휴지통으로_끌어오지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var work = vault.Dir("업무");
        await StoreFor(vault.Root).CreateAsync(Path.Combine(work, "문서.tbx"));
        var outsideDoc = outside.WriteRaw("문서.tbx", Marker);
        var (shell, dlg) = await Build(vault);
        await Select(shell, "문서");

        links.Replace(work, outside.Root);
        shell.DeleteCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Equal(Marker, File.ReadAllBytes(outsideDoc));
        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".trash")));
        Assert.Contains(LinkStop, dlg.LastErrorMessage);
    }

    /// 드래그 중 커서 판정(CheckMove)이 링크 너머 하위 전체를 매번 열거하지 않는다.
    /// 열거하면 금고 밖 트리 깊이가 판정에 섞인다 [실측 — 172 → 781].
    [Fact]
    public void 드래그_중_경로_길이_판정이_링크_너머를_읽지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var tree = new TreeService(vault.Root);
        var work = vault.Dir("업무");
        var before = tree.DeepestDerivedLength(work, "00000000-000000");
        outside.WriteRaw(Path.Combine(new string('가', 150), new string('나', 150), "깊은.tbx"), Marker);

        links.Replace(work, outside.Root);

        Assert.Equal(before, tree.DeepestDerivedLength(work, "00000000-000000"));
    }

    /// 휴지통 비우기 확인 창의 "N개 문서를 영구 삭제" 가 금고 밖 파일까지 세면 안 된다.
    /// SearchOption.AllDirectories 는 정션을 따라 들어갔다 [실측].
    [Fact]
    public void 휴지통_안의_정션은_세지도_따라가_지우지도_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var tree = new TreeService(vault.Root);
        foreach (var name in new[] { "a.bin", "b.bin", "c.bin" }) outside.WriteRaw(name, Marker);
        vault.WriteRaw(@".trash\20260929-120000\버린.tbx", Marker);
        links.Create(Path.Combine(vault.Root, ".trash", "20260929-120000", "링크"), outside.Root);

        Assert.Equal(1, tree.CountTrashItems());

        // 안쪽 정션은 재귀 삭제가 따라가지 않는다 [실측]. 첫 시도가 예외로 끝나는 일이 있어 삼킨다 —
        // 여기서 보는 것은 금고 밖이 무사한지다.
        try { tree.EmptyTrash(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        Assert.Equal(3, Directory.GetFiles(outside.Root).Length);
    }

    /// 스냅샷을 못 남겼다고 문서를 못 열게 하지 않는다 (D-037) — 원래 규칙이다.
    [Fact]
    public async Task history가_정션이어도_문서는_열리고_스냅샷만_안_남는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        await StoreFor(vault.Root).CreateAsync(Path.Combine(vault.Root, "문서.tbx"));
        links.Create(Path.Combine(vault.Root, ".history"), outside.Root);
        var (shell, dlg) = await Build(vault);

        await Select(shell, "문서");

        Assert.Single(shell.Tabs);
        Assert.Equal(0, dlg.ErrorCount);
        Assert.Empty(Everything(outside.Root));
    }

    [Fact]
    public async Task history가_정션이면_되돌릴_지점_보기가_이유를_알린다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        await StoreFor(vault.Root).CreateAsync(Path.Combine(vault.Root, "문서.tbx"));
        outside.WriteRaw(@"문서.tbx\opened.tbx", Marker);
        links.Create(Path.Combine(vault.Root, ".history"), outside.Root);
        var (shell, dlg) = await Build(vault);
        await Select(shell, "문서");

        shell.OpenBackupCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Single(shell.Tabs);                                // 금고 밖 스냅샷이 탭으로 뜨지 않았다
        Assert.Contains(AppAreaHint, dlg.LastErrorMessage);
    }

    // ── 오판하지 않는다 ───────────────────────────────────────────────────────

    /// 금고를 일부러 링크 뒤에 두는 것은 정상 사용이다. 루트 자신은 보지 않는다.
    [Fact]
    public async Task 금고_루트_자체가_정션이면_모든_동작이_정상이다()
    {
        using var real = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(outside.Root);
        var linkRoot = Path.Combine(outside.Root, "금고링크");
        links.Create(linkRoot, real.Root);
        var tree = new TreeService(linkRoot);
        var store = new DocumentStore(tree, TestKeys.Codec());

        var folder = tree.CreateFolder(linkRoot, "폴더");
        var doc = Path.Combine(folder, "문서.tbx");
        await store.CreateAsync(doc);
        await store.SaveAsync(doc, "값");
        Assert.True((await store.LoadAsync(doc)).IsOk);
        store.CaptureOpenSnapshot(doc);
        Assert.NotNull(store.SnapshotPathIfExists(doc));
        var renamed = tree.Rename(doc, "새이름");
        tree.MoveToTrash(renamed, "20260929-120000");
        Assert.Equal(1, tree.CountTrashItems());
        tree.EmptyTrash();

        Assert.Equal(0, tree.CountTrashItems());
        Assert.True(Directory.Exists(Path.Combine(real.Root, "폴더")));
    }

    /// 없는 경로의 DirectoryInfo.Attributes 는 -1 이라 링크처럼 보인다 [실측]. 그 함정에 걸리면
    /// 새로 만드는 모든 것이 거부된다.
    [Fact]
    public void 아직_없는_하위_경로는_링크로_오판하지_않는다()
    {
        using var vault = new TempVault();
        var tree = new TreeService(vault.Root);

        tree.EnsureInsideRoot(Path.Combine(vault.Root, "없는1", "없는2", "문서.tbx"));
        var created = tree.CreateFolder(Path.Combine(vault.Root, "없는1"), "새폴더");

        Assert.True(Directory.Exists(created));
    }

    /// 링크가 가리키는 곳은 금고 밖 정보다. 알림에는 금고 안 링크 자리만 싣는다.
    [Fact]
    public void 알림에_금고_밖_경로를_싣지_않는다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        links.Create(Path.Combine(vault.Root, "업무"), outside.Root);
        var tree = new TreeService(vault.Root);

        var ex = Assert.Throws<LinkedPathException>(() => tree.EnsureInsideRoot(Path.Combine(vault.Root, "업무", "문서.tbx")));

        Assert.DoesNotContain(outside.Root, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Q-1: 금고 재지정 창은 금고가 정말 없을 때만 ─────────────────────────────

    [Fact]
    public async Task 지워진_폴더에_새_문서를_만들면_금고_재지정_창을_띄우지_않는다()
    {
        using var vault = new TempVault();
        var work = vault.Dir("업무");
        var (shell, dlg) = await Build(vault);
        await Select(shell, "업무");

        Directory.Delete(work);
        dlg.PromptTextResult = "새문서";
        shell.NewDocumentCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Equal(0, dlg.PickFolderCount);
        Assert.NotEqual("금고 폴더 없음", dlg.LastErrorTitle);
        Assert.Contains("이 항목을 찾을 수 없습니다", dlg.LastErrorMessage);
        Assert.False(Directory.Exists(work));                    // 폴더를 되살리지도 않았다
    }

    /// 금고를 링크 뒤에 둔 경우(정상 사용) 그 대상이 사라지면 Directory.Exists 는 링크를 보고 참이다 [실측].
    /// 그대로 두면 재지정 창 대신 "F5" 안내가 떠 사용자가 빈 금고를 보고 데이터가 사라졌다고 오해한다.
    [Fact]
    public void 링크_뒤_금고의_대상이_사라지면_금고가_없다고_본다()
    {
        using var real = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(outside.Root);
        var linkRoot = Path.Combine(outside.Root, "금고링크");
        links.Create(linkRoot, real.Root);
        var tree = new TreeService(linkRoot);
        Assert.True(tree.RootExists());

        Directory.Delete(real.Root, recursive: true);

        Assert.False(tree.RootExists());
    }

    [Fact]
    public async Task 금고_자체가_없어지면_여전히_재지정_창을_띄운다()
    {
        using var vault = new TempVault();
        var (shell, dlg) = await Build(vault);
        dlg.CancelPickFolder = true;

        Directory.Delete(vault.Root, recursive: true);
        shell.RefreshCommand.Execute(null);
        await ShellFixture.Settle(shell);

        Assert.Equal("금고 폴더 없음", dlg.LastErrorTitle);
        Assert.Equal(1, dlg.PickFolderCount);
    }
}
