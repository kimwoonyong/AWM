using System.Buffers.Binary;
using System.Security.Cryptography;
using TextBean.Models;
using TextBean.Services;
using TextBean.Services.Interfaces;

namespace TextBean.Tests;

/// <summary>
/// 테스트용 키 조립. 모든 테스트가 같은 키 문자열을 쓰므로, 서로 다른 코덱 인스턴스가 만든 문서도
/// 처음 보는 salt 를 그 자리에서 만들어 열 수 있다 (D-056) — 다른 PC 에서 파일 하나를 여는 것과 같은 경로다.
/// </summary>
internal static class TestKeys
{
    public const string DefaultKey = "test-key-1234";

    /// <summary>
    /// 키가 들어간 서비스. <paramref name="inUse"/> 면 새 문서용 salt 를 미리 고정해 "이미 쓰던 키" 상태로 둔다 —
    /// 안 그러면 셸이 첫 문서를 만들 때마다 오타 방지 재입력을 묻는다. 그 흐름 자체를 볼 때만 false 로 준다.
    /// </summary>
    public static VaultKeyService Service(string key = DefaultKey, IKeyDerivation? derivation = null, bool inUse = true)
    {
        var service = new VaultKeyService(derivation ?? new FastKeyDerivation());
        service.Activate(service.Evaluate(key, []));

        if (inUse)
        {
            using var lease = service.Acquire()!;
            try { lease.NewDocumentKey(); }
            catch (KeyUnavailableException) { }      // 새 키 규칙을 못 넘는 키 — 새 문서를 만들 수 없는 상태 그대로 둔다
        }

        return service;
    }

    public static KeyDocumentCodec Codec(string key = DefaultKey) => new(Service(key));

    public static DocumentStore Store(string root, string key = DefaultKey) => new(new TreeService(root), Codec(key));
}

/// 옛 방식(DPAPI, 버전 1) 모양의 파일. 앱은 헤더만 보고 가른다 — 진짜 DPAPI 로 만들 필요가 없고, 만들지 않는다 (D-073).
internal static class LegacyShape
{
    public static byte[] Bytes() => [.. "TBX1"u8, 1, .. RandomNumberGenerator.GetBytes(120)];
}

/// <summary>
/// 반복 횟수를 무시하는 빠른 키 만들기. 헤더에는 운영값(60만)이 그대로 적혀 범위 검사를 약하게 하지 않는다.
/// 암호화 키와 확인값을 나누는 규칙은 운영 코드의 것을 그대로 쓴다 — 규칙이 둘로 갈리면 테스트가 다른 형식을 시험한다.
/// </summary>
internal sealed class FastKeyDerivation : IKeyDerivation
{
    private int _calls;

    /// 키 만들기가 몇 번 돌았나. salt 가 키마다 하나인지 확인할 때 본다.
    public int Calls => _calls;

    public DerivedKey Derive(ReadOnlySpan<byte> keyBytes, DocumentKeyId id)
    {
        Interlocked.Increment(ref _calls);

        var salt = Convert.FromHexString(id.SaltHex);
        var data = new byte[1 + 4 + salt.Length];
        data[0] = id.KdfId;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1, 4), id.Iterations);
        salt.CopyTo(data, 5);

        var master = HMACSHA256.HashData(keyBytes, data);
        try
        {
            return Pbkdf2KeyDerivation.FromMaster(master);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }
}

/// <summary>
/// 키 만들기를 멈춰 세운다. "판정이 도는 동안"(IsKeyBusy)을 테스트가 결정적으로 만든다 (LL-079).
/// 키 만들기는 스레드 풀에서 돈다(Classify · Evaluate 모두 Task.Run) — 여기서 막아도 테스트 스레드는 멈추지 않는다.
/// </summary>
internal sealed class GatedKeyDerivation : IKeyDerivation
{
    private readonly FastKeyDerivation _inner = new();
    private readonly ManualResetEventSlim _open = new(initialState: true);
    private TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Close()
    {
        _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _open.Reset();
    }

    public void Open() => _open.Set();

    /// 닫힌 뒤 키 만들기가 실제로 게이트에 걸렸다.
    public Task Entered => _entered.Task;

    public DerivedKey Derive(ReadOnlySpan<byte> keyBytes, DocumentKeyId id)
    {
        if (!_open.IsSet)
        {
            _entered.TrySetResult();
            _open.Wait(TimeSpan.FromSeconds(10));   // 회귀로 테스트 스레드에서 불려도 영원히 멈추지는 않는다
        }
        return _inner.Derive(keyBytes, id);
    }
}
