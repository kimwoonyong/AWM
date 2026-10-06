using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using AWM.Models;
using AWM.Services.Interfaces;

namespace AWM.Services;

public sealed class DraftStore(string root) : IDraftStore
{
    private const string FileName = "draft.json";
    private const int MaxTitleLength = 40;
    private const string UntitledName = "제목 없음";
    private const string ImagesFolder = "images";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 기본값은 한글을 \uXXXX 로 바꾼다 [실측]. 로컬 파일이라 HTML 용 이스케이프가 필요 없다 (D-008)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Root { get; } = root;

    public async Task<StoredDraft> CreateAsync(SavedDraft draft)
    {
        Directory.CreateDirectory(Root);
        var folder = ReserveFolder(BuildFolderName(draft), currentFolder: null);
        Directory.CreateDirectory(folder);
        try
        {
            await WriteAsync(folder, draft).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteEmptyFolder(folder);
            throw;
        }
        return new StoredDraft(folder, draft);
    }

    public async Task<(StoredDraft Stored, bool RenameFailed)> SaveAsync(string folder, SavedDraft draft)
    {
        if (!Directory.Exists(folder))
            throw new DraftFolderMissingException(folder);

        // 내용을 먼저 저장한다 — 이름 변경이 실패해도 고친 글은 남는다 (D-004)
        await WriteAsync(folder, draft).ConfigureAwait(false);

        var baseName = BuildFolderName(draft);
        if (HasBaseName(Path.GetFileName(folder), baseName))
            return (new StoredDraft(folder, draft), false);

        var renamed = ReserveFolder(baseName, folder);
        try
        {
            Directory.Move(folder, renamed);
            return (new StoredDraft(renamed, draft), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (new StoredDraft(folder, draft), true);
        }
    }

    public async Task<DraftListResult> ListAsync()
    {
        if (!Directory.Exists(Root))
            return new DraftListResult([], 0);

        var drafts = new List<StoredDraft>();
        var unreadable = 0;
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            try
            {
                drafts.Add(new StoredDraft(folder, await ReadAsync(folder).ConfigureAwait(false)));
            }
            catch (Exception ex) when (IsUnreadable(ex))
            {
                unreadable++;
            }
        }

        drafts.Sort((a, b) => b.Draft.UpdatedAt.CompareTo(a.Draft.UpdatedAt));
        return new DraftListResult(drafts, unreadable);
    }

    public async Task<StoredDraft> OpenAsync(string folder)
    {
        if (!Directory.Exists(folder))
            throw new DraftFolderMissingException(folder);
        return new StoredDraft(folder, await ReadAsync(folder).ConfigureAwait(false));
    }

