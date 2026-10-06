using System.Text;
using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// .txt 고치기 (add-txt-editing, D-117~D-120). 판정 축은 디스크 바이트다 — 고친 곳 말고는
/// 인코딩 · BOM · 줄바꿈이 원래 파일과 같아야 한다.
/// </summary>
public class PlainTextEditTests
{
    static PlainTextEditTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static (EditorViewModel Editor, DocumentStore Store) Build(TempVault vault)
    {
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        return (new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), new FakeAutoSaveTimer()), store);
    }

    private static async Task<byte[]> EditAndSave(TempVault vault, byte[] original, Func<string, string> edit)
    {
        var (editor, _) = Build(vault);
        var path = vault.WriteRaw("메모.txt", original);
        await editor.LoadAsync(path);
        Assert.False(editor.IsReadOnly);

        editor.Text = edit(editor.Text);
        Assert.True(await editor.TrySaveAsync());
        return File.ReadAllBytes(path);
    }

    public static TheoryData<string, byte[], Encoding> Formats => new()
    {
        { "UTF-8", [], new UTF8Encoding(false) },
        { "UTF-8 BOM", [0xEF, 0xBB, 0xBF], new UTF8Encoding(false) },
        { "UTF-16LE BOM", [0xFF, 0xFE], new UnicodeEncoding(false, false) },
        { "UTF-16BE BOM", [0xFE, 0xFF], new UnicodeEncoding(true, false) },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task 고치면_원래_인코딩과_BOM_으로_저장된다(string name, byte[] bom, Encoding encoding)
    {
        using var vault = new TempVault();
        var original = (byte[])[.. bom, .. encoding.GetBytes("첫 줄\r\n둘째 줄")];

        var saved = await EditAndSave(vault, original, t => t + "\r\n셋째 줄");

        Assert.Equal((byte[])[.. bom, .. encoding.GetBytes("첫 줄\r\n둘째 줄\r\n셋째 줄")], saved);
        Assert.NotNull(name);
    }

    [Fact]
    public async Task CP949_파일은_CP949_로_다시_쓰고_안_고친_바이트는_그대로다()
    {
        using var vault = new TempVault();
        var cp949 = Encoding.GetEncoding(949);
        byte[] original = [.. cp949.GetBytes("가나다\r\n"), 0x80, 0x41];       // 0x80 단독도 그대로 돌아온다 [실측]

        var saved = await EditAndSave(vault, original, t => t + "라");

        Assert.Equal((byte[])[.. original, .. cp949.GetBytes("라")], saved);
    }

    /// Enter 는 CRLF 를 넣는다 — LF 파일은 LF 로 맞춘다 (D-118)
    [Fact]
    public async Task LF_파일에_넣은_줄바꿈은_LF_로_저장된다()
    {
        using var vault = new TempVault();
        var utf8 = new UTF8Encoding(false);

        var saved = await EditAndSave(vault, utf8.GetBytes("a\nb\n"), t => t + "c\r\nd");

        Assert.Equal(utf8.GetBytes("a\nb\nc\nd"), saved);
    }

    [Fact]
    public async Task CP949_에_못_담는_글자면_저장하지_않고_줄_번호만_알린다()
    {
        using var vault = new TempVault();
        var (editor, _) = Build(vault);
        var cp949 = Encoding.GetEncoding(949);
        var original = cp949.GetBytes("가\r\n나\r\n다");
        var path = vault.WriteRaw("메모.txt", original);
        await editor.LoadAsync(path);

        editor.Text = "가\r\n나😀\r\n다";

        Assert.False(await editor.TrySaveAsync());
        Assert.Equal(original, File.ReadAllBytes(path));                  // 인코딩을 바꾸지 않았다 (D-119)
        Assert.Contains("2번째 줄", editor.StatusText);
        Assert.DoesNotContain("😀", editor.StatusText);                   // 글자 자체는 적지 않는다 (CUSTOM-04)
        Assert.True(editor.IsDirty);
        Assert.False(Directory.EnumerateFiles(vault.Root, "*.tmp", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task 평문을_저장해도_이력_사본을_남기지_않는다()
    {
        using var vault = new TempVault();

        await EditAndSave(vault, new UTF8Encoding(false).GetBytes("값"), t => t + " 고침");

        var history = Path.Combine(vault.Root, ".history");
        Assert.False(Directory.Exists(history) && Directory.EnumerateFileSystemEntries(history, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task 평문_저장_갈래는_txt_만_받는다()
    {
        using var vault = new TempVault();
        var (_, store) = Build(vault);
        var path = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(path);
        var before = File.ReadAllBytes(path);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SavePlainAsync(path, "평문", new PlainTextFormat("UTF-8", false, "\r\n")));

        Assert.Equal(before, File.ReadAllBytes(path));                   // 금고 문서가 평문으로 덮이지 않았다
    }

    /// 종료 저장 대상이 "고칠 수 있는 탭"이라 평문도 함께 저장된다
    [Fact]
    public async Task 종료_저장은_평문_탭도_저장한다()
    {
        using var vault = new TempVault();
        var (shell, _, _) = await ShellFixture.BuildAsync(vault);
        var path = vault.WriteRaw("메모.txt", new UTF8Encoding(false).GetBytes("원래 값"));
        await shell.RefreshAsync();
        await shell.OpenAsync(path);

        shell.ActiveTab!.Text = "끄기 전 값";
        Assert.True(await shell.SaveAllForExitAsync());

        Assert.Equal("끄기 전 값", File.ReadAllText(path));
        shell.Dispose();
    }
}
