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

    // 「사진 배치」는 글을 고치지 않는다 — 사진 줄 위치만 받아 앱이 끼운다 (D-015)
    private const string PlaceSystemPrompt = """
        너는 블로그 편집자다. 함께 보낸 사진을 하나씩 보고, 번호 붙은 문단 중 그 사진이 가장 어울리는 문단 바로 뒤에 둘 곳을 정한다.
        - 글은 고치지 않는다. 위치와 사진 설명만 정한다.
        - 모든 사진을 한 번씩 배치한다. 파일 이름은 받은 그대로 쓴다.
        - after_block 은 그 문단 번호. 맨 앞에 둘 때만 0.
        - description 은 사진에 보이는 내용 한 줄(사진에 없는 것은 쓰지 않는다).
        """;

    private const string PlaceSchema = """
        {"type":"object","properties":{"placements":{"type":"array","items":{"type":"object","properties":{"file":{"type":"string"},"description":{"type":"string"},"after_block":{"type":"integer"}},"required":["file","description","after_block"],"additionalProperties":false}}},"required":["placements"],"additionalProperties":false}
        """;

    public async Task<BlogDraft> CreateAsync(DraftRequest request, IReadOnlyList<ImageInput> photos, CancellationToken ct)
    {
        var output = await cli.RunAsync(BuildPrompt(request, photos), photos, SystemPrompt, Schema, ct).ConfigureAwait(false);

        var title = ReadString(output, "title").Trim();
        // 앱 편집란의 Enter 는 \r\n 이라, 받은 \n 도 맞춰 둔다
        var body = ReadString(output, "body").Trim().ReplaceLineEndings();
        if (title.Length == 0 || body.Length == 0)
            throw new ClaudeCliException(ClaudeCliFailure.BadOutput, "Claude 응답의 제목이나 본문이 비어 있습니다.");

        return new BlogDraft(title, body, ReadTags(output));
    }

    public async Task<IReadOnlyList<PhotoPlacement>> PlacePhotosAsync(string body, IReadOnlyList<ImageInput> photos,
        CancellationToken ct)
    {
        var blocks = PhotoMarkers.NumberedBlocks(body);
        var prompt = new StringBuilder();
        prompt.AppendLine($"사진 {photos.Count}장: {string.Join(", ", photos.Select(photo => photo.FileName))}");
        prompt.AppendLine("번호 붙은 문단:");
        for (var i = 0; i < blocks.Count; i++)
            prompt.AppendLine($"[{i + 1}] {blocks[i]}");

        var output = await cli.RunAsync(prompt.ToString(), photos, PlaceSystemPrompt, PlaceSchema, ct).ConfigureAwait(false);

        // 보낸 적 없는 파일·범위 밖 번호는 버린다 — 그 사진은 「배치 안 됨」으로 남는다
        var sent = photos.Select(photo => photo.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var placements = new List<PhotoPlacement>();
        if (output.TryGetProperty("placements", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var file = ReadString(item, "file").Trim();
                var after = item.TryGetProperty("after_block", out var number) && number.TryGetInt32(out var value) ? value : -1;
                if (sent.Contains(file) && after >= 0 && after <= blocks.Count
                    && placements.All(placed => !string.Equals(placed.File, file, StringComparison.OrdinalIgnoreCase)))
                {
                    placements.Add(new PhotoPlacement(file, ReadString(item, "description").Trim(), after));
                }
            }
        }
        return placements;
    }

    private static string BuildPrompt(DraftRequest request, IReadOnlyList<ImageInput> photos)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"주제: {request.Topic.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Keywords))
            prompt.AppendLine($"키워드: {request.Keywords.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Extra))
            prompt.AppendLine($"추가 요청: {request.Extra.Trim()}");

        if (photos.Count == 0)
        {
            // 사진 없이 쓰면 빈 사진 자리를 만들지 않는다 (D-014)
            prompt.AppendLine("사진 줄([사진 …])은 쓰지 않는다.");
            return prompt.ToString();
        }

        // 사진 줄 형식은 PhotoMarkers 와 같아야 한다
        prompt.AppendLine($"사진 {photos.Count}장을 함께 보낸다(각 그림 앞에 파일 이름).");
        prompt.AppendLine("- 각 사진에 보이는 장면을 글 내용에 자연스럽게 반영한다. 사진에 없는 사실은 지어내지 않는다.");
        prompt.AppendLine("- 모든 사진을 한 번씩, 어울리는 곳에 다음 형식의 한 줄로 둔다(앞뒤는 빈 줄):");
        prompt.AppendLine("  [사진: 사진에 보이는 내용 한 줄 | 파일이름]");
        prompt.AppendLine($"- 파일 이름: {string.Join(", ", photos.Select(photo => photo.FileName))}");
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
