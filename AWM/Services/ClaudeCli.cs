using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AWM.Models;
using AWM.Services.Interfaces;

namespace AWM.Services;

/// <summary>
/// claude.exe 프로세스를 다루는 유일한 곳. 로그인은 사용자가 터미널에서 한 것을 그대로 쓴다 — 앱은 자격 증명을 만지지 않는다.
/// </summary>
public sealed class ClaudeCli : IClaudeCli
{
    private const string Model = "sonnet";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(180);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // stdin 상한은 10MB [문서 — headless 조사]. 여유를 둔다
    private const int MaxInputBytes = 8 * 1024 * 1024;

    // -p 는 이 변수가 있으면 묻지 않고 API 키로 과금한다 [문서]. 앱이 띄운 실행은 늘 구독으로 간다 (D-005)
    private static readonly string[] StrippedVariables = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    public Task<JsonElement> RunAsync(string prompt, string systemPrompt, string jsonSchema, CancellationToken ct) =>
        RunAsync(prompt, [], systemPrompt, jsonSchema, ct);

    public async Task<JsonElement> RunAsync(string prompt, IReadOnlyList<ImageInput> images, string systemPrompt,
        string jsonSchema, CancellationToken ct)
    {
        // 그림은 글 입력으로 넘길 수 없다 — stream-json 입력의 user 메시지 블록으로 넘긴다 [실측 — add-draft-images 관찰 5]
        var withImages = images.Count > 0;
        var input = withImages ? BuildImageMessage(prompt, images) : prompt;
        if (withImages && Utf8NoBom.GetByteCount(input) > MaxInputBytes)
            throw new ClaudeCliException(ClaudeCliFailure.Failed,
                $"보낼 사진이 너무 큽니다({images.Count}장). 사진 수를 줄여 주세요.");

        var exe = FindExecutable();
        using var process = new Process { StartInfo = BuildStartInfo(exe, systemPrompt, jsonSchema, withImages) };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new ClaudeCliException(ClaudeCliFailure.NotInstalled, $"Claude Code CLI를 실행하지 못했습니다: {exe}\n{ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 취소 콜백 안에서 바로 죽인다 — 앱 종료(OnExit) 중에 취소해도 비동기 뒤처리를 기다리지 않고 프로세스가 남지 않게
        using var killOnStop = stop.Token.Register(() => Kill(process));
        stop.CancelAfter(Timeout);

        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // 입력을 읽기 전에 끝난 경우(로그인 안 됨 등) 파이프가 닫힌다. 이유는 stdout 에 있으니 그쪽으로 판정한다
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (stop.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw new ClaudeCliException(ClaudeCliFailure.TimedOut, $"응답이 {Timeout.TotalSeconds:0}초 안에 오지 않아 중단했습니다.");
        }

        // stream-json 출력은 줄마다 사건 하나다. 판정은 마지막 result 줄로 — 규칙은 json 출력과 같다 (D-006)
        return Interpret(process.ExitCode, withImages ? LastResultLine(stdout) : stdout, stderr);
    }

    private static string BuildImageMessage(string prompt, IReadOnlyList<ImageInput> images)
    {
        var content = new List<object>();
        foreach (var image in images)
        {
            // 그림마다 파일 이름을 붙여 어느 그림이 어느 파일인지 알게 한다
            content.Add(new { type = "text", text = $"사진 {image.FileName}" });
            content.Add(new
            {
                type = "image",
                source = new { type = "base64", media_type = image.MediaType, data = Convert.ToBase64String(image.Data) },
            });
        }
        content.Add(new { type = "text", text = prompt });

        var message = new { type = "user", message = new { role = "user", content } };
        return JsonSerializer.Serialize(message) + "\n";
    }

    private static string LastResultLine(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            if (line.Contains("\"type\":\"result\"", StringComparison.Ordinal))
                return line.Trim();
        }
        // result 줄이 없으면 그대로 넘겨 형식 오류로 판정되게 한다
        return stdout;
    }

