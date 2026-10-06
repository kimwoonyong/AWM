using System.Text.Json;
using AWM.Models;

namespace AWM.Services.Interfaces;

public interface IClaudeCli
{
    /// <summary>
    /// claude.exe 를 -p 로 한 번 실행하고 structured_output 을 돌려준다.
    /// 실패는 <see cref="ClaudeCliException"/>, 사용자 취소는 <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<JsonElement> RunAsync(string prompt, string systemPrompt, string jsonSchema, CancellationToken ct);

    /// <summary>
    /// 그림을 함께 보여 준다. 그림이 없으면 위와 같다.
    /// </summary>
    Task<JsonElement> RunAsync(string prompt, IReadOnlyList<ImageInput> images, string systemPrompt, string jsonSchema,
        CancellationToken ct);
}
