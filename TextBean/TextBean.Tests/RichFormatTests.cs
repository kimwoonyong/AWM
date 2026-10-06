using TextBean.Models;
using TextBean.Services;
using TextBean.Services.Interfaces;
using TextBean.ViewModels;

namespace TextBean.Tests;

/// <summary>
/// 서식 형식 v3 (D-123 · D-124)과 편집기의 서식 본문 계약 (D-126 · D-129). 화면 없이 본다 — 화면 쪽은 RichBodyTests.
/// </summary>
public class RichFormatTests
{
    private static readonly byte[] SomeRich = [0x50, 0x4B, 0x03, 0x04, 0, 255, 7];   // 저장소 · 코덱은 서식 바이트를 해석하지 않는다

    private readonly KeyDocumentCodec _codec = TestKeys.Codec();

    // ── 코덱 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void 글자와_서식_바이트가_함께_왕복한다()
    {
        var body = new DocumentBody("첫 줄\r\n둘째 줄 🔑", SomeRich);

        var read = _codec.Decrypt(_codec.EncryptForNew(body));

        Assert.True(read.IsOk);
        Assert.Equal(body.Text, read.Text);
        Assert.Equal(SomeRich, read.Rich);
    }

    [Fact]
    public void 서식_없는_본문은_빈_서식으로_돌아온다()
    {
        var read = _codec.Decrypt(_codec.EncryptForNew(DocumentBody.Plain("글자만")));

        Assert.Equal("글자만", read.Text);
        Assert.Empty(read.Rich!);
    }