    private static string FindExecutable()
    {
        // npm 은 %APPDATA%\npm 아래에, 네이티브 설치는 %USERPROFILE%\.local\bin 에 둔다.
        // claude.cmd 는 bin\claude.exe 를 부르는 껍데기라 [실측] exe 를 바로 부른다 — cmd.exe 인자 해석을 피한다 (D-004)
        var appData = Environment.GetEnvironmentVariable("APPDATA") ?? "";
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
        string[] candidates =
        [
            Path.Combine(appData, "npm", "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe"),
            Path.Combine(userProfile, ".local", "bin", "claude.exe"),
        ];

        foreach (var candidate in candidates)
        {
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                return candidate;
        }

        throw new ClaudeCliException(ClaudeCliFailure.NotInstalled,
            "Claude Code CLI를 찾지 못했습니다. 찾아본 경로:\n" + string.Join("\n", candidates));
    }

    private static ProcessStartInfo BuildStartInfo(string exe, string systemPrompt, string jsonSchema, bool streamJson)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // 빈 폴더에서 실행한다 — 현재 폴더의 CLAUDE.md · 프로젝트 설정이 지침에 섞이지 않게
            WorkingDirectory = EnsureWorkingDirectory(),
        };

        string[] arguments =
        [
            "-p",
            "--json-schema", jsonSchema,
            "--system-prompt", systemPrompt,
            "--tools", "",
            "--no-session-persistence",
            "--strict-mcp-config",
            "--disable-slash-commands",
            // 사용자 전역 훅이 호출마다 약 4,000토큰을 끼워 넣는다 [실측] (D-009)
            "--setting-sources", "project,local",
            "--settings", """{"disableAllHooks":true}""",
            "--model", Model,
        ];
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        // 그림을 넘길 때만 stream-json 입출력 (--verbose 는 stream-json 출력에 필요하다)
        string[] format = streamJson
            ? ["--input-format", "stream-json", "--output-format", "stream-json", "--verbose"]
            : ["--output-format", "json"];
        foreach (var argument in format)
            psi.ArgumentList.Add(argument);

        foreach (var name in StrippedVariables)
            psi.Environment.Remove(name);

        return psi;
    }

    private static string EnsureWorkingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AWM", "claude-cwd");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 취소와 정상 종료가 겹쳐 이미 끝난 경우
        }
    }

    private static JsonElement Interpret(int exitCode, string stdout, string stderr)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stdout);
        }
        catch (JsonException)
        {
            var failure = exitCode != 0 ? ClaudeCliFailure.Failed : ClaudeCliFailure.BadOutput;
            throw new ClaudeCliException(failure,
                $"Claude 응답을 읽지 못했습니다 (종료 코드 {exitCode}).\n{Head(stderr.Length > 0 ? stderr : stdout)}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ClaudeCliException(ClaudeCliFailure.BadOutput, $"Claude 응답 형식이 예상과 다릅니다.\n{Head(stdout)}");

            // 실패해도 subtype 은 "success" 로 온다 [실측]. 종료 코드와 is_error 만 본다 (D-006)
            var isError = root.TryGetProperty("is_error", out var flag) && flag.ValueKind == JsonValueKind.True;
            if (exitCode != 0 || isError)
            {
                var reason = root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String
                    ? result.GetString()
                    : Head(stderr);
                throw new ClaudeCliException(ClaudeCliFailure.Failed, $"Claude가 실패를 알렸습니다: {reason} (종료 코드 {exitCode})");
            }

            if (!root.TryGetProperty("structured_output", out var output) || output.ValueKind != JsonValueKind.Object)
                throw new ClaudeCliException(ClaudeCliFailure.BadOutput, $"Claude 응답에 초안이 없습니다.\n{Head(stdout)}");

            return output.Clone();
        }
    }

    private static string Head(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
