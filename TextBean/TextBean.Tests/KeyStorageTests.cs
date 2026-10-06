using System.Security.Cryptography;
using TextBean.Models;
using TextBean.Services;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 저장 규칙: 문서는 그 문서를 잠근 키로만 저장한다 (plan §2-4).
/// 가장 위험한 실패는 "저장 실패"가 아니라 <b>조용한 재암호화</b>다 — 옛 키로 연 탭의 자동 저장이 문서를 새 키로
/// 다시 잠그면, 사용자는 옛 키로 돌아가 "열리지 않는 문서"를 보고 잃은 줄 안다 (research C-8).
/// </summary>
public class KeyStorageTests
{
    private const string KeyA = "first-key-1234";
    private const string KeyB = "second-key-5678";

    private static (VaultKeyService Keys, DocumentStore Store) Build(TempVault vault, string key = KeyA)
    {
        var keys = TestKeys.Service(key);
        return (keys, new DocumentStore(new TreeService(vault.Root), new KeyDocumentCodec(keys)));
    }

    private static EditorViewModel Editor(Services.Interfaces.IDocumentStore store, FakeAutoSaveTimer? timer = null)
        => new(store, new FakeClipboard(), new FakeDialogs(), timer ?? new FakeAutoSaveTimer());

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ── K-4 조용한 재암호화 ───────────────────────────────────────────────────

    [Fact]
    public async Task 문서를_연_채_키를_바꾸면_저장이_거부되고_파일은_그대로다()
    {
        using var vault = new TempVault();
        var (keys, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "A문서.tbx");
        await store.CreateAsync(doc);
        await store.SaveAsync(doc, "A 키의 값");
        var editor = Editor(store);
        await editor.LoadAsync(doc);
        var before = Hash(doc);

        // 탭 닫기를 거치지 않고 키가 바뀐 경우 (상류 게이트가 회귀한 상황)
        keys.Activate(keys.Evaluate(KeyB, []));
        editor.Text = "B 키 상태에서 친 값";

        Assert.False(await editor.TrySaveAsync());
        Assert.Equal(before, Hash(doc));
        Assert.True(editor.IsDirty);                          // 저장됐다고 속이지 않는다

        // A 키로 돌아가면 원래 값이 그대로 열린다
        keys.Activate(keys.Evaluate(KeyA, [KeyDocumentCodec.Parse(await File.ReadAllBytesAsync(doc))]));
        Assert.Equal("A 키의 값", (await store.LoadAsync(doc)).Text);
    }

    [Fact]
    public async Task 키를_바꾼_뒤_파일이_지워졌어도_새_키로_되살리지_않는다()
    {
        using var vault = new TempVault();
        var (keys, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "A문서.tbx");
        await store.CreateAsync(doc);
        var editor = Editor(store);
        await editor.LoadAsync(doc);

        keys.Activate(keys.Evaluate(KeyB, []));
        File.Delete(doc);
        editor.Text = "값";

        Assert.False(await editor.TrySaveAsync());
        Assert.False(File.Exists(doc));
    }

    [Fact]
    public async Task 같은_세대에서_파일이_지워지면_그_문서의_원래_키로_되살린다()
    {
        using var vault = new TempVault();
        var (keys, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "A문서.tbx");
        await store.CreateAsync(doc);
        var editor = Editor(store);
        await editor.LoadAsync(doc);
        var salt = Convert.ToHexString(await File.ReadAllBytesAsync(doc), 10, 16);

        File.Delete(doc);
        editor.Text = "되살린 값";

        Assert.True(await editor.TrySaveAsync());
        Assert.Equal(salt, Convert.ToHexString(await File.ReadAllBytesAsync(doc), 10, 16));
        Assert.Equal("되살린 값", (await store.LoadAsync(doc)).Text);
    }

    [Fact]
    public async Task 밖에서_다른_키_문서로_바꿔치기된_파일은_덮어쓰지_않는다()
    {
        using var vault = new TempVault();
        var (_, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "A문서.tbx");
        await store.CreateAsync(doc);
        var editor = Editor(store);
        await editor.LoadAsync(doc);

        await File.WriteAllBytesAsync(doc, TestKeys.Codec(KeyB).EncryptForNew("B 키의 다른 문서"));
        var swapped = Hash(doc);
        editor.Text = "A 키로 친 값";

        Assert.False(await editor.TrySaveAsync());
        Assert.Equal(swapped, Hash(doc));
    }

    // ── 옛 방식 문서 (D-073 — 변환 코드를 지운 뒤) ─────────────────────────────

