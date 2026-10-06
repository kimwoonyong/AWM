using System.IO;

namespace TextBean.Services;

/// <summary>
/// 파일 경로와 예외 타입까지만 남긴다. 문서 내용과 복호화된 값은 어떤 레벨에서도 기록하지 않는다
/// (PROHIBITED-CUSTOM-04). 예외 메시지를 그대로 찍는 것이 값이 새는 경로이므로 타입 이름만 쓴다.
/// 암호화해 둔 것을 로그로 평문 유출하면 설계 전체가 무의미해진다.
/// </summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    /// 테스트에서 임시 폴더로 바꿀 수 있게 열어 둔다.
    public static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TextBean", "logs");

    public static void Warn(string action, string? path, Exception? ex) => Write("WARN", action, path, ex);

    public static void Error(string action, string? path, Exception? ex) => Write("ERROR", action, path, ex);

    private static void Write(string level, string action, string? path, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = Path.Combine(Directory, $"textbean-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTime.Now:HH:mm:ss}\t{level}\t{action}\t{path ?? "-"}\t{ex?.GetType().Name ?? "-"}";

            lock (Gate) File.AppendAllLines(file, [line]);
        }
        catch
        {
            // 로그를 못 남기는 것이 앱 동작을 막으면 안 된다
        }
    }
}
