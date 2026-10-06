using System.IO;
using System.Text.Json;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

public sealed class AppSettingsService(string filePath) : IAppSettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// 루트 경로 자체는 비밀이 아니고, 이걸 암호화하면 앱이 자기 설정을 못 읽는 순환이 생긴다.
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextBean", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public string SuggestedDefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TextBean");

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Current = new AppSettings();
                return;
            }

            await using var stream = File.OpenRead(filePath);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // 설정을 못 읽는다고 앱이 아예 못 뜨면 안 된다. 기본값으로 시작하고 루트를 다시 묻는다.
            AppLog.Warn("settings-load", null, ex);
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, Current, Options);
    }
}