    [Fact]
    public async Task 옛_방식_파일은_열지_않고_이유를_알리며_어떤_경로로도_덮어쓰지_않는다()
    {
        using var vault = new TempVault();
        var (_, store) = Build(vault);
        var doc = vault.WriteRaw("옛문서.tbx", LegacyShape.Bytes());
        var before = Hash(doc);

        var read = await store.LoadAsync(doc);
        var editor = Editor(store);
        await editor.LoadAsync(doc);
        editor.Text = "덮어쓰려는 값";

        Assert.Equal(DocumentReadStatus.LegacyDpapi, read.Status);
        Assert.Contains("이 버전에서는 열 수 없습니다", read.UserMessage);
        Assert.False(await editor.TrySaveAsync());                                             // 편집기 경로
        await Assert.ThrowsAsync<KeyUnavailableException>(() => store.SaveAsync(doc, "직접 저장"));   // 저장소 경로
        Assert.Equal(before, Hash(doc));
    }

    /// 계획 §2-11: 앱 어셈블리에 Windows 계정 암호화(DPAPI) 호출이 남지 않았다.
    [Fact]
    public void 앱은_DPAPI_를_참조하지_않는다()
    {
        var references = typeof(KeyDocumentCodec).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("System.Security.Cryptography.ProtectedData", references);
    }

    // ── K-5a 저장하는 사이의 입력 (기존 버그) ──────────────────────────────────

    /// 저장이 끝나면 "수정됨"을 무조건 껐다. 그 사이 친 글자는 다시 저장되지 않고 탭을 닫으면 사라졌다 [실측].
    [Fact]
    public async Task 저장을_기다리는_사이_친_글자는_수정됨으로_남아_다음_저장에_들어간다()
    {
        using var vault = new TempVault();
        var (_, inner) = Build(vault);
        var gated = new GatedDocumentStore(inner);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await gated.CreateAsync(doc);
        var timer = new FakeAutoSaveTimer();
        var editor = Editor(gated, timer);
        await editor.LoadAsync(doc);

        // 저장 한 번(Ctrl+S · 자동 저장)의 규칙을 본다. 떠나기 전 저장은 깨끗해질 때까지 다시 저장한다 —
        // 그 경로는 KeyFlowTests 의 탭 닫기 시험이 본다.
        editor.Text = "ab";
        gated.Close();
        var saving = editor.TrySaveAsync();
        editor.Text = "abc";                                  // 저장을 기다리는 사이의 입력
        gated.Open();
        Assert.True(await saving);

        Assert.True(editor.IsDirty);
        Assert.True(timer.IsRunning);                         // 입력이 자동 저장을 다시 예약했다

        // 타이머 발화는 fire-and-forget 이라 기다릴 수 없다 — 같은 저장 경로를 직접 부른다 (LL-079)
        Assert.True(await editor.TrySaveAsync());
        Assert.False(editor.IsDirty);
        Assert.Equal("abc", (await inner.LoadAsync(doc)).Text);
    }

    /// 떠나기 전 저장(탭 닫기 · 종료)은 깨끗해질 때까지 다시 저장한다. 끝없이 바뀌면 실패로 돌려 닫기를 멈추게 한다.
    [Fact]
    public async Task 떠나기_전_저장은_깨끗해질_때까지_하고_계속_바뀌면_실패로_돌린다()
    {
        using var vault = new TempVault();
        var (_, inner) = Build(vault);
        var gated = new GatedDocumentStore(inner);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await gated.CreateAsync(doc);
        var editor = Editor(gated);
        await editor.LoadAsync(doc);

        var typed = 0;
        editor.Text = "a";
        gated.DuringSave = () => { if (typed++ < 2) editor.Text += "b"; };      // 저장 두 번 동안 입력이 들어온다
        Assert.True(await editor.TrySaveForLeaveAsync());
        Assert.False(editor.IsDirty);
        Assert.Equal("abb", (await inner.LoadAsync(doc)).Text);

        gated.DuringSave = () => editor.Text += "c";                           // 멈추지 않는 입력
        editor.Text += "!";
        Assert.False(await editor.TrySaveForLeaveAsync());
        Assert.True(editor.IsDirty);                                           // 닫기를 멈춘다 — 입력을 버리지 않는다
    }

