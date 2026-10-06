using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TextBean.Models;
using TextBean.Services.Interfaces;

namespace TextBean.Services;

/// <summary>
/// 후보 키의 판정 결과. 확인 창에 "이 키가 맞는 문서 3개 / 전체 10개" 를 보인 뒤
/// 사용자가 고르면 <see cref="IVaultKeyService.Activate"/>, 취소하면 <see cref="IVaultKeyService.Discard"/>.
/// "맞는 문서"는 키 확인값이 맞은 문서다. 본문만 손상된 문서도 들어갈 수 있다 [실측] — 문구를 "열리는"으로 쓰지 않는다.
/// </summary>
public sealed class KeyEvaluation
{
    internal KeyEvaluation(byte[] keyBytes, bool qualifiesAsNewKey,
                           ConcurrentDictionary<DocumentKeyId, DerivedKey> matched,
                           ConcurrentDictionary<DocumentKeyId, byte> mismatched,
                           DocumentKeyId? preferred,
                           int matchCount, int otherKeyCount, int legacyCount, int corruptedCount, int unreadableCount)
    {
        KeyBytes = keyBytes;
        QualifiesAsNewKey = qualifiesAsNewKey;
        Matched = matched;
        Mismatched = mismatched;
        Preferred = preferred;
        MatchCount = matchCount;
        OtherKeyCount = otherKeyCount;
        LegacyCount = legacyCount;
        CorruptedCount = corruptedCount;
        UnreadableCount = unreadableCount;
    }

    public int MatchCount { get; }
    public int OtherKeyCount { get; }
    public int LegacyCount { get; }
    public int CorruptedCount { get; }
    public int UnreadableCount { get; }
    public int Total => MatchCount + OtherKeyCount + LegacyCount + CorruptedCount + UnreadableCount;

    /// 이 키로 새 salt 를 만들 수 있는가 (새 키 규칙 D-049 · D-051).
    public bool QualifiesAsNewKey { get; }

    internal byte[] KeyBytes { get; }
    internal ConcurrentDictionary<DocumentKeyId, DerivedKey> Matched { get; }
    internal ConcurrentDictionary<DocumentKeyId, byte> Mismatched { get; }
    internal DocumentKeyId? Preferred { get; }
    internal bool Consumed { get; set; }

    internal void Wipe()
    {
        CryptographicOperations.ZeroMemory(KeyBytes);
        foreach (var key in Matched.Values) key.Clear();
    }
}

/// 한 세대. 불변 — 키 바이트는 바뀌지 않고, 캐시에는 이 세대의 키로 만든 것만 들어간다.
internal sealed class KeyGeneration(long number, KeyEvaluation evaluation)
{
    private readonly object _gate = new();
    private int _leases;
    private bool _retired;
    private bool _wiped;

    public long Number { get; } = number;
    public byte[] KeyBytes { get; } = evaluation.KeyBytes;
    public bool QualifiesAsNewKey { get; } = evaluation.QualifiesAsNewKey;
    public ConcurrentDictionary<DocumentKeyId, DerivedKey> Matched { get; } = evaluation.Matched;
    public ConcurrentDictionary<DocumentKeyId, byte> Mismatched { get; } = evaluation.Mismatched;

    public readonly object NewKeyGate = new();
    public DocumentKeyId? NewDocumentKey { get; set; } = evaluation.Preferred;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            if (_retired) return false;
            _leases++;
            return true;
        }
    }

    public void Release()
    {
        bool wipe;
        lock (_gate)
        {
            _leases--;
            wipe = _retired && _leases == 0 && !_wiped;
            if (wipe) _wiped = true;
        }
        if (wipe) Wipe();
    }

    public void Retire()
    {
        bool wipe;
        lock (_gate)
        {
            _retired = true;
            wipe = _leases == 0 && !_wiped;
            if (wipe) _wiped = true;
        }
        if (wipe) Wipe();
    }

    private void Wipe()
    {
        CryptographicOperations.ZeroMemory(KeyBytes);
        foreach (var key in Matched.Values) key.Clear();
    }
}

/// <summary>
/// 빌린 세대. 반드시 using 으로 돌려준다 — 안 돌려주면 옛 세대의 키가 지워지지 않는다.
/// </summary>
public sealed class KeyLease : IDisposable
{
    private readonly KeyGeneration _generation;
    private readonly IKeyDerivation _derivation;
    private int _released;

    internal KeyLease(KeyGeneration generation, IKeyDerivation derivation)
    {
        _generation = generation;
        _derivation = derivation;
    }

