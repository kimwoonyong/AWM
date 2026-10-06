using System.IO;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

public sealed class DocumentStore(ITreeService tree, IDocumentCodec codec) : IDocumentStore
{
    public Task CreateAsync(string fullPath)
    {
        tree.EnsureInsideRoot(fullPath);

        // 아무것도 안 만들면 F5나 재시작에 새 노드가 조용히 사라진다. 0바이트로 만들면
        // 매직 검사에 걸려 읽기 전용으로 잠기고, 저장이 완전히 차단되므로 앱 안에서는
        // 영영 쓸 수 없는 파일이 된다. 유효한 빈 문서만이 두 함정을 모두 피한다 (D-009).
        return File.WriteAllBytesAsync(fullPath, codec.EncryptForNew(DocumentBody.Empty));
    }

    /// <summary>
    /// 문서마다 헤더만 읽는다 (트리 표시 · 키 판정). 파일 하나가 실패해도 나머지는 계속한다 —
    /// 링크로 바뀐 파일 하나 때문에 새로고침 전체가 죽으면 모든 파일 조작 뒤 트리가 멈춘다.
    /// 읽지 못한 항목은 null.
    /// </summary>
    public Task<IReadOnlyList<DocumentHeader?>> ReadHeadersAsync(IReadOnlyList<string> fullPaths)
        => Task.Run<IReadOnlyList<DocumentHeader?>>(() => fullPaths.Select(ReadHeaderOrNull).ToList());

    /// <summary>
    /// 지금 키에 대한 문서마다의 상태 (트리 표시). 처음 보는 salt 는 여기서 키를 만든다 — 스레드 풀에서 돈다.
    /// </summary>
    public Task<IReadOnlyList<DocumentKeyState>> ClassifyAsync(IReadOnlyList<string> fullPaths)
        => Task.Run<IReadOnlyList<DocumentKeyState>>(() => fullPaths
            .Select(path => ReadHeaderOrNull(path) is { } header ? codec.Classify(header) : DocumentKeyState.Unreadable)
            .ToList());

    private DocumentHeader? ReadHeaderOrNull(string fullPath)
    {
        try
        {
            tree.EnsureInsideRoot(fullPath);
            return codec.ParseHeader(ReadHead(fullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("header", fullPath, ex);
            return null;
        }
    }

    private byte[] ReadHead(string fullPath)
    {
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[codec.HeaderLength];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return buffer[..read];
    }

    public async Task<DocumentReadResult> LoadAsync(string fullPath)
    {
        tree.EnsureInsideRoot(fullPath);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath);
        }
        catch (Exception ex)
        {
            // 읽기에 성공하지 못한 모든 경우가 잠금 대상이다 (D-005 보강).
            // 여기서 Ok를 돌려주면 빈 편집기가 원본을 덮는다.
            AppLog.Warn("load", fullPath, ex);
            return DocumentReadResult.Fail(DocumentReadStatus.ReadFailed, ex.GetType().Name);
        }

        // 종류는 내용이 아니라 확장자가 정한다. 내용(TBX1 매직)으로 가르면 "평문인데 .tbx 인 파일"이
        // 정상 문서로 읽혀, 읽기 실패 문서의 저장 차단(D-005)이 통째로 무너진다.
        return PathRules.IsPlainText(fullPath) ? PlainTextReader.Decode(bytes) : codec.Decrypt(bytes);
    }

    public void CaptureOpenSnapshot(string fullPath)
    {
        tree.EnsureInsideRoot(fullPath);
        if (!File.Exists(fullPath)) return;

        try
        {
            // .history 쪽 링크도 이 try 안에서 막힌다 — 스냅샷만 안 남고 문서는 열린다
            var snapshot = PathRules.SnapshotPathFor(tree.Root, fullPath);
            tree.EnsureInsideRoot(snapshot);
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.Copy(fullPath, snapshot, overwrite: true);
            TrySetHiddenDirectory(Path.Combine(tree.Root, PathRules.HistoryFolderName));
        }
        catch (Exception ex)
        {
            // 스냅샷을 못 남겼다고 문서를 못 열게 하면 안 된다. 다만 조용히 넘기지도 않는다.
            AppLog.Warn("snapshot", fullPath, ex);
        }
    }

    public string? SnapshotPathIfExists(string fullPath)
    {
        tree.EnsureInsideRoot(fullPath);
        var snapshot = PathRules.SnapshotPathFor(tree.Root, fullPath);
        tree.EnsureInsideRoot(snapshot);
        return File.Exists(snapshot) ? snapshot : null;
    }

    public Task SaveAsync(string fullPath, string text) => SaveAsync(fullPath, DocumentBody.Plain(text), binding: null);

