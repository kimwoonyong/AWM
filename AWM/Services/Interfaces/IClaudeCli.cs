using System.Text.Json;

namespace AWM.Services.Interfaces;

public interface IClaudeCli
{
    /// <summary>
    /// claude.exe 를 -p 로 한 번 실행하고 structured_output 을 돌려준다.
    /// 실패는 <see cref="ClaudeCliException"/>, 사용자 취소는 <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<JsonElement> RunAsync(string prompt, string systemPrompt, string jsonSchema, CancellationToken ct);
}
