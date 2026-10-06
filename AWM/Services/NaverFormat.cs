using System.Text;
using System.Text.RegularExpressions;
using AWM.Models;

namespace AWM.Services;

/// <summary>
/// 본문 표시 규칙 → 네이버용 HTML · 일반 텍스트.
/// 규칙은 네이버 스마트에디터가 붙여넣기에서 살린 것만 둔다 [실측 — add-naver-blog-format research 관찰 2]:
/// "## " 소제목(h2) · "> " 인용 · "- " / "1. " 목록 · "---" 구분선. 문장 안 굵게는 에디터가 버려서 규칙에 없다.
/// 규칙에 없는 줄은 문단 글자 그대로 둔다 — 예외를 내지 않는다.
/// </summary>
public static partial class NaverFormat
{
    private enum Kind
    {
        Paragraph,
        Heading,
        Quote,
        Bullets,
        Numbers,
        Rule,
    }

    private sealed record Block(Kind Kind, List<string> Lines);

    /// <param name="tags">
    /// 본문 맨 끝에 「#태그 #태그」 한 줄로 붙인다. 네이버는 본문의 #단어를 태그로 자동 등록한다 [실측 — research 관찰 3] (D-010).
    /// </param>
    public static FormattedBody Format(string body, IReadOnlyList<string>? tags = null)
    {
        var blocks = Parse(body);
        if (tags is { Count: > 0 })
            blocks.Add(new Block(Kind.Paragraph, [string.Join(" ", tags.Select(tag => "#" + tag))]));
        return new FormattedBody(ToHtml(blocks), ToPlainText(blocks));
    }

    private static List<Block> Parse(string body)
    {
        var blocks = new List<Block>();
        Block? open = null;

        foreach (var raw in body.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            // 사진 줄은 글에 넣지 않는다 — 그림은 클립보드 글에 함께 붙일 수 없다 [실측] (add-draft-images D-012)
            if (line.Length == 0 || PhotoMarkers.IsMarker(line))
            {
                open = null;
                continue;
            }

            if (line == "---")
            {
                blocks.Add(new Block(Kind.Rule, []));
                open = null;
            }
            else if (line.StartsWith("## ") && line.Length > 3)
            {
                blocks.Add(new Block(Kind.Heading, [line[3..].Trim()]));
                open = null;
            }
            else if (line.StartsWith("> "))
                Append(Kind.Quote, line[2..].Trim());
            else if (line.StartsWith("- ") && line.Length > 2)
                Append(Kind.Bullets, line[2..].Trim());
            else if (NumberedItem().Match(line) is { Success: true } numbered)
                Append(Kind.Numbers, numbered.Groups[1].Value.Trim());
            else
                Append(Kind.Paragraph, line);
        }
        return blocks;

        void Append(Kind kind, string text)
        {
            if (open?.Kind == kind)
            {
                open.Lines.Add(text);
                return;
            }
            open = new Block(kind, [text]);
            blocks.Add(open);
        }
    }

    private static string ToHtml(List<Block> blocks)
    {
        var html = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            // <p> 끼리는 붙어 버리고 빈 문단은 빈 줄이 된다 [실측]. 인용·구분선은 에디터가 스스로 여백을 둔다 (D-005)
            if (i > 0 && !HasOwnSpacing(blocks[i - 1]) && !HasOwnSpacing(block))
                html.Append("<p><br></p>");

            switch (block.Kind)
            {
                case Kind.Heading:
                    html.Append("<h2>").Append(Escape(block.Lines[0])).Append("</h2>");
                    break;
                case Kind.Paragraph:
                    html.Append("<p>").Append(string.Join("<br>", block.Lines.Select(Escape))).Append("</p>");
                    break;
                case Kind.Quote:
                    html.Append("<blockquote>").Append(string.Join("<br>", block.Lines.Select(Escape))).Append("</blockquote>");
                    break;
                case Kind.Bullets:
                    AppendList(html, "ul", block.Lines);
                    break;
                case Kind.Numbers:
                    AppendList(html, "ol", block.Lines);
                    break;
                case Kind.Rule:
                    html.Append("<hr>");
                    break;
            }
        }
        return html.ToString();
    }

    private static bool HasOwnSpacing(Block block) => block.Kind is Kind.Quote or Kind.Rule;

    private static void AppendList(StringBuilder html, string tag, List<string> items)
    {
        html.Append('<').Append(tag).Append('>');
        foreach (var item in items)
            html.Append("<li>").Append(Escape(item)).Append("</li>");
        html.Append("</").Append(tag).Append('>');
    }

    // 텍스트로 붙일 곳에는 표시 기호를 남기지 않는다. 목록 기호는 텍스트에서도 뜻이 있어 남긴다 (D-008)
    private static string ToPlainText(List<Block> blocks)
    {
        var parts = new List<string>();
        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case Kind.Rule:
                    continue;
                case Kind.Bullets:
                    parts.Add(string.Join("\r\n", block.Lines.Select(item => "- " + item)));
                    break;
                case Kind.Numbers:
                    parts.Add(string.Join("\r\n", block.Lines.Select((item, index) => $"{index + 1}. {item}")));
                    break;
                default:
                    parts.Add(string.Join("\r\n", block.Lines));
                    break;
            }
        }
        return string.Join("\r\n\r\n", parts);
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex NumberedItem();
}
