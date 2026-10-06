using System.IO;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// 금고 검색.
///
/// 열거는 <see cref="ITreeService.ScanAsync"/> 만 경유한다 — 편하다고
/// Directory.EnumerateFiles 를 쓰면 .trash 의 지운 비밀과 .history 의 편집 전 값이
/// 그대로 복호화돼 결과에 뜬다 [실측]. 예약 영역을 거르는 코드는 TreeService 안에만 있다.
///
/// 평문은 문서 하나씩 복호화 → 판정 → 놓는다. 전부 모아 두면 금고 전체 평문이
/// 동시에 살아 있게 된다 (2000문서×20KB 에서 10.8MB 대 77.4MB) [실측].
/// </summary>
public sealed class SearchService(ITreeService tree, IDocumentStore store) : ISearchService
{
    // 문화권 비교는 일치 길이가 검색어 길이와 달라질 수 있어(ß ↔ ss) 강조 사각형이 어긋난다.
    // 결과가 같으면서 더 빠르고 예측 가능한 쪽을 쓴다.
    private static StringComparison RuleFor(bool matchCase)
        => matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public async Task<SearchOutcome> SearchAsync(string query, string? scopeFolder,
                                                 IReadOnlyDictionary<string, string> openTabs,
                                                 CancellationToken ct, bool matchCase = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return SearchOutcome.Empty;

        var rule = RuleFor(matchCase);

        var scan = await CollectDocumentsAsync(scopeFolder, ct).ConfigureAwait(false);
        if (scan is null) return SearchOutcome.Empty with { Canceled = true };

        var (targets, blindFolders) = scan.Value;
        var hits = new List<SearchHit>();
        var unreadable = 0;
        var otherKey = 0;
        var legacy = 0;
        var scanned = 0;

        foreach (var doc in targets)
        {
            if (ct.IsCancellationRequested)
                return new SearchOutcome(hits, unreadable, scanned, Canceled: true, blindFolders, otherKey, legacy);

            scanned++;

            string? text;
            if (openTabs.TryGetValue(doc.FullPath, out var live))
            {
                // 탭이 들고 있으면 디스크를 읽지 않는다 — 디스크는 최대 1.5초 낡아 있다
                text = live;
            }
            else
            {
                DocumentReadResult result;
                try
                {
                    result = await store.LoadAsync(doc.FullPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 문서 하나가 검색 전체를 죽이면 안 된다. 경로도 내용도 남기지 않는다.
                    AppLog.Warn("search-read", null, ex);
                    unreadable++;
                    continue;
                }

                if (!result.IsOk)
                {
                    // 조용히 삼키면 "일치 없음"과 구분되지 않는다 — 사용자는 금고에 없다고 믿는다.
                    // 다른 키 문서는 손상과 따로 센다. 섞으면 정상인 문서를 "읽지 못함"으로 알린다.
                    switch (result.Status)
                    {
                        case DocumentReadStatus.DifferentKey or DocumentReadStatus.NoKey: otherKey++; break;
                        case DocumentReadStatus.LegacyDpapi: legacy++; break;
                        default: unreadable++; break;
                    }
                    continue;
                }

                text = result.Text!;
            }

            var count = CountMatches(text, query, rule);
            if (count > 0)
                hits.Add(new SearchHit(doc.FullPath, doc.Name, IsFolder: false, count, text.IndexOf(query, rule),
                                       doc.IsPlainText));
        }

        return new SearchOutcome(hits, unreadable, scanned, Canceled: false, blindFolders, otherKey, legacy);
    }

    public async Task<SearchOutcome> SearchDocumentAsync(string fullPath, string query, string? liveText,
                                                         CancellationToken ct, bool matchCase = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return SearchOutcome.Empty;

        var full = PathRules.NormalizeFull(fullPath);

        // 예약 영역은 어떤 경로로도 검색하지 않는다 — 지운 비밀과 바꾸기 전 값이 들어 있다
        if (PathRules.IsReservedArea(tree.Root, full)) return SearchOutcome.Empty;

        string text;
        if (liveText is not null)
        {
            text = liveText;
        }
        else
        {
            DocumentReadResult result;
            try
            {
                result = await store.LoadAsync(full).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn("search-read", null, ex);
                return SearchOutcome.Empty with { UnreadableCount = 1, ScannedCount = 1 };
            }

            // 조용히 삼키면 "뒤져봤고 없었다"는 거짓 답이 된다. 세는 규칙은 전체 범위와 같다.
            if (!result.IsOk) return SearchOutcome.NotRead(result.Status);

            text = result.Text!;
        }

        var rule = RuleFor(matchCase);
        var count = CountMatches(text, query, rule);

        return count == 0
            ? SearchOutcome.Empty with { ScannedCount = 1 }
            : new SearchOutcome(
                [BuildHit(full, count, text.IndexOf(query, rule))],
                UnreadableCount: 0, ScannedCount: 1, Canceled: false);
    }

    /// 평문만 확장자를 드러낸다 — 트리·탭과 같은 규칙이어야 결과 목록에서도 둘이 갈린다.
    private static SearchHit BuildHit(string full, int count, int firstIndex)
    {
        var plain = PathRules.IsPlainText(full);
        var name = plain ? Path.GetFileName(full) : Path.GetFileNameWithoutExtension(full);

        return new SearchHit(full, name, IsFolder: false, count, firstIndex, plain);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchNamesAsync(string query, string? scopeFolder,
                                                                 CancellationToken ct, bool matchCase = false)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var rule = RuleFor(matchCase);

        var root = await ScanAsync(ct).ConfigureAwait(false);
        if (root is null) return [];

        var hits = new List<SearchHit>();
        var rootPath = PathRules.NormalizeFull(root.FullPath);

        Walk(root, node =>
        {
            var full = PathRules.NormalizeFull(node.FullPath);

            // 금고 루트 자신은 결과가 될 수 없다 — 열 수도, 범위로 좁힐 수도 없는 항목이다
            if (string.Equals(full, rootPath, StringComparison.OrdinalIgnoreCase)) return;

            if (!InScope(full, scopeFolder)) return;
            if (!node.Name.Contains(query, rule)) return;

            // 이름 일치라 본문 개수는 세지 않는다 — 복호화하지 않았다.
            // 폴더 여부를 실어야 결과를 열 때 문서처럼 다루지 않는다.
            hits.Add(new SearchHit(full, node.Name, node.IsFolder, 0, -1, node.IsPlainText));
        });
        return hits;
    }

    /// <summary>검색 대상 문서 목록과, 목록조차 못 읽은 폴더 수. 취소되면 null.</summary>
    private async Task<(List<TreeNode> Docs, int BlindFolders)?> CollectDocumentsAsync(
        string? scopeFolder, CancellationToken ct)
    {
        var root = await ScanAsync(ct).ConfigureAwait(false);
        if (root is null) return null;

        var docs = new List<TreeNode>();
        var blind = 0;

        Walk(root, node =>
        {
            if (node.IsFolder)
            {
                // 목록을 못 읽은 폴더는 그 안에 문서가 몇 개인지도 알 수 없다.
                // 세지 않으면 "다 뒤졌고 없다"가 되지만 실제로는 통째로 안 본 영역이 있다.
                if (node.Unreadable && InScope(node.FullPath, scopeFolder)) blind++;
                return;
            }

            if (!InScope(node.FullPath, scopeFolder)) return;

            docs.Add(node with { FullPath = PathRules.NormalizeFull(node.FullPath) });
        });
        return (docs, blind);
    }

    private async Task<TreeNode?> ScanAsync(CancellationToken ct)
    {
        try
        {
            return await tree.ScanAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// 루트 노드 자신은 범위 판정에서 제외한다 — 폴더 범위가 루트면 전체와 같다.
    private static bool InScope(string fullPath, string? scopeFolder)
        => scopeFolder is null || PathRules.IsInsideRoot(scopeFolder, fullPath);

    private static void Walk(TreeNode node, Action<TreeNode> visit)
    {
        visit(node);
        foreach (var child in node.Children) Walk(child, visit);
    }

    /// <summary>
    /// 겹치지 않게 센다. 사용자가 다음/이전으로 돌 수 있는 자리 수와 같아야 한다 —
    /// 'aaaa' 에서 'aa' 는 3이 아니라 2다.
    /// </summary>
    private static int CountMatches(string text, string query, StringComparison rule)
    {
        var count = 0;
        var from = 0;

        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, rule);
            if (at < 0) break;

            count++;
            from = at + query.Length;
        }
        return count;
    }
}
