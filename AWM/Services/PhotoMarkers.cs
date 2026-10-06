using System.Text.RegularExpressions;
using AWM.Models;

namespace AWM.Services;

/// <summary>
/// 본문의 사진 줄 규칙. 한 줄 전체가 「[사진]」 · 「[사진: 설명]」 · 「[사진: 설명 | 파일]」 이면 사진 줄이다.
/// 파일 이름을 줄 안에 적어, 사용자가 줄을 옮기면 사진도 따라간다 (D-004).
/// DraftService 지침의 사진 줄 형식과 같아야 한다.
/// </summary>
public static partial class PhotoMarkers
{
    [GeneratedRegex(@"^\[사진(?:\s*:\s*(?<desc>[^|\]]*?))?\s*(?:\|\s*(?<file>[^\]|]*?)\s*)?\]$")]
    private static partial Regex MarkerLine();

    public static PhotoSlot? Parse(string line, int lineIndex = 0)
    {
        var match = MarkerLine().Match(line.Trim());
        if (!match.Success)
            return null;

        var file = match.Groups["file"].Success ? match.Groups["file"].Value.Trim() : "";
        return new PhotoSlot(lineIndex, match.Groups["desc"].Value.Trim(), file.Length > 0 ? file : null);
    }

    public static bool IsMarker(string line) => Parse(line) is not null;

    public static string Format(string description, string? file)
    {
        var desc = description.Trim();
        if (file is null)
            return desc.Length == 0 ? "[사진]" : $"[사진: {desc}]";
        return $"[사진: {desc} | {file}]";
    }

    public static IReadOnlyList<PhotoSlot> Slots(string body)
    {
        var lines = Lines(body);
        var slots = new List<PhotoSlot>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (Parse(lines[i], i) is { } slot)
                slots.Add(slot);
        }
        return slots;
    }

    /// <summary>
    /// 본문에 배치된 사진 파일(처음 나온 순서, 중복 없이).
    /// </summary>
    public static IReadOnlyList<string> PlacedFiles(string body) =>
        Slots(body).Where(slot => slot.File is not null).Select(slot => slot.File!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static string StripMarkers(string body) =>
        Join(Lines(body).Where(line => !IsMarker(line)));

    /// <summary>
    /// 그 사진을 가리키는 줄을 모두 지운다 — 파일은 지우지 않는다(D-005).
    /// </summary>
    public static string RemoveFile(string body, string file) =>
        Join(Lines(body).Where(line => !string.Equals(Parse(line)?.File, file, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// 「사진 배치」 요청용 문단. 빈 줄·사진 줄로 나뉜 글 덩어리에 1부터 번호를 붙인다.
    /// </summary>
    public static IReadOnlyList<string> NumberedBlocks(string body) =>
        Blocks(Lines(body)).Select(block => block.Text).ToList();

    /// <summary>
    /// 사진 줄을 지정한 문단 뒤에 끼운다. 본문 글자는 바꾸지 않는다 (D-015). 번호가 범위 밖인 배치는 버린다.
    /// </summary>
    public static string InsertAfterBlocks(string body, IEnumerable<PhotoPlacement> placements)
    {
        var lines = Lines(body);
        var blocks = Blocks(lines);
        var inserts = placements
            .Where(p => p.AfterBlock >= 0 && p.AfterBlock <= blocks.Count)
            .Select((placement, order) => (At: placement.AfterBlock == 0 ? 0 : blocks[placement.AfterBlock - 1].End + 1, order, placement))
            // 뒤쪽부터 끼워야 앞쪽 위치가 밀리지 않는다. 같은 자리는 받은 순서대로
            .OrderByDescending(item => item.At).ThenByDescending(item => item.order)
            .ToList();

        foreach (var (at, _, placement) in inserts)
        {
            var piece = new List<string>();
            if (at > 0 && lines[at - 1].Trim().Length > 0)
                piece.Add("");
            piece.Add(Format(placement.Description, placement.File));
            if (at < lines.Count && lines[at].Trim().Length > 0)
                piece.Add("");
            lines.InsertRange(at, piece);
        }
        return Join(lines);
    }

    /// <summary>
    /// 네이버에 차례로 붙일 조각. 글은 NaverFormat 으로, 연달아 있는 사진은 한 조각으로.
    /// 파일을 찾지 못한 사진 줄(파일 없음·지정 안 됨)은 건너뛰고 개수를 돌려준다. 태그 줄은 마지막 글 조각 끝에.
    /// </summary>
    public static IReadOnlyList<PasteSegment> Segments(string body, IReadOnlyList<string> tags,
        Func<string, string?> resolveFile, out int skipped)
    {
        var segments = new List<PasteSegment>();
        var text = new List<string>();
        var images = new List<string>();
        skipped = 0;

        foreach (var line in Lines(body))
        {
            var slot = Parse(line);
            if (slot is null)
            {
                if (line.Trim().Length > 0)
                    FlushImages();
                text.Add(line);
                continue;
            }

            var path = slot.File is null ? null : resolveFile(slot.File);
            if (path is null)
            {
                skipped++;
                continue;
            }
            FlushText(withTags: false);
            images.Add(path);
        }

        FlushImages();
        FlushText(withTags: true);
        return segments;

        void FlushText(bool withTags)
        {
            var chunk = Join(text);
            text.Clear();
            if (chunk.Trim().Length == 0 && !(withTags && tags.Count > 0))
                return;
            segments.Add(new TextSegment(NaverFormat.Format(chunk, withTags ? tags : null)));
        }

        void FlushImages()
        {
            if (images.Count == 0)
                return;
            segments.Add(new ImageSegment(images.ToList()));
            images.Clear();
        }
    }

    private sealed record Block(int Start, int End, string Text);

    private static List<Block> Blocks(List<string> lines)
    {
        var blocks = new List<Block>();
        var start = -1;
        for (var i = 0; i <= lines.Count; i++)
        {
            var isText = i < lines.Count && lines[i].Trim().Length > 0 && !IsMarker(lines[i]);
            if (isText && start < 0)
                start = i;
            else if (!isText && start >= 0)
            {
                blocks.Add(new Block(start, i - 1, string.Join("\n", lines[start..i])));
                start = -1;
            }
        }
        return blocks;
    }

    private static List<string> Lines(string body) => body.ReplaceLineEndings("\n").Split('\n').ToList();

    private static string Join(IEnumerable<string> lines) => string.Join(Environment.NewLine, lines);
}
