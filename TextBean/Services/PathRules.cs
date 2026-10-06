using System.IO;

namespace TextBean.Services;

public enum NameCheckResult
{
    Ok,
    Empty,
    InvalidChars,
    ReservedDeviceName,
    TrailingDotOrSpace,
    ReservedTrashName
}

/// <summary>
/// 경로 정규화·이름 검증·파생 경로 계산. 순수 함수만 둔다 — 디스크에 닿지 않는다.
/// </summary>
public static class PathRules
{
    public const string DocumentExtension = ".tbx";

    /// <summary>
    /// 앱이 암호화하지 않는 참고 파일. 금고 문서가 아니라 "보기만 하는 평문"이다.
    /// 종류는 내용이 아니라 확장자가 정한다 — 내용으로 가르면 "평문인데 .tbx 인 파일"이
    /// 문서로 읽혀 D-005 의 읽기 실패 잠금이 무너진다.
    /// </summary>
    public const string PlainTextExtension = ".txt";

    public const string TrashFolderName = ".trash";
    public const string HistoryFolderName = ".history";

    /// 트리에 싣는 확장자. 제외 목록이 아니라 허용 목록이다 —
    /// 제외 목록으로 가면 .bak 이나 사용자가 떨군 임의 파일이 전부 문서로 뜬다.
    public static readonly string[] ListedExtensions = [DocumentExtension, PlainTextExtension];

    public static bool IsDocument(string path) => HasExtension(path, DocumentExtension);

    public static bool IsPlainText(string path) => HasExtension(path, PlainTextExtension);

    public static bool IsListed(string path) => IsDocument(path) || IsPlainText(path);

    /// 대소문자를 무시한다. 무시하지 않으면 .TXT 와 .TBX 가 트리에서 조용히 빠진다.
    private static bool HasExtension(string path, string extension)
        => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    /// 문서를 연 시점의 내용 한 벌. 자동 저장이 만드는 위험은 "이번에 연 뒤로 내가 망친 것"이고,
    /// 그건 연 시점 상태 하나면 정확히 덮인다. 세대를 쌓으면 바꾼 옛 비밀값이 계속 남는다 (D-016).
    public const string SnapshotFileName = "opened" + DocumentExtension;

    /// app.manifest 에 longPathAware 를 켜 두었고 이 PC는 LongPathsEnabled=1 이다.
    /// 그래도 한계는 존재하므로 파생 경로(.bak, .trash 접두사)까지 포함해 검사한다.
    public const int MaxPathLength = 32000;

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    public static string NormalizeFull(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsInsideRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeFull(root);
        var normalizedCandidate = NormalizeFull(candidate);

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)) return true;

        // 구분자를 붙여 비교해야 C:\Vault 와 C:\VaultOther 를 가른다
        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static NameCheckResult CheckName(string name, bool directlyUnderRoot)
    {
        if (string.IsNullOrWhiteSpace(name)) return NameCheckResult.Empty;

        if (directlyUnderRoot
            && (string.Equals(name, TrashFolderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, HistoryFolderName, StringComparison.OrdinalIgnoreCase)))
            return NameCheckResult.ReservedTrashName;

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return NameCheckResult.InvalidChars;
        if (name.EndsWith('.') || name.EndsWith(' ')) return NameCheckResult.TrailingDotOrSpace;

        var stem = Path.GetFileNameWithoutExtension(name);
        foreach (var reserved in ReservedNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
                return NameCheckResult.ReservedDeviceName;
        }

        return NameCheckResult.Ok;
    }

    /// 한 문서의 이력이 모이는 폴더. 문서의 루트 기준 상대 경로를 그대로 따라간다.
    public static string HistoryFolderFor(string root, string documentPath)
    {
        var normalizedRoot = NormalizeFull(root);
        var relative = Path.GetRelativePath(normalizedRoot, NormalizeFull(documentPath));
        return Path.Combine(normalizedRoot, HistoryFolderName, relative);
    }

    public static string SnapshotPathFor(string root, string documentPath)
        => Path.Combine(HistoryFolderFor(root, documentPath), SnapshotFileName);

    /// <summary>
    /// <see cref="SnapshotPathFor"/> 의 역 — "열었을 때 상태" 파일에서 원본 문서 경로를 구한다.
    /// .history 아래의 열었을 때 상태 파일이 아니면 null.
    /// </summary>
    public static string? DocumentPathForSnapshot(string root, string snapshotPath)
    {
        var normalizedRoot = NormalizeFull(root);
        var history = Path.Combine(normalizedRoot, HistoryFolderName);
        var full = NormalizeFull(snapshotPath);

        if (!string.Equals(Path.GetFileName(full), SnapshotFileName, StringComparison.OrdinalIgnoreCase)) return null;
        if (Path.GetDirectoryName(full) is not { } folder || !IsInsideRoot(history, folder)) return null;

        var relative = Path.GetRelativePath(history, folder);
        return relative == "." ? null : Path.Combine(normalizedRoot, relative);
    }

    /// <summary>
    /// 앱이 쓰는 영역(.trash · .history) 안인지 경로로 판정한다.
    /// CheckName 의 예약어 검사는 directlyUnderRoot 일 때만 돌아 하위 폴더를 못 잡는다.
    /// </summary>
    public static bool IsReservedArea(string root, string candidate)
    {
        var normalizedRoot = NormalizeFull(root);

        foreach (var reserved in new[] { TrashFolderName, HistoryFolderName })
        {
            if (IsInsideRoot(Path.Combine(normalizedRoot, reserved), candidate)) return true;
        }

        return false;
    }

    public static string TrashPathFor(string root, string itemFullPath, string stamp)
    {
        var normalizedRoot = NormalizeFull(root);
        var relative = Path.GetRelativePath(normalizedRoot, NormalizeFull(itemFullPath));
        return Path.Combine(normalizedRoot, TrashFolderName, stamp, relative);
    }

    /// 문서 하나가 만들어 낼 수 있는 가장 긴 경로. .tbx 경로만 재면
    /// "만들 수는 있는데 두 번째 저장부터 영영 실패하는 문서" 또는
    /// "만들 수는 있는데 지울 수 없는 문서"가 생긴다.
    public static int LongestDerivedLength(string root, string itemFullPath, string stamp)
        => Math.Max(SnapshotPathFor(root, itemFullPath).Length,
                    TrashPathFor(root, itemFullPath, stamp).Length);

    public static string MessageFor(NameCheckResult result) => result switch
    {
        NameCheckResult.Ok => "",
        NameCheckResult.Empty => "이름을 입력해주세요.",
        NameCheckResult.InvalidChars => @"이름에 \ / : * ? "" < > | 문자는 쓸 수 없습니다.",
        NameCheckResult.ReservedDeviceName => "Windows가 예약한 이름이라 쓸 수 없습니다.",
        NameCheckResult.TrailingDotOrSpace => "이름 끝에 마침표나 공백을 둘 수 없습니다.",
        NameCheckResult.ReservedTrashName => ".trash 와 .history 는 앱이 쓰는 이름이라 만들 수 없습니다.",
        _ => "이름을 쓸 수 없습니다."
    };
}