    /// <summary>
    /// U-4. 자동 저장이 도는 사이 닫기 저장이 겹치면, 먼저 시작해 늦게 끝난 자동 저장이 옛 본문으로 새 본문을 덮었다 —
    /// 닫기 저장은 이미 성공을 돌려줘 탭이 닫힌 뒤다. 같은 탭의 저장은 한 번에 하나다.
    /// </summary>
    [Fact]
    public async Task 먼저_시작한_자동_저장이_늦게_끝나도_닫기_저장의_새_본문을_덮지_않는다()
    {
        using var vault = new TempVault();
        var (_, inner) = Build(vault);
        var gated = new GatedDocumentStore(inner);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await gated.CreateAsync(doc);
        var editor = Editor(gated);
        await editor.LoadAsync(doc);
        gated.HoldFirstSaveUntilSecondEnds = true;

        editor.Text = "a";
        var autoSave = editor.TrySaveAsync();                 // 자동 저장과 같은 저장 한 번 — 먼저 시작해 늦게 끝난다
        editor.Text = "ab";
        var leave = editor.TrySaveForLeaveAsync();            // 그 사이 탭 닫기

        Assert.True(await leave);
        await autoSave;

        Assert.False(editor.IsDirty);
        Assert.Equal("ab", (await inner.LoadAsync(doc)).Text);
    }

    [Fact]
    public async Task 그_사이_입력이_없으면_저장_뒤_수정됨이_꺼진다()
    {
        using var vault = new TempVault();
        var (_, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(doc);
        var editor = Editor(store);
        await editor.LoadAsync(doc);

        editor.Text = "값";

        Assert.True(await editor.TrySaveAsync());
        Assert.False(editor.IsDirty);
    }

    // ── 헤더 읽기 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task 헤더_읽기는_파일_하나가_실패해도_나머지를_계속한다()
    {
        using var vault = new TempVault();
        using var outside = new TempVault();
        using var links = new Junctions(vault.Root);
        var (_, store) = Build(vault);
        var ok = Path.Combine(vault.Root, "정상.tbx");
        await store.CreateAsync(ok);
        links.Create(Path.Combine(vault.Root, "링크"), outside.Root);
        outside.WriteRaw("밖.tbx", [1, 2, 3]);

        var headers = await store.ReadHeadersAsync(
            [ok, Path.Combine(vault.Root, "없는.tbx"), Path.Combine(vault.Root, "링크", "밖.tbx")]);

        Assert.Equal(DocumentHeaderKind.Valid, headers[0]!.Kind);
        Assert.Null(headers[1]);
        Assert.Null(headers[2]);                               // 링크 너머는 읽지 않는다
    }

    /// 디스크에 닿은 판을 되읽어 풀지 못하면(전원 차단으로 잘린 판 등) 원본을 바꾸지 않는다.
    /// 되읽은 평문이 다르게 나오는 코덱으로 흉내 낸다 — 이 검사를 빼면 원본이 그 판으로 바뀐다.
    [Fact]
    public async Task 되읽은_내용이_다르면_원본을_바꾸지_않는다()
    {
        using var vault = new TempVault();
        var (keys, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(doc);
        await store.SaveAsync(doc, "원래 값");
        var before = Hash(doc);
        var lying = new DocumentStore(new TreeService(vault.Root), new MisreadingCodec(new KeyDocumentCodec(keys)));

        await Assert.ThrowsAsync<IOException>(() => lying.SaveAsync(doc, "새 값"));

        Assert.Equal(before, Hash(doc));
        Assert.Empty(Directory.GetFiles(vault.Root, "*.tmp"));
    }

    /// 암호화는 그대로 하고, 되읽을 때만 다른 평문을 돌려준다
    private sealed class MisreadingCodec(Services.Interfaces.IDocumentCodec inner) : Services.Interfaces.IDocumentCodec
    {
        public int HeaderLength => inner.HeaderLength;
        public DocumentReadResult Decrypt(byte[] fileBytes)
            => inner.Decrypt(fileBytes) is { IsOk: true } ok ? ok with { Text = ok.Text + "?" } : inner.Decrypt(fileBytes);
        public byte[] EncryptForNew(string plainText) => inner.EncryptForNew(plainText);
        public byte[] EncryptReplacing(string plainText, ReadOnlySpan<byte> existingHeader, DocumentKeyBinding? binding)
            => inner.EncryptReplacing(plainText, existingHeader, binding);
        public byte[] EncryptFor(string plainText, DocumentKeyBinding binding) => inner.EncryptFor(plainText, binding);
        public DocumentHeader ParseHeader(ReadOnlySpan<byte> bytes) => inner.ParseHeader(bytes);
        public DocumentKeyState Classify(DocumentHeader header) => inner.Classify(header);
    }

    [Fact]
    public async Task 저장은_되읽어_확인한_뒤_교체하고_임시_파일을_남기지_않는다()
    {
        using var vault = new TempVault();
        var (_, store) = Build(vault);
        var doc = Path.Combine(vault.Root, "문서.tbx");
        await store.CreateAsync(doc);

        await store.SaveAsync(doc, "첫 값");
        await store.SaveAsync(doc, "둘째 값");

        Assert.Equal("둘째 값", (await store.LoadAsync(doc)).Text);
        Assert.Empty(Directory.GetFiles(vault.Root, "*.tmp"));
    }
}
