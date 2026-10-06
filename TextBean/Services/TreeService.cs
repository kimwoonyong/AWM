using System.IO;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// 루트 하위 열거·생성·이름변경·삭제. 디스크에 닿는 유일한 곳 중 하나다 (PROHIBITED-CUSTOM-03).
/// </summary>
public sealed class TreeService : ITreeService
{
    public TreeService(string root) => Root = PathRules.NormalizeFull(root);

    public string Root { get; private set; }

    public void SetRoot(string root) => Root = PathRules.NormalizeFull(root);

    private string TrashRoot => Path.Combine(Root, PathRules.TrashFolderName);

    /// <summary>
    /// 폴더가 실제로 열리는지까지 본다. 금고를 링크 뒤에 두었는데 그 대상이 사라지면
    /// Directory.Exists 는 링크 자신을 보고 참을 돌려준다 [실측] — 그때도 금고 재지정으로 보내야 한다.
    /// </summary>
    public bool RootExists()
    {
        if (!Directory.Exists(Root)) return false;

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(Root).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return true; }                // 금고는 있다. 다른 이유로 못 읽는 것이다
        catch (UnauthorizedAccessException) { return true; }
    }

    public void EnsureInsideRoot(string fullPath)
    {
        if (!PathRules.IsInsideRoot(Root, fullPath))
            throw new UnauthorizedAccessException("금고 루트 밖의 경로입니다.");

        if (FindLinkBelowRoot(fullPath) is { } link)
            throw new LinkedPathException(Path.GetRelativePath(Root, link), PathRules.IsReservedArea(Root, link));
    }

    /// <summary>
    /// 루트 아래 구간 중 이미 있는 것이 링크(재파스 포인트)면 그 구간을 돌려준다 (PROHIBITED-CUSTOM-05).
    /// 글자 검사만으로는 스캔 뒤 밖에서 폴더가 정션으로 바뀐 것을 모른다 — 금고 안처럼 보이는 경로가
    /// 금고 밖에 쓰고, .history 가 정션이면 문서를 지울 때 금고 밖 폴더가 지워졌다 [실측].
    /// 루트 자신은 보지 않는다: 금고를 일부러 링크 뒤에 두는 것은 정상 사용이다.
    /// 부르는 쪽이 루트 안 경로임을 먼저 확인한다.
    /// </summary>
    private string? FindLinkBelowRoot(string fullPath)
    {
        var below = PathRules.NormalizeFull(fullPath)[Root.Length..];
        var current = Root;

        foreach (var segment in below.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);

            // DirectoryInfo.Attributes 는 쓰지 않는다 — 없는 경로가 -1 이라 링크로 오판한다 [실측].
            // 못 읽는 경우(권한 등)는 그 예외를 그대로 올려 명령을 멈춘다. 링크라고 단정하지는 않는다.
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return null; }      // 여기부터는 이 명령이 새로 만들 자리다
            catch (DirectoryNotFoundException) { return null; }

            if (attributes.HasFlag(FileAttributes.ReparsePoint)) return current;
        }

        return null;
    }

    public Task<TreeNode> ScanAsync(CancellationToken ct = default)
    {
        // 루트 자체가 사라진 것은 조용히 빈 트리로 넘길 일이 아니다.
        // 사용자는 데이터가 전부 날아갔다고 오해한다. 호출자가 재지정 흐름으로 보낸다.
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException("금고 폴더를 찾을 수 없습니다.");

        return Task.Run(() => ScanFolder(Root, ct), ct);
    }

    private TreeNode ScanFolder(string folder, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var children = new List<TreeNode>();
        var unreadable = false;

        // 폴더 단위로 방어한다. 하위 폴더 하나에 접근 권한이 없다고 스캔 전체가 예외로 끝나면,
        // 기동 시에는 앱이 아예 안 뜨고 F5에서는 명령을 타고 앱이 종료된다.
        // 다만 삼켰다는 사실은 남긴다 — 안 남기면 그 안 문서가 검색에서 조용히 사라진다.
        foreach (var dir in SafeEnumerate(() => Directory.EnumerateDirectories(folder), ref unreadable))
        {
            if (IsReparsePoint(dir)) continue;          // 정션/심볼릭 링크 무한 재귀 차단
            if (IsReservedRoot(dir)) continue;          // .trash / .history
            children.Add(ScanFolder(dir, ct));
        }

        // 글롭을 확장자마다 돌리지 않고 한 번만 열거한 뒤 허용 목록으로 거른다.
        // 허용 목록인 이유: 제외 목록으로 가면 .bak 잔재와 사용자가 떨군 임의 파일이 전부 문서로 뜬다.
        // 확장자 비교는 대소문자를 무시한다 — 무시하지 않으면 .TXT 와 .TBX 가 조용히 빠진다.
        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(folder), ref unreadable))
        {
            if (!PathRules.IsListed(file)) continue;

            // 평문만 확장자를 드러낸다. 감추면 '메모.tbx' 와 '메모.txt' 가 트리·탭·검색 결과에서
            // 모두 '메모' 하나로 겹치고, 아이콘이 없는 탭과 검색 결과에서는 구분할 수단이 사라진다.
            var plain = PathRules.IsPlainText(file);
            var name = plain ? Path.GetFileName(file) : Path.GetFileNameWithoutExtension(file);

            children.Add(new TreeNode(file, name, false, [], IsPlainText: plain));
        }

        // 이름이 같으면 경로로 한 번 더 가른다. 2차 키가 없으면 비교자가 0을 돌려주고
        // List.Sort 는 불안정 정렬이라 새로고침마다 두 행의 위아래가 바뀐다.
        children.Sort((a, b) => a.IsFolder != b.IsFolder
            ? (a.IsFolder ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture) is var byName && byName != 0
                ? byName
                : string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));

        return new TreeNode(folder, Path.GetFileName(folder), true, children, unreadable);
    }

    private static List<string> SafeEnumerate(Func<IEnumerable<string>> enumerate, ref bool failed)
    {
        try { return enumerate().ToList(); }
        catch (UnauthorizedAccessException) { failed = true; return []; }
        catch (DirectoryNotFoundException) { failed = true; return []; }
        catch (IOException) { failed = true; return []; }
    }

    private static bool IsReparsePoint(string dir)
    {
        try { return new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (IOException) { return true; }               // 속성을 못 읽으면 들어가지 않는 쪽이 안전하다
        catch (UnauthorizedAccessException) { return true; }
    }

    private string HistoryRoot => Path.Combine(Root, PathRules.HistoryFolderName);

    private bool IsReservedRoot(string dir)
    {
        var normalized = PathRules.NormalizeFull(dir);
        return string.Equals(normalized, PathRules.NormalizeFull(TrashRoot), StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, PathRules.NormalizeFull(HistoryRoot), StringComparison.OrdinalIgnoreCase);
    }

    public bool NameTaken(string parentFullPath, string fileOrFolderName)
    {
        var candidate = Path.Combine(parentFullPath, fileOrFolderName);
        return File.Exists(candidate) || Directory.Exists(candidate);   // 메모리 트리가 아니라 디스크를 본다
    }

    public bool FileExists(string fullPath) => File.Exists(fullPath);

    public string CreateFolder(string parentFullPath, string name)
    {
        EnsureInsideRoot(parentFullPath);
        var path = Path.Combine(parentFullPath, name);
        EnsureInsideRoot(path);

        Directory.CreateDirectory(path);
        return path;
    }

    public string Rename(string fullPath, string newName)
    {
        EnsureInsideRoot(fullPath);
        var parent = Path.GetDirectoryName(fullPath)!;

        // 이력을 두고 가면 옛 이름의 고아 이력이 남는다 — 그 안에는 이름을 바꾸기 전의 값이
        // 복호화 가능한 상태로 들어 있고, 앱 어디에도 나타나지 않아 사용자는 존재조차 모른다.
        // 폴더도 마찬가지다: 폴더 이름만 바꿔도 그 안 모든 문서의 "열었을 때 상태 보기"가 끊긴다.
        if (Directory.Exists(fullPath))
        {
            var destFolder = Path.Combine(parent, newName);
            EnsureInsideRoot(destFolder);
            EnsureHistoryMovable(fullPath, destFolder);
            Directory.Move(fullPath, destFolder);
            MoveHistoryFolder(fullPath, destFolder);
            return destFolder;
        }

        // 원본 확장자를 보존한다. .tbx 를 강제로 붙이던 예전 코드는 평문 파일을 파괴했다 —
        // 이름 변경 대화상자의 초기값이 확장자를 뗀 이름이라, .txt 를 고르고 아무것도 고치지 않고
        // 확인만 눌러도 .tbx 가 되고 그 파일은 매직 검사에 걸려 앱 안에서 영영 못 열린다.
        var destDoc = Path.Combine(parent, newName + Path.GetExtension(fullPath));
        EnsureInsideRoot(destDoc);
        EnsureHistoryMovable(fullPath, destDoc);
        File.Move(fullPath, destDoc);
        MoveHistoryFolder(fullPath, destDoc);

        return destDoc;
    }

    /// <summary>
    /// 문서든 폴더든 이력 하위 트리를 같은 상대 경로로 옮긴다.
    /// 본체를 먼저 옮긴 뒤에 부른다 — 본체가 이름 충돌로 실패하면 이력은 손대지 않은 채 끝난다.
    /// </summary>
    private void MoveHistoryFolder(string oldPath, string newPath)
    {
        var source = PathRules.HistoryFolderFor(Root, oldPath);
        var destination = PathRules.HistoryFolderFor(Root, newPath);
        EnsureInsideRoot(source);
        EnsureInsideRoot(destination);

        // 같은 자리면 할 일이 없다. 예전에는 여기서 destination 을 먼저 지운 뒤
        // Move 가 실패해 "유일한 되돌릴 지점"이 사라졌다 [실측].
        if (string.Equals(PathRules.NormalizeFull(source), PathRules.NormalizeFull(destination),
                          StringComparison.OrdinalIgnoreCase)) return;

        if (!Directory.Exists(source)) return;

        // 목적지 이력을 말없이 지우지 않는다. 그쪽 문서의 되돌릴 지점이다.
        if (Directory.Exists(destination))
            throw new IOException("옮길 위치에 같은 이름의 이력이 이미 있습니다.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
    }

    /// <summary>
    /// 본체를 옮기기 전에 부른다 (D-037). 이력 쪽에서 막히는 것을 본체를 옮긴 뒤에 알면
    /// 본체만 옮겨진 반쪽 상태가 된다 — 호출자는 실패로 보고 탭 경로를 갱신하지 않아
    /// 탭이 옛 경로를 쥔 채 남는다.
    /// </summary>
    private void EnsureHistoryMovable(string oldPath, string newPath)
    {
        EnsureInsideRoot(PathRules.HistoryFolderFor(Root, oldPath));
        EnsureInsideRoot(PathRules.HistoryFolderFor(Root, newPath));
    }

    /// 남겨두면 지운 것의 값이 .history 에 복호화 가능한 상태로 계속 남는다.
    /// 경로 검사를 빼면 안 된다: 조상(.history 등)이 정션이면 재귀 삭제가 그 너머의
    /// 금고 밖 폴더를 실제로 지운다 [실측].
    private void DeleteHistoryFor(string fullPath)
    {
        var history = PathRules.HistoryFolderFor(Root, fullPath);
        EnsureInsideRoot(history);
        if (Directory.Exists(history)) Directory.Delete(history, recursive: true);
    }

    public MoveCheck CheckMove(string sourceFullPath, string destinationFolder)
    {
        if (!PathRules.IsInsideRoot(Root, sourceFullPath) || !PathRules.IsInsideRoot(Root, destinationFolder))
            return new MoveCheck(MoveRejection.OutsideRoot);

        if (PathRules.IsReservedArea(Root, sourceFullPath) || PathRules.IsReservedArea(Root, destinationFolder))
            return new MoveCheck(MoveRejection.ReservedFolder);

        var isFolder = Directory.Exists(sourceFullPath);
        if (!isFolder && !File.Exists(sourceFullPath)) return new MoveCheck(MoveRejection.SourceMissing);
        if (!Directory.Exists(destinationFolder)) return new MoveCheck(MoveRejection.DestinationNotFolder);

        var currentParent = Path.GetDirectoryName(sourceFullPath);
        if (currentParent is not null
            && string.Equals(PathRules.NormalizeFull(currentParent), PathRules.NormalizeFull(destinationFolder),
                             StringComparison.OrdinalIgnoreCase))
            return new MoveCheck(MoveRejection.SameLocation);

        // 폴더를 자기 자손으로 옮기면 OS 가 IOException 으로 막지만 이유를 설명하지 않는다
        if (isFolder && PathRules.IsInsideRoot(sourceFullPath, destinationFolder))
            return new MoveCheck(MoveRejection.IntoItself);

        var leaf = Path.GetFileName(sourceFullPath);
        if (NameTaken(destinationFolder, leaf)) return new MoveCheck(MoveRejection.NameTaken);

        // 한계를 정하는 것은 옮기는 항목 자신이 아니라 가장 깊은 자손이다
        var destinationPath = Path.Combine(destinationFolder, leaf);
        var grew = destinationPath.Length - PathRules.NormalizeFull(sourceFullPath).Length;
        if (DeepestDerivedLength(sourceFullPath, "00000000-000000") + grew > PathRules.MaxPathLength)
            return new MoveCheck(MoveRejection.PathTooLong);

        return MoveCheck.Ok;
    }

    public string Move(string sourceFullPath, string destinationFolder)
    {
        var check = CheckMove(sourceFullPath, destinationFolder);
        if (!check.CanMove) throw new InvalidOperationException(check.Message);

        var destination = Path.Combine(destinationFolder, Path.GetFileName(sourceFullPath));

        // CheckMove 는 드래그 중 커서 판정에도 쓰여 글자만 본다. 디스크의 링크는 실행 직전에,
        // 본체를 옮기기 전에 이력 쪽까지 본다 (D-037).
        EnsureInsideRoot(sourceFullPath);
        EnsureInsideRoot(destination);
        EnsureHistoryMovable(sourceFullPath, destination);

        // 본체를 먼저 옮긴다. 실패하면 이력은 손대지 않은 채 끝난다 (Task 1의 순서 규칙).
        if (Directory.Exists(sourceFullPath)) Directory.Move(sourceFullPath, destination);
        else File.Move(sourceFullPath, destination);

        MoveHistoryFolder(sourceFullPath, destination);
        return destination;
    }

    public int DeepestDerivedLength(string fullPath, string stamp)
    {
        var longest = PathRules.LongestDerivedLength(Root, fullPath, stamp);
        if (!Directory.Exists(fullPath)) return longest;

        // 시작점이 링크 너머면 열거하지 않는다. AttributesToSkip 은 시작점을 거르지 않아
        // 금고 밖 하위 전체를 드래그 중 매번 읽는다 [실측]. 여기는 커서 판정이라 던지지 않는다 —
        // 실제 거부는 Move 가 한다.
        if (!PathRules.IsInsideRoot(Root, fullPath) || CrossesLink(fullPath)) return longest;

        foreach (var descendant in SafeEnumerateDeep(fullPath))
        {
            longest = Math.Max(longest, PathRules.LongestDerivedLength(Root, descendant, stamp));
        }

        return longest;
    }

    /// <summary>
    /// 링크 안으로 들어가지 않는 재귀 열거 (D-038). SearchOption.AllDirectories 는 정션을 따라가
    /// 금고 밖 항목까지 센다 [실측]. 나머지 세 값은 SearchOption 오버로드의 동작을 그대로 옮긴 것이다 —
    /// new EnumerationOptions() 의 기본값은 숨김·시스템 파일을 건너뛰어 개수가 달라진다.
    /// </summary>
    private static readonly EnumerationOptions DeepWithoutLinks = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        MatchType = MatchType.Win32,
    };

    private bool CrossesLink(string fullPath)
    {
        try { return FindLinkBelowRoot(fullPath) is not null; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static List<string> SafeEnumerateDeep(string folder)
    {
        try { return Directory.EnumerateFileSystemEntries(folder, "*", DeepWithoutLinks).ToList(); }
        catch (UnauthorizedAccessException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (IOException) { return []; }
    }

    public void MoveToTrash(string fullPath, string stamp)
    {
        EnsureInsideRoot(fullPath);

        // 본체를 옮기기 전에 휴지통·이력 쪽까지 본다 (D-037). .trash 쪽 조상이 정션이면
        // 금고 밖으로 옮겨지고, .history 쪽이면 이력 삭제가 금고 밖 폴더를 지운다.
        var destination = PathRules.TrashPathFor(Root, fullPath, stamp);
        EnsureInsideRoot(destination);
        EnsureInsideRoot(PathRules.HistoryFolderFor(Root, fullPath));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // 이력도 함께 버린다 — 폴더든 문서든. 남겨두면 지운 것의 값이 .history 에
        // 계속 복호화 가능한 상태로 남고, 사용자는 지웠다고 믿는다.
        if (Directory.Exists(fullPath))
        {
            Directory.Move(fullPath, destination);
            DeleteHistoryFor(fullPath);
            TrySetHiddenDirectory(TrashRoot);
            return;
        }

        File.Move(fullPath, destination);
        DeleteHistoryFor(fullPath);

        TrySetHiddenDirectory(TrashRoot);
    }

    public int CountTrashItems()
    {
        if (!Directory.Exists(TrashRoot)) return 0;

        // 시작점은 AttributesToSkip 이 거르지 않는다 — .trash 자체가 정션이면 금고 밖을 센다 [실측]
        EnsureInsideRoot(TrashRoot);
        return Directory.EnumerateFiles(TrashRoot, "*", DeepWithoutLinks).Count();
    }

    public void EmptyTrash()
    {
        if (!Directory.Exists(TrashRoot)) return;

        EnsureInsideRoot(TrashRoot);
        Directory.Delete(TrashRoot, recursive: true);
    }

    private static void TrySetHiddenDirectory(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (info.Exists) info.Attributes |= FileAttributes.Hidden;
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
