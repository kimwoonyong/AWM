using System.Text;
using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 판정 축은 "게이트가 false 를 돌려주는가"가 아니라 <b>디스크 원본 바이트가 그대로인가</b>이다.
/// 게이트별로 세면 새 쓰기 경로가 생겼을 때 조용히 빠져나간다 — 닫는 경로가 둘인데
/// 하나만 막아 본 적이 있다 (LL-078).
/// </summary>
public class PlainTextReadOnlyTests
{
    private const string Body = "서버 접속 정보\r\nhost = 10.0.4.12\r\nuser = admin";

    private static byte[] PlainBytes => new UTF8Encoding(false).GetBytes(Body);

    private static (EditorViewModel Editor, FakeAutoSaveTimer Timer, DocumentStore Store) Build(TempVault vault)
    {
        var store = new DocumentStore(new TreeService(vault.Root), TestKeys.Codec());
        var timer = new FakeAutoSaveTimer();
        return (new EditorViewModel(store, new FakeClipboard(), new FakeDialogs(), timer), timer, store);
    }

    [Fact]
    public async Task 평문은_읽기에_성공하고_읽기_전용으로_열린다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);
        var path = vault.WriteRaw("메모.txt", PlainBytes);

        await editor.LoadAsync(path);

        Assert.Equal(Body, editor.Text);
        Assert.True(editor.IsReadOnly);
        Assert.False(editor.LoadFailed);        // 읽기 실패와 구분되어야 본문·복사·문구가 갈린다
        Assert.True(editor.IsPlainText);
        Assert.True(editor.CanCopy);
        Assert.Equal("UTF-8", editor.EncodingLabel);
    }

    /// <summary>
    /// 배너는 Visibility 가 IsReadOnly 에만, Text 가 LockReason 에만 묶여 있다.
    /// 문구를 비우면 글자 없는 붉은 띠가 그려진다 — 화면이 고장난다.
    /// </summary>
    [Fact]
    public async Task 평문_배너_문구가_비어_있지_않고_인코딩을_말한다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);

        await editor.LoadAsync(vault.WriteRaw("메모.txt", PlainBytes));

        Assert.False(string.IsNullOrWhiteSpace(editor.LockReason));
        Assert.Contains("암호화되지 않은", editor.LockReason!);
        Assert.Contains("UTF-8", editor.LockReason!);
        Assert.Contains("추정", editor.LockReason!);        // BOM 이 없으므로 확정이라고 말하면 안 된다
        Assert.Contains("보기 전용", editor.StatusText);    // "저장할 수 없습니다"는 고장처럼 읽힌다
    }

    [Fact]
    public async Task BOM이_있으면_추정이라고_말하지_않는다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(PlainBytes).ToArray();

        await editor.LoadAsync(vault.WriteRaw("메모.txt", bytes));

        Assert.Equal("UTF-8 (BOM)", editor.EncodingLabel);
        Assert.DoesNotContain("추정", editor.LockReason!);
    }

    /// 여는 것만으로는 타이머가 무장되지 않는다. 무장시키는 것은 Text 세터 하나뿐이다.
    [Fact]
    public async Task 평문은_타이핑해도_자동저장이_예약되지_않는다()
    {
        using var vault = new TempVault();
        var (editor, timer, _) = Build(vault);
        await editor.LoadAsync(vault.WriteRaw("메모.txt", PlainBytes));

        editor.Text = "덮어쓰려는 값";

        Assert.Equal(0, timer.RestartCount);
        Assert.False(timer.IsRunning);
    }

    /// <summary>
    /// 모든 쓰기 경로를 연달아 두들기고 <b>원본 바이트가 그대로인지</b>만 본다.
    /// 경로가 하나 늘어도 이 테스트는 그대로 유효하다.
    /// </summary>
    [Fact]
    public async Task 모든_저장_경로를_거쳐도_원본_바이트가_그대로다()
    {
        using var vault = new TempVault();
        var (editor, timer, _) = Build(vault);
        var path = vault.WriteRaw("메모.txt", PlainBytes);
        await editor.LoadAsync(path);

        editor.Text = "덮어쓰려는 값";

        timer.Fire();                                            // 예약이 없으면 아무 일도 없어야 한다
        Assert.False(await editor.TrySaveAsync());               // Ctrl+S
        Assert.True(await editor.ConfirmLeaveAsync());           // 탭 떠나기 — 잃을 편집 내용이 없다
        Assert.True(await editor.TrySaveForLeaveAsync());        // 종료 시 저장

        Assert.Equal(PlainBytes, File.ReadAllBytes(path));
    }

    /// 대조군이 없으면 "아무 일도 안 일어난다"가 버그인지 설계인지 구분되지 않는다.
    [Fact]
    public async Task 대조군_금고문서는_같은_조작에서_실제로_저장된다()
    {
        using var vault = new TempVault();
        var (editor, timer, store) = Build(vault);
        var path = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(path);
        await editor.LoadAsync(path);
        var before = File.ReadAllBytes(path);

        editor.Text = "새 값";

        Assert.False(editor.IsReadOnly);
        Assert.False(editor.IsPlainText);
        Assert.Equal(1, timer.RestartCount);
        Assert.True(await editor.TrySaveAsync());
        Assert.NotEqual(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 2차 방어선. 편집기 게이트가 회귀해도 서비스가 막아 평문이 암호문으로 덮이지 않는다.
    /// </summary>
    [Fact]
    public async Task 서비스에_직접_저장을_요청해도_평문은_거부한다()
    {
        using var vault = new TempVault();
        var (_, _, store) = Build(vault);
        var path = vault.WriteRaw("메모.txt", PlainBytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(path, "덮어쓰려는 값"));

        Assert.Equal(PlainBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task 판정_불가_파일은_잠기고_이유를_말한다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);

        // NUL 이 섞인 이진 파일
        await editor.LoadAsync(vault.WriteRaw("이진.txt", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]));

        Assert.True(editor.IsReadOnly);
        Assert.True(editor.LoadFailed);
        Assert.False(editor.IsPlainText);          // 정상 평문이 아니라 오류다 — 오류 배너가 맞다
        Assert.Equal("", editor.Text);
        Assert.False(editor.CanCopy);
        Assert.False(string.IsNullOrWhiteSpace(editor.LockReason));
        Assert.Contains("인코딩", editor.LockReason!);
    }

    /// <summary>
    /// 표시 상한. 읽기는 싸지만 본문 TextBox 에 올리는 것은 20MB 에 6초 걸린다 [실측].
    /// 상한을 넘으면 본문을 올리지 않고 이유만 말한다.
    /// </summary>
    [Fact]
    public async Task 너무_긴_평문은_본문을_올리지_않고_이유를_말한다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);
        var big = new string('가', 1_000_001);

        await editor.LoadAsync(vault.WriteRaw("큰파일.txt", new UTF8Encoding(false).GetBytes(big)));

        Assert.Equal("", editor.Text);
        Assert.True(editor.IsReadOnly);
        Assert.Contains("너무 길어", editor.LockReason!);
        Assert.Contains("검색에는", editor.LockReason!);     // 찾을 수는 있다는 것을 알려야 한다
    }

    [Fact]
    public async Task 상한_바로_아래는_그대로_보여준다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);
        var body = new string('가', 1_000_000);

        await editor.LoadAsync(vault.WriteRaw("큰파일.txt", new UTF8Encoding(false).GetBytes(body)));

        Assert.Equal(body, editor.Text);
        Assert.True(editor.IsPlainText);
    }

    /// 탭 제목이 같은 글자로 겹치면 사용자는 어느 쪽을 보고 있는지 모른다.
    [Fact]
    public async Task 탭_제목은_평문만_확장자를_보여준다()
    {
        using var vault = new TempVault();
        var (plain, _, store) = Build(vault);
        var (doc, _, _) = Build(vault);

        var tbx = Path.Combine(vault.Root, "메모.tbx");
        await store.CreateAsync(tbx);

        await plain.LoadAsync(vault.WriteRaw("메모.txt", PlainBytes));
        await doc.LoadAsync(tbx);

        Assert.Equal("메모.txt", plain.TabTitle);
        Assert.Equal("메모", doc.TabTitle);
        Assert.NotEqual(plain.TabTitle, doc.TabTitle);
    }

    [Fact]
    public async Task 평문을_연_뒤_금고문서를_열면_평문_상태가_남지_않는다()
    {
        using var vault = new TempVault();
        var (editor, _, store) = Build(vault);
        var tbx = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(tbx);

        await editor.LoadAsync(vault.WriteRaw("메모.txt", PlainBytes));
        await editor.LoadAsync(tbx);

        Assert.False(editor.IsPlainText);
        Assert.False(editor.IsReadOnly);
        Assert.Null(editor.EncodingLabel);
        Assert.Null(editor.LockReason);
    }

    /// 확장자가 종류를 정한다. 내용으로 가르면 이 회귀핀이 깨진다.
    [Fact]
    public async Task 평문_내용이어도_tbx_확장자면_여전히_잠긴다()
    {
        using var vault = new TempVault();
        var (editor, _, _) = Build(vault);

        await editor.LoadAsync(vault.WriteRaw("가짜.tbx", PlainBytes));

        Assert.True(editor.LoadFailed);
        Assert.False(editor.IsPlainText);
        Assert.Equal("", editor.Text);
        Assert.Contains("TextBean 문서가 아닙니다", editor.LockReason!);
    }
}