    [Fact]
    public void 빈_문서도_왕복한다()
    {
        var read = _codec.Decrypt(_codec.EncryptForNew(DocumentBody.Empty));

        Assert.Equal(("", 0), (read.Text, read.Rich!.Length));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void 버전_1과_2는_옛_형식이라_열지도_덮어쓰지도_않는다(byte version)
    {
        var bytes = _codec.EncryptForNew("x");
        bytes[4] = version;

        Assert.Equal(DocumentReadStatus.LegacyDpapi, _codec.Decrypt(bytes).Status);
        Assert.Equal(DocumentKeyState.Legacy, _codec.Classify(_codec.ParseHeader(bytes)));
        Assert.Throws<KeyUnavailableException>(() => _codec.EncryptReplacing(DocumentBody.Plain("y"), bytes, binding: null));
    }

    [Fact]
    public void 옛_형식_문구는_옛_방식만이_아니라_옛_형식이라고_말한다()
        => Assert.StartsWith("옛 형식", DocumentReadResult.Fail(DocumentReadStatus.LegacyDpapi).UserMessage);

    [Fact]
    public void 서식_바이트도_암호문에_평문으로_남지_않는다()
    {
        var marker = "SECRET-FORMAT-MARK"u8.ToArray();

        var bytes = _codec.EncryptForNew(new DocumentBody("x", marker));

        Assert.False(bytes.AsSpan().IndexOf(marker) >= 0);
    }

    // ── 저장소 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task 저장소는_서식_바이트까지_저장하고_읽는다()
    {
        using var vault = new TempVault();
        var store = TestKeys.Store(vault.Root);
        var path = Path.Combine(vault.Root, "a.tbx");
        await store.CreateAsync(path);

        await store.SaveAsync(path, new DocumentBody("값", SomeRich), binding: null);
        var read = await store.LoadAsync(path);

        Assert.Equal("값", read.Text);
        Assert.Equal(SomeRich, read.Rich);
    }

    /// 서식만 바꾼 저장에서 글자만 비교하면 깨진 서식을 맞다고 보고 원본을 갈아 끼운다
    [Fact]
    public async Task 되읽은_서식_바이트가_다르면_저장을_실패시키고_원본을_남긴다()
    {
        using var vault = new TempVault();
        var path = Path.Combine(vault.Root, "a.tbx");
        await TestKeys.Store(vault.Root).CreateAsync(path);
        var before = File.ReadAllBytes(path);
        var store = new DocumentStore(new TreeService(vault.Root), new RichMisreadingCodec(TestKeys.Codec()));

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(path, new DocumentBody("값", SomeRich), binding: null));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(vault.Root, "*.tmp"));
    }

    /// 암호화는 그대로, 되읽을 때 서식 바이트만 한 바이트 바꾼다 — 글자는 같다
    private sealed class RichMisreadingCodec(IDocumentCodec inner) : IDocumentCodec
    {
        public int HeaderLength => inner.HeaderLength;
        public DocumentReadResult Decrypt(byte[] fileBytes)
            => inner.Decrypt(fileBytes) is { IsOk: true, Rich.Length: > 0 } ok
                ? ok with { Rich = [.. ok.Rich![..^1], (byte)(ok.Rich![^1] ^ 1)] }
                : inner.Decrypt(fileBytes);
        public byte[] EncryptForNew(DocumentBody body) => inner.EncryptForNew(body);
        public byte[] EncryptReplacing(DocumentBody body, ReadOnlySpan<byte> existingHeader, DocumentKeyBinding? binding)
            => inner.EncryptReplacing(body, existingHeader, binding);
        public byte[] EncryptFor(DocumentBody body, DocumentKeyBinding binding) => inner.EncryptFor(body, binding);
        public DocumentHeader ParseHeader(ReadOnlySpan<byte> bytes) => inner.ParseHeader(bytes);
        public DocumentKeyState Classify(DocumentHeader header) => inner.Classify(header);
    }

    // ── 편집기 ───────────────────────────────────────────────────────────────

    private static async Task<(EditorViewModel vm, IDocumentStore store, string path, FakeClipboard clip)> OpenAsync(
        TempVault vault, DocumentBody body, IDocumentStore? wrap = null)
    {
        var inner = TestKeys.Store(vault.Root);
        var path = Path.Combine(vault.Root, "a.tbx");
        await inner.CreateAsync(path);
        await inner.SaveAsync(path, body, binding: null);

        var store = wrap ?? inner;
        var clip = new FakeClipboard();
        var vm = new EditorViewModel(store, clip, new FakeDialogs(), new FakeAutoSaveTimer());
        await vm.LoadAsync(path);
        return (vm, store, path, clip);
    }

    [Fact]
    public async Task 불러온_서식을_화면에_넘긴다()
    {
        using var vault = new TempVault();
        var (vm, _, _, _) = await OpenAsync(vault, new DocumentBody("값", SomeRich));

        Assert.Equal(SomeRich, vm.Rich);
        Assert.Equal("값", vm.Text);
        Assert.True(vm.ShowFormatBar);
    }

    [Fact]
    public async Task 화면이_보낸_글자는_수정됨으로_치지_않는다()
    {
        using var vault = new TempVault();
        var (vm, _, _, _) = await OpenAsync(vault, DocumentBody.Plain("a\nb"));

        vm.SyncText("a\r\nb");                // 문단 줄바꿈 표기만 다르다

        Assert.Equal("a\r\nb", vm.Text);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task 서식만_바꿔도_수정됨이_되고_화면이_뽑은_본문을_저장한다()
    {
        using var vault = new TempVault();
        var (vm, store, path, _) = await OpenAsync(vault, DocumentBody.Plain("값"));
        var edited = new DocumentBody("값", SomeRich);
        vm.CaptureBody = () => edited;

        vm.MarkEdited();
        Assert.True(vm.IsDirty);
        Assert.True(await vm.TrySaveAsync());

        Assert.False(vm.IsDirty);
        Assert.Equal(SomeRich, (await store.LoadAsync(path)).Rich);
    }

    /// 화면이 아직 없는 탭(한 번도 안 보인 탭)의 저장은 불러온 서식을 그대로 쓴다 — 서식이 지워지면 안 된다
    [Fact]
    public async Task 화면이_없으면_불러온_서식을_그대로_저장한다()
    {
        using var vault = new TempVault();
        var (vm, store, path, _) = await OpenAsync(vault, new DocumentBody("값", SomeRich));

        vm.MarkEdited();
        Assert.True(await vm.TrySaveAsync());

        Assert.Equal(SomeRich, (await store.LoadAsync(path)).Rich);
    }

    /// 저장을 기다리는 사이 서식만 바꾼 입력 — 글자로 비교하면 같아 보여 수정됨이 꺼지고 그 서식은 저장되지 않는다 (D-129)
    [Fact]
    public async Task 저장_중에_서식만_바꾼_입력은_수정됨으로_남는다()
    {
        using var vault = new TempVault();
        var gated = new GatedDocumentStore(TestKeys.Store(vault.Root));
        var (vm, _, _, _) = await OpenAsync(vault, DocumentBody.Plain("값"), gated);
        vm.MarkEdited();

        gated.Close();
        var saving = vm.TrySaveAsync();
        vm.MarkEdited();                       // 글자는 그대로, 서식만
        gated.Open();
        Assert.True(await saving);

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task 전체_복사는_서식째_보낸다()
    {
        using var vault = new TempVault();
        var (vm, _, _, clip) = await OpenAsync(vault, DocumentBody.Plain("값"));
        var all = new ClipboardPayload("값", "{\\rtf1}", SomeRich);
        vm.CaptureAllForCopy = () => all;

        vm.Copy(null);

        Assert.Same(all, clip.CopiedPayload);
    }

    [Fact]
    public async Task 서식을_못_읽으면_잠그고_저장하지_않는다()
    {
        using var vault = new TempVault();
        var (vm, store, path, _) = await OpenAsync(vault, new DocumentBody("값", SomeRich));
        var before = File.ReadAllBytes(path);

        vm.RichLoadFailed(new FormatException());
        vm.MarkEdited();
        await vm.TrySaveAsync();

        Assert.True(vm.IsReadOnly);
        Assert.False(vm.ShowFormatBar);
        Assert.False(vm.IsDirty);
        Assert.Equal(DocumentReadStatus.Corrupted, vm.LoadStatus);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
