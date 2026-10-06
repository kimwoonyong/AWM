using System.Text;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 평문을 열어도 .history 에 사본이 생기면 안 된다.
///
/// CaptureOpenSnapshot 은 내용을 보지 않고 File.Copy 한다. 평문에 걸면 숨김 폴더에
/// 암호화되지 않은 사본이 한 벌 더 생기고, 그 사본은 이름이 opened.tbx 라 매직 검사에 걸려
/// 앱이 다시 읽지도 못한다 — 만들기만 하고 못 쓰는 순수 비용이다.
/// 스냅샷의 목적은 자동 저장이 1.5초 만에 확정하는 실수를 되돌리는 것인데(D-016),
/// 읽기 전용이면 그 실수 자체가 없다.
/// </summary>
public class PlainTextHistoryTests
{
    private static byte[] PlainBytes => new UTF8Encoding(false).GetBytes("서버 접속 정보");

    private static (EditorViewModel Editor, DocumentStore Store) Build(TempVault vault)
    {
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        return (new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer()), store);
    }

    [Fact]
    public async Task 평문을_열어도_history에_아무것도_안_생긴다()
    {
        using var vault = new TempVault();
        var (editor, _) = Build(vault);

        await editor.LoadAsync(vault.WriteRaw("메모.txt", PlainBytes));

        var history = Path.Combine(vault.Root, ".history");
        Assert.False(Directory.Exists(history) && Directory.EnumerateFiles(history, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task 평문을_여러_번_열어도_마찬가지다()
    {
        using var vault = new TempVault();
        var (editor, _) = Build(vault);
        var path = vault.WriteRaw("메모.txt", PlainBytes);

        await editor.LoadAsync(path);
        await editor.LoadAsync(path);
        await editor.LoadAsync(path);

        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history")));
    }

    /// 대조군. 금고 문서는 여전히 연 시점 상태를 남겨야 한다 (D-016).
    [Fact]
    public async Task 대조군_금고문서는_연_시점_상태를_남긴다()
    {
        using var vault = new TempVault();
        var (editor, store) = Build(vault);
        var path = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(path);

        await editor.LoadAsync(path);

        Assert.True(File.Exists(Path.Combine(vault.Root, ".history", "문서.tbx", "opened.tbx")));
    }

    /// <summary>
    /// 평문에는 되돌릴 지점이 없다. "이 문서를 아직 연 적이 없습니다"는 .txt 에 영원히 거짓이므로
    /// 그 문구를 그대로 쓰면 안 된다 — 셸 쪽 분기가 이 사실에 기댄다.
    /// </summary>
    [Fact]
    public async Task 평문에는_되돌릴_지점_자체가_없다()
    {
        using var vault = new TempVault();
        var (editor, store) = Build(vault);
        var path = vault.WriteRaw("메모.txt", PlainBytes);

        await editor.LoadAsync(path);

        Assert.Null(store.SnapshotPathIfExists(path));
    }

    /// <summary>
    /// 판정 불가 파일도 읽기에 실패하므로 스냅샷 경로를 타지 않는다.
    /// 여기서 새면 이진 파일의 첫 바이트들이 .history 에 남는다.
    /// </summary>
    [Fact]
    public async Task 판정_불가_파일도_사본을_남기지_않는다()
    {
        using var vault = new TempVault();
        var (editor, _) = Build(vault);

        await editor.LoadAsync(vault.WriteRaw("이진.txt", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]));

        Assert.False(Directory.Exists(Path.Combine(vault.Root, ".history")));
    }
}