    public long Generation => _generation.Number;

    /// 이 세대에 키 확인값이 맞은 키가 하나라도 있는가. 없으면 이 키로 만드는 첫 문서다 (오타일 수 있다).
    public bool HasMatchedKey => !_generation.Matched.IsEmpty;

    internal byte[] KeyBytes => _generation.KeyBytes;

    internal bool QualifiesAsNewKey => _generation.QualifiesAsNewKey;

    /// <summary>
    /// 헤더의 키에 맞는 이 세대의 키. 처음 보는 salt 면 이 세대의 키 바이트로 그 자리에서 만든다
    /// (키를 넣은 뒤 USB 로 들어온 문서 — D-056). 확인값이 다르면 null.
    /// </summary>
    public DerivedKey? Find(DocumentKeyId id, ReadOnlySpan<byte> check)
    {
        if (_generation.Matched.TryGetValue(id, out var known))
            return CryptographicOperations.FixedTimeEquals(known.Check, check) ? known : null;

        if (_generation.Mismatched.ContainsKey(id)) return null;

        var derived = _derivation.Derive(_generation.KeyBytes, id);
        if (!CryptographicOperations.FixedTimeEquals(derived.Check, check))
        {
            derived.Clear();
            _generation.Mismatched.TryAdd(id, 0);
            return null;
        }

        if (_generation.Matched.TryAdd(id, derived)) return derived;

        // 다른 스레드가 먼저 넣었다
        derived.Clear();
        return _generation.Matched.TryGetValue(id, out var raced)
               && CryptographicOperations.FixedTimeEquals(raced.Check, check) ? raced : null;
    }

    /// 이미 맞은 키만 찾는다 — 새로 만들지 않는다.
    public DerivedKey? FindMatched(DocumentKeyId id) => _generation.Matched.GetValueOrDefault(id);

    /// <summary>
    /// 새 문서에 쓸 키. 맞은 salt 중 가장 많이 쓰인 것을 쓴다. 없으면 처음 한 번 새로 만들어 이 세대에 고정한다 —
    /// 고정하지 않으면 문서마다 salt 가 생겨 키 입력·검색이 문서 수만큼 느려진다 [실측 1,000개 약 74초].
    /// </summary>
    public (DocumentKeyId Id, DerivedKey Key) NewDocumentKey()
    {
        lock (_generation.NewKeyGate)
        {
            if (_generation.NewDocumentKey is { } pinned && _generation.Matched.TryGetValue(pinned, out var existing))
                return (pinned, existing);

            // 새 salt 를 만드는 것은 새 키를 정하는 것이다. 규칙을 여기서도 본다 — 화면 한 곳에만 걸지 않는다 (LL-078).
            if (!_generation.QualifiesAsNewKey) throw new KeyUnavailableException(KeyUnavailableReason.WeakKeyForNewDocument);

            var id = new DocumentKeyId(Pbkdf2KeyDerivation.KdfPbkdf2Sha256, KeyDocumentCodec.DefaultIterations,
                                       Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyDocumentCodec.SaltLength)));
            var derived = _derivation.Derive(_generation.KeyBytes, id);
            _generation.Matched[id] = derived;
            _generation.NewDocumentKey = id;
            return (id, derived);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) _generation.Release();
    }
}

public sealed class VaultKeyService(IKeyDerivation derivation) : IVaultKeyService, IDisposable
{
    private readonly object _gate = new();
    private KeyGeneration? _current;
    private long _generation;

    public long Generation
    {
        get { lock (_gate) return _generation; }
    }

    public bool HasKey
    {
        get { lock (_gate) return _current is not null; }
    }

    public event EventHandler? Changed;

    public KeyEvaluation Evaluate(string key, IReadOnlyList<DocumentHeader?> headers, CancellationToken ct = default)
    {
        var normalized = KeyRules.Normalize(key);
        var entered = KeyRules.CheckEntered(normalized);
        if (entered != KeyRuleResult.Ok) throw new ArgumentException(KeyRules.MessageFor(entered));

        // 고정 배열: GC 가 옮기며 사본을 남기지 않게 한다. 세대가 끝나면 0 으로 지운다.
        var keyBytes = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(normalized), pinned: true);
        Encoding.UTF8.GetBytes(normalized, keyBytes);

