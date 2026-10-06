using TextBean.Models;
using TextBean.Services;

namespace TextBean.Tests;

public class DocumentStoreTests
{
    private static DocumentStore NewStore(TempVault vault)
        => new(new TreeService(vault.Root), TestKeys.Codec());

    private static string? Decrypt(string path)
        => TestKeys.Codec().Decrypt(File.ReadAllBytes(path)).Text;

    [Fact]
    public async Task 새_문서는_생성_즉시_유효한_빈_문서다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "정보3.tbx");

        await store.CreateAsync(path);

        Assert.True(File.Exists(path));                       // 파일이 없으면 F5에 노드가 사라진다
        var result = await store.LoadAsync(path);
        Assert.Equal(DocumentReadStatus.Ok, result.Status);   // 0바이트면 여기서 영구 잠긴다
        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task 저장_왕복()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        await store.SaveAsync(path, "DB_PASS=8Qm");

        Assert.Equal("DB_PASS=8Qm", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task 스냅샷은_찍은_시점의_내용을_담는다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "연 시점의 내용");

        store.CaptureOpenSnapshot(path);
        await store.SaveAsync(path, "그 뒤에 망친 내용");

        var snapshot = store.SnapshotPathIfExists(path);
        Assert.NotNull(snapshot);
        Assert.Equal("연 시점의 내용", Decrypt(snapshot));
        Assert.Equal("그 뒤에 망친 내용", (await store.LoadAsync(path)).Text);
    }

    [Fact]
    public async Task 저장을_여러_번_해도_스냅샷은_한_벌이고_바뀌지_않는다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "연 시점");
        store.CaptureOpenSnapshot(path);

        for (var i = 0; i < 20; i++) await store.SaveAsync(path, $"{i}번째 자동 저장");

        // 세대를 쌓으면 바꾼 옛 비밀값이 계속 남는다 (D-016)
        var folder = PathRules.HistoryFolderFor(vault.Root, path);
        Assert.Single(Directory.GetFiles(folder));
        Assert.Equal("연 시점", Decrypt(store.SnapshotPathIfExists(path)!));
    }

    [Fact]
    public async Task 다시_열면_스냅샷이_그때_시점으로_갱신된다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        await store.SaveAsync(path, "첫 번째 세션");
        store.CaptureOpenSnapshot(path);
        await store.SaveAsync(path, "두 번째 세션");
        store.CaptureOpenSnapshot(path);          // 다시 연 상황

        Assert.Equal("두 번째 세션", Decrypt(store.SnapshotPathIfExists(path)!));
    }

    [Fact]
    public async Task 아직_연_적_없는_문서는_스냅샷이_없다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "신규.tbx");

        await store.CreateAsync(path);

        Assert.Null(store.SnapshotPathIfExists(path));
    }

    [Fact]
    public async Task 부모_폴더가_없으면_저장에_실패하고_폴더를_만들지_않는다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "사라진폴더", "a.tbx");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => store.SaveAsync(path, "x"));

        // 조용히 되살리면, 상위 폴더가 이름변경된 뒤 저장할 때 옛 이름 위치에 파일이 생긴다
        Assert.False(Directory.Exists(Path.Combine(vault.Root, "사라진폴더")));
    }

    [Fact]
    public async Task 저장에_실패해도_원본과_임시파일이_남지_않는다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);
        await store.SaveAsync(path, "원본 내용");

        File.SetAttributes(path, FileAttributes.ReadOnly);     // 교체를 실패시킨다
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(path, "덮어쓸 내용"));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Equal("원본 내용", (await store.LoadAsync(path)).Text);   // 원본 보존
        Assert.Empty(Directory.GetFiles(vault.Root, "*.tmp"));           // 임시파일 잔재 없음
    }

    [Fact]
    public async Task 문서가_아닌_파일은_Ok가_아니다()
    {
        using var vault = new TempVault();
        var path = vault.WriteRaw("가짜.tbx", "그냥 텍스트"u8.ToArray());

        Assert.Equal(DocumentReadStatus.NotTextBeanDocument, (await NewStore(vault).LoadAsync(path)).Status);
    }

    [Fact]
    public async Task 없는_파일을_열면_ReadFailed이고_Ok가_아니다()
    {
        using var vault = new TempVault();

        var result = await NewStore(vault).LoadAsync(Path.Combine(vault.Root, "없다.tbx"));

        Assert.Equal(DocumentReadStatus.ReadFailed, result.Status);
    }

    [Fact]
    public async Task 점유된_파일을_열면_ReadFailed이고_Ok가_아니다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        // 백업 소프트웨어가 잠깐 점유한 상황.
        // 여기서 Ok가 나오면 빈 편집기가 원본을 덮는다.
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(DocumentReadStatus.ReadFailed, (await store.LoadAsync(path)).Status);
    }

    [Fact]
    public async Task 루트_밖_경로는_로드도_저장도_거부한다()
    {
        using var vault = new TempVault();
        var store = NewStore(vault);
        var outside = Path.Combine(vault.Root, @"..\밖.tbx");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.LoadAsync(outside));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(outside, "x"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.CreateAsync(outside));
        Assert.Throws<UnauthorizedAccessException>(() => store.CaptureOpenSnapshot(outside));
        Assert.Throws<UnauthorizedAccessException>(() => store.SnapshotPathIfExists(outside));
    }
}