    public async Task<string> AddImageAsync(string folder, string sourcePath)
    {
        if (!Directory.Exists(folder))
            throw new DraftFolderMissingException(folder);

        var images = Directory.CreateDirectory(Path.Combine(folder, ImagesFolder)).FullName;
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        for (var number = NextImageNumber(images); ; number++)
        {
            var name = $"{number:00}{extension}";
            FileStream target;
            try
            {
                // 같은 이름이 있으면 덮어쓰지 않고 다음 번호로
                target = new FileStream(Path.Combine(images, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            }
            catch (IOException) when (File.Exists(Path.Combine(images, name)))
            {
                continue;
            }

            try
            {
                await using (target)
                    await source.CopyToAsync(target).ConfigureAwait(false);
                return name;
            }
            catch
            {
                TryDelete(Path.Combine(images, name));
                throw;
            }
        }
    }

    public string? FindImage(string folder, string fileName)
    {
        // 사진 줄의 파일 이름은 사용자가 고칠 수 있다 — images\ 밖을 가리키지 못하게 이름만 받는다
        if (fileName != Path.GetFileName(fileName) || fileName.Length == 0)
            return null;
        var path = Path.Combine(folder, ImagesFolder, fileName);
        return File.Exists(path) ? path : null;
    }

    public Task<byte[]> ReadImageAsync(string path) => File.ReadAllBytesAsync(path);

    private static int NextImageNumber(string imagesFolder)
    {
        var max = 0;
        foreach (var file in Directory.EnumerateFiles(imagesFolder))
        {
            if (int.TryParse(Path.GetFileNameWithoutExtension(file), out var number) && number > max)
                max = number;
        }
        return max + 1;
    }

    /// <summary>
    /// 읽기 실패로 칠 예외. 목록은 이것들만 개수로 세고, 나머지는 그대로 올린다.
    /// </summary>
    public static bool IsUnreadable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;

    // 시각은 처음 만든 시각으로 고정한다 — 이름을 바꿔도 만든 날이 남는다 (D-007)
    private static string BuildFolderName(SavedDraft draft) =>
        $"{draft.CreatedAt.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)} ({SanitizeTitle(draft.Title)})";

    private static string SanitizeTitle(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();
        if (name.Length > MaxTitleLength)
            name = name[..MaxTitleLength];
        if (name.Length > 0 && char.IsHighSurrogate(name[^1]))
            name = name[..^1];
        name = name.TrimEnd('.', ' ');
        return name.Length == 0 ? UntitledName : name;
    }

    /// <summary>
    /// "이름" 또는 "이름 (2)" 꼴이면 같은 이름으로 본다 — 번호 때문에 저장할 때마다 이름을 바꾸지 않게.
    /// </summary>
    private static bool HasBaseName(string folderName, string baseName)
    {
        if (string.Equals(folderName, baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = baseName + " (";
        return folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && folderName.EndsWith(')')
               && int.TryParse(folderName.AsSpan(prefix.Length, folderName.Length - prefix.Length - 1), out _);
    }

    private string ReserveFolder(string baseName, string? currentFolder)
    {
        for (var number = 1; ; number++)
        {
            var name = number == 1 ? baseName : $"{baseName} ({number})";
            var candidate = Path.Combine(Root, name);
            if (currentFolder is not null && string.Equals(candidate, currentFolder, StringComparison.OrdinalIgnoreCase))
                return candidate;
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }
    }

    private static async Task<SavedDraft> ReadAsync(string folder)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, FileName)).ConfigureAwait(false);
        var draft = JsonSerializer.Deserialize<SavedDraft>(bytes, JsonOptions)
                    ?? throw new InvalidDataException("draft.json 이 비어 있습니다.");
        if (draft.Version != SavedDraft.CurrentVersion)
            throw new InvalidDataException($"지원하지 않는 draft.json 버전입니다: {draft.Version}");
        // json 의 null 은 init 기본값을 덮는다 — 화면이 null 을 받지 않게 여기서 막는다
        if (draft.Title is null || draft.Body is null || draft.Tags is null || draft.Request is null)
            throw new InvalidDataException("draft.json 에 빠진 항목이 있습니다.");
        return draft;
    }

    // 같은 폴더에 쓰고 교체한다 — 쓰는 중 꺼져도 원본이 반쯤 덮이지 않는다 (D-006)
    private static async Task WriteAsync(string folder, SavedDraft draft)
    {
        var path = Path.Combine(folder, FileName);
        var temp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(draft, JsonOptions);
        try
        {
            File.Delete(temp);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                await ReplaceAsync(temp, path).ConfigureAwait(false);
            else
                File.Move(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// 방금 쓴 파일을 백신이 잠깐 잡아 교체가 실패할 수 있다 [실측 — TextBean]. 공유·잠금 위반일 때만 다시 한다.
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
                await Task.Delay(50 * attempt).ConfigureAwait(false);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 남아도 다음 저장이 먼저 지운다
        }
    }

    private static void TryDeleteEmptyFolder(string folder)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 빈 폴더가 남으면 목록에서 "읽지 못한 폴더"로 보인다
        }
    }
}