    /// <summary>
    /// 저장은 그 문서를 잠근 키로만 한다. 파일이 있으면 디스크 헤더의 키로, 없으면 결속의 키로.
    /// 결속의 세대가 지금 세대가 아니면(키 전환·잠그기 뒤) 쓰지 않는다 — 옛 키로 연 탭이 문서를
    /// 조용히 새 키로 다시 잠그면, 사용자는 옛 키로 돌아가 "열리지 않는 문서"를 보고 잃은 줄 안다.
    /// 결속이 없으면 지금 세대로 본다(테스트·기존 호출).
    /// </summary>
    public async Task SaveAsync(string fullPath, DocumentBody body, DocumentKeyBinding? binding)
    {
        tree.EnsureInsideRoot(fullPath);

        // 2차 방어선. 저장을 막는 것은 원래 EditorViewModel 의 IsReadOnly 게이트이고 그것으로 충분하지만,
        // 상류 게이트 한 줄에 전부를 걸면 그 줄이 회귀했을 때 평문이 암호문으로 덮여 복구할 수 없다.
        // 닫는 경로가 둘인데 하나만 막아 본 적이 있다 (LL-078).
        if (PathRules.IsPlainText(fullPath))
            throw new InvalidOperationException("평문 파일은 저장하지 않습니다.");

        var folder = Path.GetDirectoryName(fullPath)!;

        // 폴더를 만들어 버리면, 열어 둔 문서의 상위 폴더가 이름변경된 뒤 저장할 때
        // 옛 이름 위치에 파일이 조용히 되살아난다 (D-011).
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("문서가 있던 폴더를 찾을 수 없습니다.");

        var existing = File.Exists(fullPath);
        var cipher = existing
            ? codec.EncryptReplacing(body, ReadHead(fullPath), binding)
            : binding is null ? codec.EncryptForNew(body) : codec.EncryptFor(body, binding);

        // 임시 파일은 반드시 같은 폴더에 — 다른 볼륨이면 원자적 교체가 성립하지 않는다
        var temp = Path.Combine(folder, Path.GetFileName(fullPath) + ".tmp");
        try
        {
            await WriteDurablyAsync(temp, cipher);

            // 되읽어 푼다. 디스크에 제대로 닿지 않은 판으로 원본을 바꾸면 되돌릴 길이 없다.
            var check = codec.Decrypt(await File.ReadAllBytesAsync(temp));
            // 서식 바이트도 비교한다 — 서식만 바꾼 저장에서 글자만 보면 깨진 서식을 맞다고 본다
            if (!check.IsOk
                || !string.Equals(check.Text, body.Text, StringComparison.Ordinal)
                || !check.Rich.AsSpan().SequenceEqual(body.Rich))
                throw new IOException("저장한 내용을 다시 읽어 확인하지 못했습니다.");

            // 저장마다 세대를 쌓지 않는다. 되돌릴 기준은 "이번에 문서를 연 시점"이고
            // 그 한 벌은 CaptureOpenSnapshot 이 이미 남겼다 (D-016).
            if (existing && File.Exists(fullPath)) await ReplaceAsync(temp, fullPath);
            else File.Move(temp, fullPath);
        }
        catch
        {
            TryDelete(temp);
            throw;      // 호출자가 수정 상태를 유지하고 전환/종료를 취소한다
        }
    }

    /// <summary>
    /// 평문(.txt) 저장 (D-117 · D-120). 키 · 암호화 경로를 타지 않는다 — 위 SaveAsync 의 "평문 거부"는 그대로 둔다.
    /// 읽은 형식(인코딩 · BOM · 줄바꿈)으로 다시 쓴다. 이력 · 열었을 때 사본은 남기지 않는다(평문 사본을 늘리지 않는다).
    /// 교체 방식(같은 폴더 임시 파일 → 되읽기 확인 → File.Replace)은 암호 문서와 같다.
    /// </summary>
    public async Task SavePlainAsync(string fullPath, string text, PlainTextFormat format)
    {
        tree.EnsureInsideRoot(fullPath);

        if (!PathRules.IsPlainText(fullPath))
            throw new InvalidOperationException("평문 형식으로는 .txt 만 저장합니다.");

        var folder = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("문서가 있던 폴더를 찾을 수 없습니다.");

        var bytes = PlainTextReader.Encode(text, format);     // 못 담는 글자면 여기서 던진다 — 파일은 그대로다

        var existing = File.Exists(fullPath);
        var temp = Path.Combine(folder, Path.GetFileName(fullPath) + ".tmp");
        try
        {
            await WriteDurablyAsync(temp, bytes);

            if (!(await File.ReadAllBytesAsync(temp)).AsSpan().SequenceEqual(bytes))
                throw new IOException("저장한 내용을 다시 읽어 확인하지 못했습니다.");

            if (existing && File.Exists(fullPath)) await ReplaceAsync(temp, fullPath);
            else File.Move(temp, fullPath);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// 원자적 교체. 방금 만들거나 읽은 파일은 백신이 잠깐 잡고 있어 "바꿀 파일을 제거할 수 없습니다"로 실패한다
    /// [실측 — 문서 1,000개를 만들자마자 저장하는 중 1회]. 공유·잠금 위반일 때만 잠깐 기다려 다시 한다.
    /// 다른 오류는 그대로 올린다 — 호출자가 수정 상태를 유지한다.
    /// </summary>
    private static async Task ReplaceAsync(string temp, string destination)
    {
        const int sharingViolation = 32, lockViolation = 33, unableToRemoveReplaced = 1175;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(temp, destination, null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException ex) when (attempt < 5
                                         && (ex.HResult & 0xFFFF) is sharingViolation or lockViolation or unableToRemoveReplaced)
            {
                await Task.Delay(50 * attempt);
            }
        }
    }

    /// <summary>
    /// 디스크까지 내려보낸다 — WriteAllBytes 는 OS 캐시에 두고 돌아와, 전원이 나가면 잘린 판이 교체될 수 있다.
    /// 남아 있던 임시 파일은 먼저 지운다. 그 이름이 금고 밖 파일의 하드 링크로 심겨 있으면
    /// 제자리 쓰기가 금고 밖 내용을 덮는다 — 지우면 이름만 사라지고 금고 밖은 그대로다.
    /// </summary>
    private static async Task WriteDurablyAsync(string path, byte[] bytes)
    {
        TryDelete(path);

        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                bufferSize: 4096, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        // 정리 실패가 저장 실패를 가리면 안 된다
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TrySetHiddenDirectory(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (info.Exists) info.Attributes |= FileAttributes.Hidden;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
