using System.Text;
using System.Text.Json;
using AWM.Models;
using AWM.Services.Interfaces;

namespace AWM.Services;

public sealed class DraftService(IClaudeCli cli) : IDraftService
{
    // 앱이 네이버용으로 바꿀 수 있는 표시만 쓰게 한다 — 네이버가 살리는 서식이 이것뿐이다 [실측]
    // (add-naver-blog-format D-003, build-blog-autopost D-007 대체). 규칙은 NaverFormat 과 같아야 한다
    private const string SystemPrompt = """
        너는 한국어 개인 블로그 글을 쓰는 작가다. 요청한 주제로 블로그 글 한 편을 쓴다.
        본문 서식은 아래 표시만 쓴다. 이 밖의 마크다운(#, ###, **, *, `, 표, 링크)과 이모지는 쓰지 않는다.
        - 소제목: 줄 맨 앞에 "## " 를 붙여 한 줄로 쓴다. 소제목은 2~4개.
        - 인용: 줄 맨 앞에 "> " (필요할 때만).
        - 목록: 줄 맨 앞에 "- " 또는 "1. " (필요할 때만).
        - 구분선: "---" 한 줄 (필요할 때만).
        - 문단 사이는 빈 줄 하나로 나눈다.
        - 본문 분량은 공백 포함 1,500~2,500자로 한다.
        - 제목은 본문 첫 줄에 다시 쓰지 않는다.
        - 제목은 한 줄로 쓴다.
        - 태그는 5~10개, # 없이 띄어쓰기 없는 단어로 쓴다.
        - 확인되지 않은 가격·운영 시간·수치는 단정하지 말고 방문이나 이용 전에 확인하라고 쓴다.
        """;

    private const string Schema = """
        {"type":"object","properties":{"title":{"type":"string"},"body":{"type":"string"},"tags":{"type":"array","items":{"type":"string"}}},"required":["title","body","tags"],"additionalProperties":false}
        """;

    public async Task<BlogDraft> CreateAsync(DraftRequest request, CancellationToken ct)
    {
        var output = await cli.RunAsync(BuildPrompt(request), SystemPrompt, Schema, ct).ConfigureAwait(false);

        var title = ReadString(output, "title").Trim();
        // 앱 편집란의 Enter 는 \r\n 이라, 받은 \n 도 맞춰 둔다
        var body = ReadString(output, "body").Trim().ReplaceLineEndings();
        if (title.Length == 0 || body.Length == 0)
            throw new ClaudeCliException(ClaudeCliFailure.BadOutput, "Claude 응답의 제목이나 본문이 비어 있습니다.");

        return new BlogDraft(title, body, ReadTags(output));
    }

    private static string BuildPrompt(DraftRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"주제: {request.Topic.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Keywords))
            prompt.AppendLine($"키워드: {request.Keywords.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Extra))
            prompt.AppendLine($"추가 요청: {request.Extra.Trim()}");
        return prompt.ToString();
    }

    private static string ReadString(JsonElement output, string name) =>
        output.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static List<string> ReadTags(JsonElement output)
    {
        var tags = new List<string>();
        if (!output.TryGetProperty("tags", out var array) || array.ValueKind != JsonValueKind.Array)
            return tags;

        foreach (var item in array.EnumerateArray())
        {
            var tag = item.ValueKind == JsonValueKind.String ? (item.GetString() ?? "").Trim().TrimStart('#') : "";
            if (tag.Length > 0)
                tags.Add(tag);
        }
        return tags;
    }
}
