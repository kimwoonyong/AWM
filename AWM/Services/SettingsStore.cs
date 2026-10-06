using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AWM.Models;
using AWM.Services.Interfaces;

namespace AWM.Services;

public sealed class SettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 한글을 \uXXXX 로 바꾸지 않는다 [실측 — add-draft-storage] (D-007)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    // TextBean 과 같은 자리 (D-007)
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AWM", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public string? Load()
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            // 메모장 등이 붙이는 UTF-8 BOM 은 JSON 파서가 거부한다 [실측] — 손으로 고친 설정이 통째로 기본값이 되지 않게 건너뛴다
            ReadOnlySpan<byte> bytes = File.ReadAllBytes(filePath);
            if (bytes.StartsWith(Utf8Bom))
                bytes = bytes[Utf8Bom.Length..];
            var loaded = JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions)
                         ?? throw new JsonException("빈 설정");
            Current = Repair(loaded, out var repaired);
            return repaired ? "설정 일부가 올바르지 않아 그 값만 고쳤습니다(범위 밖 값은 기본값, 너무 긴 직접 쓰기 지침은 앞 2,000자만)." : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 손으로 고친 파일을 바로 덮어 잃지 않게 남겨 둔다 (D-008)
            var backup = filePath + ".bak";
            try
            {
                File.Copy(filePath, backup, overwrite: true);
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                return $"설정 파일을 읽지 못해 기본값으로 시작합니다({ex.Message}). 원본을 따로 남기지도 못했습니다.";
            }
            Current = new AppSettings();
            return $"설정 파일을 읽지 못해 기본값으로 시작합니다. 원본은 {Path.GetFileName(backup)} 로 남겼습니다.";
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var folder = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(folder);
        var temp = filePath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        try
        {
            File.Delete(temp);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // 같은 폴더에 쓰고 교체한다 — 쓰는 중 꺼져도 원래 설정이 반쯤 덮이지 않는다
            if (File.Exists(filePath))
                File.Replace(temp, filePath, null, ignoreMetadataErrors: true);
            else
                File.Move(temp, filePath);
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
        Current = settings;
    }

    /// <summary>
    /// 범위 밖·모르는 값은 그 값만 기본값으로 (D-008).
    /// </summary>
    private static AppSettings Repair(AppSettings loaded, out bool repaired)
    {
        var defaults = new AppSettings();
        var result = loaded with { Version = AppSettings.CurrentVersion, CustomTone = loaded.CustomTone ?? "" };
        repaired = false;

        // 손으로 고쳐 한도를 넘긴 지침은 지우지 않고 앞부분만 남긴다 (D-013)
        if (result.CustomTone.Length > AppSettings.CustomToneLimit)
        {
            result = result with { CustomTone = result.CustomTone[..AppSettings.CustomToneLimit] };
            repaired = true;
        }

        if (!Enum.IsDefined(loaded.Length))
        {
            result = result with { Length = defaults.Length };
            repaired = true;
        }
        if (!Enum.IsDefined(loaded.Tone))
        {
            result = result with { Tone = defaults.Tone };
            repaired = true;
        }
        if (!AppSettings.IsValidTagRange(loaded.TagMin, loaded.TagMax))
        {
            result = result with { TagMin = defaults.TagMin, TagMax = defaults.TagMax };
            repaired = true;
        }
        if (loaded.DraftsFolder is { } folder && !Path.IsPathFullyQualified(folder))
        {
            result = result with { DraftsFolder = null };
            repaired = true;
        }
        return result;
    }
}