        return EvaluateBytes(keyBytes, KeyRules.CheckNew(normalized) == KeyRuleResult.Ok, headers, ct);
    }

    public KeyEvaluation? EvaluateCurrent(IReadOnlyList<DocumentHeader?> headers, CancellationToken ct = default)
    {
        using var lease = Acquire();
        if (lease is null) return null;

        // 지금 세대의 키 바이트를 복사한다 — 새 세대는 제 사본을 가지고, 옛 세대가 지워져도 영향이 없다
        var copy = GC.AllocateArray<byte>(lease.KeyBytes.Length, pinned: true);
        lease.KeyBytes.CopyTo(copy);
        return EvaluateBytes(copy, lease.QualifiesAsNewKey, headers, ct);
    }

    public bool Matches(string key)
    {
        using var lease = Acquire();
        if (lease is null) return false;

        var candidate = Encoding.UTF8.GetBytes(KeyRules.Normalize(key));
        try
        {
            return CryptographicOperations.FixedTimeEquals(candidate, lease.KeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private KeyEvaluation EvaluateBytes(byte[] keyBytes, bool qualifiesAsNewKey, IReadOnlyList<DocumentHeader?> headers,
                                        CancellationToken ct)
    {
        var ids = headers.Where(h => h?.Kind == DocumentHeaderKind.Valid).Select(h => h!.Key).Distinct().ToList();
        var derived = new ConcurrentDictionary<DocumentKeyId, DerivedKey>();
        try
        {
            // salt 종류가 8개 이하면 병렬로 한 번 만드는 시간과 비슷하다 [실측 79~121ms]
            Parallel.ForEach(ids, new ParallelOptions { CancellationToken = ct },
                             id => derived[id] = derivation.Derive(keyBytes, id));
        }
        catch
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            foreach (var d in derived.Values) d.Clear();
            throw;
        }

        int matched = 0, other = 0, legacy = 0, corrupted = 0, unreadable = 0;
        var matchesById = new Dictionary<DocumentKeyId, int>();
        foreach (var header in headers)
        {
            switch (header?.Kind)
            {
                case null:
                    unreadable++;
                    break;
                case DocumentHeaderKind.Legacy:
                    legacy++;
                    break;
                case DocumentHeaderKind.Valid when derived.TryGetValue(header.Key, out var d)
                                                   && CryptographicOperations.FixedTimeEquals(d.Check, header.Check):
                    matched++;
                    matchesById[header.Key] = matchesById.GetValueOrDefault(header.Key) + 1;
                    break;
                case DocumentHeaderKind.Valid:
                    other++;
                    break;
                default:
                    corrupted++;
                    break;
            }
        }

        var matchedKeys = new ConcurrentDictionary<DocumentKeyId, DerivedKey>();
        var mismatched = new ConcurrentDictionary<DocumentKeyId, byte>();
        foreach (var (id, made) in derived)
        {
            if (matchesById.ContainsKey(id)) matchedKeys[id] = made;
            else
            {
                made.Clear();
                mismatched[id] = 0;
            }
        }

        DocumentKeyId? preferred = matchesById.Count == 0 ? null : matchesById.MaxBy(p => p.Value).Key;

        return new KeyEvaluation(keyBytes, qualifiesAsNewKey, matchedKeys, mismatched,
                                 preferred, matched, other, legacy, corrupted, unreadable);
    }

    public void Activate(KeyEvaluation evaluation)
    {
        KeyGeneration? old;
        lock (_gate)
        {
            if (evaluation.Consumed) throw new InvalidOperationException("이미 쓰거나 버린 판정입니다.");
            evaluation.Consumed = true;

            old = _current;
            _current = new KeyGeneration(++_generation, evaluation);
        }

        old?.Retire();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Discard(KeyEvaluation evaluation)
    {
        lock (_gate)
        {
            if (evaluation.Consumed) return;
            evaluation.Consumed = true;
        }

        evaluation.Wipe();
    }

    public void Clear()
    {
        KeyGeneration? old;
        lock (_gate)
        {
            old = _current;
            _current = null;
            _generation++;
        }

        old?.Retire();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public KeyLease? Acquire(long? expectedGeneration = null)
    {
        while (true)
        {
            KeyGeneration? generation;
            lock (_gate) generation = _current;

            if (generation is null) return null;
            if (expectedGeneration is { } expected && generation.Number != expected) return null;

            // 빌리려는 사이 은퇴했으면 다시 읽는다 — Activate 는 새 세대를 먼저 세우고 옛 세대를 은퇴시킨다
            if (generation.TryAcquire()) return new KeyLease(generation, derivation);
        }
    }

    public void Dispose() => Clear();
}
