using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using TextBean.Views.Platform;

namespace TextBean.Tests;

/// <summary>
/// 시험 호스트 안에서는 앱의 조립 루트가 돌지 않는다 (D-079 · LL-087).
/// App 을 실제로 만들어 펌프해 보는 시험은 두지 않는다 — 방어가 깨지면 그 시험이 사고를 그대로 재현한다.
/// 대신 판정 함수와, 판정 호출의 위치 · "시험이 App 을 만들지 않음"을 IL 로 본다.
/// </summary>
public class StartupGuardTests
{
    [Fact]
    public void 시험_호스트는_조립하지_않고_TextBean_은_조립한다()
    {
        Assert.False(App.ShouldCompose(Assembly.GetEntryAssembly()));   // dotnet test 의 진입 어셈블리는 testhost [실측 — 계획 검토]
        Assert.False(App.ShouldCompose(null));
        Assert.False(App.ShouldCompose(typeof(StartupGuardTests).Assembly));
        Assert.True(App.ShouldCompose(typeof(App).Assembly));
    }

    /// <summary>
    /// 판정 함수만 시험하면 OnStartup 의 호출 줄을 지우거나 단일 실행 잠금 뒤로 옮겨도 통과한다.
    /// 위치만 보면 판정 인자를 바꾸거나 거절 뒤 return 을 지워도 통과했다 [실측 — 적대 검토] — 인자와 빠져나감도 본다.
    /// 조건을 뒤집는 것(if (ShouldCompose(…)))은 IL 모양이 컴파일러 설정마다 달라 보지 않는다 — 그러면 게시본이 켜자마자 꺼져 첫 실행에서 드러난다.
    /// </summary>
    [Fact]
    public void OnStartup_은_판정을_base_OnStartup_과_단일_실행_잠금보다_먼저_한다()
    {
        var code = Il.Instructions(StateMachineOf(typeof(App), "OnStartup")).ToList();
        var calls = code.Where(i => i.Target is not null).ToList();

        var guard = calls.FindIndex(i => i.Target!.Name == nameof(App.ShouldCompose) && i.Target.DeclaringType == typeof(App));
        var baseStartup = calls.FindIndex(i => IsBaseOnStartup(i.Target!));
        var mutex = calls.FindIndex(i => i.Target is ConstructorInfo && i.Target.DeclaringType == typeof(Mutex));

        Assert.True(guard >= 0, "OnStartup 이 ShouldCompose 를 부르지 않는다");
        Assert.True(baseStartup >= 0, "base.OnStartup 호출을 찾지 못했다 — 시험이 아무것도 보지 못한다");
        Assert.True(mutex >= 0, "단일 실행 잠금 생성을 찾지 못했다 — 시험이 아무것도 보지 못한다");
        Assert.True(baseStartup > guard, "base.OnStartup 이 판정보다 먼저다");
        Assert.True(mutex > guard, "단일 실행 잠금이 판정보다 먼저다 — 시험 호스트가 잠금을 쥔다");

        // 실행 중인 어셈블리(늘 TextBean)로 바꾸면 시험 호스트에서도 조립한다
        Assert.True(guard > 0 && calls[guard - 1].Target is MethodInfo { Name: nameof(Assembly.GetEntryAssembly) } entry
                    && entry.DeclaringType == typeof(Assembly), "판정 인자가 Assembly.GetEntryAssembly() 가 아니다");

        // 거절하면 Shutdown 하고 그 자리에서 끝난다 — return 이 빠지면 Shutdown 을 예약한 채 조립 루트가 그대로 돈다
        var shutdown = calls.FindIndex(guard, i => i.Target!.Name == nameof(System.Windows.Application.Shutdown)
                                                   && i.Target.DeclaringType == typeof(System.Windows.Application));
        Assert.True(shutdown > guard && shutdown < baseStartup, "판정과 base.OnStartup 사이에 Shutdown 이 없다");
        Assert.True(code.Any(i => i.Offset > calls[shutdown].Offset && i.Offset < calls[baseStartup].Offset
                                  && (i.Code == OpCodes.Leave || i.Code == OpCodes.Leave_S || i.Code == OpCodes.Ret)),
                    "Shutdown 뒤 base.OnStartup 전에 빠져나가지 않는다 — return 이 빠졌다");
    }

    /// async 메서드 안의 base 호출은 컴파일러가 만든 대리 메서드(&lt;&gt;n__N)를 거친다.
    private static bool IsBaseOnStartup(MethodBase m)
    {
        if (m.Name == "OnStartup" && m.DeclaringType == typeof(System.Windows.Application)) return true;

        return m.DeclaringType == typeof(App) && m.Name.StartsWith("<>n__", StringComparison.Ordinal)
               && Il.Calls(m).Any(inner => inner.Name == "OnStartup" && inner.DeclaringType == typeof(System.Windows.Application));
    }

    /// 메시지 루프를 돌리는 스레드에서 App 을 만들면 실제 조립 루트가 돈다 (LL-087). 시험은 빈 Application 만 쓴다(TestApplication).
    [Fact]
    public void 시험_어셈블리는_App_을_만들지_않는다()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(StartupGuardTests).Assembly.GetTypes())
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> methods = [.. type.GetMethods(all), .. type.GetConstructors(all)];

            foreach (var method in methods)
            {
                if (Il.Calls(method).Any(m => m is ConstructorInfo && typeof(App).IsAssignableFrom(m.DeclaringType)))
                    offenders.Add($"{type.FullName}.{method.Name}");
            }
        }

        Assert.Empty(offenders);
    }

    // ── 트레이 상주 (add-tray-resident) ──────────────────────────────────────

    /// <summary>
    /// 깨우기 신호(D-099)는 판정 뒤 · 단일 실행 잠금 앞에 만든다. 판정보다 앞이면 시험 호스트가 이름 있는 객체를 만들고,
    /// 잠금보다 뒤면 둘째 실행이 첫째가 만들기 전에 신호를 보내 잃는다 [실측 — 뒤에 만들면 수신 0/10].
    /// </summary>
    [Fact]
    public void OnStartup_은_깨우기_신호를_판정_뒤_잠금_앞에_만들고_둘째_실행은_신호를_보낸다()
    {
        var calls = Il.Calls(StateMachineOf(typeof(App), "OnStartup")).ToList();
        int Find(Func<MethodBase, bool> match, int from = 0) => calls.FindIndex(from, m => match(m));

        var guard = Find(m => m.Name == nameof(App.ShouldCompose) && m.DeclaringType == typeof(App));
        var wake = Find(m => m.Name == "CreateWakeSignal" && m.DeclaringType == typeof(App));
        var mutex = Find(m => m is ConstructorInfo && m.DeclaringType == typeof(Mutex));

        Assert.True(guard >= 0 && wake >= 0 && mutex >= 0, "판정 · 깨우기 신호 · 잠금 호출을 찾지 못했다");
        Assert.True(wake > guard, "깨우기 신호를 판정보다 먼저 만든다 — 시험 호스트가 이름 있는 객체를 만든다");
        Assert.True(mutex > wake, "깨우기 신호를 단일 실행 잠금 뒤에 만든다 — 둘째가 먼저 보낸 신호를 잃는다");

        // 잠금을 못 쥔 둘째는 신호를 보내고 끝난다
        var set = Find(m => m.Name == nameof(EventWaitHandle.Set) && typeof(EventWaitHandle).IsAssignableFrom(m.DeclaringType), mutex);
        var shutdown = Find(m => m.Name == nameof(System.Windows.Application.Shutdown)
                                 && m.DeclaringType == typeof(System.Windows.Application), mutex);
        Assert.True(set > mutex && set < shutdown, "둘째 실행이 Shutdown 전에 깨우기 신호를 보내지 않는다");

        // 같은 사용자 · 같은 로그온 세션만 연다 — 옵션 없는 생성자로 바꾸면 다른 사용자 프로그램도 같은 이름을 쥔다
        var create = Il.Instructions(typeof(App).GetMethod("CreateWakeSignal", BindingFlags.NonPublic | BindingFlags.Static)!).ToList();
        Assert.Contains(create, i => i.Target is ConstructorInfo c && c.DeclaringType == typeof(EventWaitHandle)
                                     && c.GetParameters().Any(p => p.ParameterType == typeof(NamedWaitHandleOptions)));
        foreach (var option in new[] { "set_CurrentUserOnly", "set_CurrentSessionOnly" })
        {
            var at = create.FindIndex(i => i.Target?.Name == option && i.Target.DeclaringType == typeof(NamedWaitHandleOptions));
            Assert.True(at > 0 && create[at - 1].Code == OpCodes.Ldc_I4_1, $"{option}(true) 가 아니다");
        }
    }

    /// <summary>
    /// 숨은 창도 "열린 창"으로 세어, 기본값(마지막 창)이면 숨겨 둔 다른 창이 남을 때 주 창을 닫아도 앱이 안 끝난다 [실측 — 리서치] (D-097).
    /// 첫 실행으로 정해진 뒤 · 조립 전에 바꾼다.
    /// </summary>
    [Fact]
    public void OnStartup_은_첫_실행에서_주_창이_닫히면_끝나게_한다()
    {
        var code = Il.Instructions(StateMachineOf(typeof(App), "OnStartup")).ToList();

        var mode = code.FindIndex(i => i.Target is MethodInfo { Name: "set_ShutdownMode" } m
                                       && m.DeclaringType == typeof(System.Windows.Application));
        var mutex = code.FindIndex(i => i.Target is ConstructorInfo && i.Target.DeclaringType == typeof(Mutex));
        var compose = code.FindIndex(i => i.Target is MethodInfo { Name: "ComposeAsync" } m && m.DeclaringType == typeof(App));

        Assert.True(mode > 0, "ShutdownMode 를 정하지 않는다");
        Assert.Equal(1, (int)System.Windows.ShutdownMode.OnMainWindowClose);
        Assert.Equal(OpCodes.Ldc_I4_1, code[mode - 1].Code);
        Assert.True(mode > mutex && mode < compose, "ShutdownMode 를 단일 실행 판정 뒤 · 조립 전에 정하지 않는다");
    }

    /// Windows 종료의 기본 처리(Shutdown)는 Closing 의 비동기 저장을 버린다 [실측 — 모의 0/2]. 그 전에 조용한 저장을 기다린다 (D-098).
    [Fact]
    public void OnSessionEnding_은_기본_처리_전에_조용한_저장을_기다린다()
    {
        var method = typeof(App).GetMethod("OnSessionEnding", BindingFlags.Instance | BindingFlags.NonPublic,
                                           [typeof(System.Windows.SessionEndingCancelEventArgs)])!;
        Assert.Equal(typeof(App), method.DeclaringType);

        var calls = Il.Calls(method).ToList();
        var run = calls.FindIndex(m => m.DeclaringType == typeof(TextBean.Views.Platform.SessionEndSave)
                                       && m.Name == nameof(TextBean.Views.Platform.SessionEndSave.Run)
                                       && m.GetParameters().Any(p => p.ParameterType == typeof(TextBean.ViewModels.ShellViewModel)));
        var baseCall = calls.FindIndex(m => m.Name == "OnSessionEnding" && m.DeclaringType == typeof(System.Windows.Application));

        Assert.True(run >= 0, "OnSessionEnding 이 SessionEndSave.Run 을 부르지 않는다");
        Assert.True(baseCall > run, "기본 처리(base.OnSessionEnding)가 저장보다 먼저다");
    }

    /// <summary>
    /// 실제 트레이 아이콘 · 전원 알림 구독은 조립 루트에서만 만든다 (D-103). 화면 시험은 MainWindow 를 실제로 만든다 —
    /// 창이나 셸이 만들면 시험이 사용자 트레이에 아이콘을 올리고 실제 시스템 이벤트를 구독한다.
    /// </summary>
    [Fact]
    public void 실제_트레이와_전원_알림은_조립_루트에서만_만든다()
    {
        var offenders = new List<string>();
        var composed = new HashSet<string>();

        foreach (var type in typeof(App).Assembly.GetTypes())
        {
            foreach (var method in DeclaredMethods(type))
            {
                foreach (var target in Il.Calls(method))
                {
                    var kind = ResidencyKind(target);
                    if (kind is null) continue;

                    var owner = Outermost(type);
                    var allowed = kind switch
                    {
                        "TrayIcon" or "SystemPowerEvents" => owner == typeof(App) && method.Name == "AttachTrayResidency",
                        "NotifyIcon" => owner == typeof(TextBean.Views.Platform.TrayIcon),
                        _ => owner == typeof(TextBean.Services.SystemPowerEvents),
                    };
                    if (allowed && owner == typeof(App)) composed.Add(kind);
                    if (!allowed) offenders.Add($"{type.FullName}.{method.Name} → {kind}");
                }
            }
        }

        Assert.Empty(offenders);
        Assert.Equal(["SystemPowerEvents", "TrayIcon"], composed.Order());   // 검사가 아무것도 못 보는 것이 아니다
    }

    /// <summary>
    /// 트레이 상주는 시작 키 입력이 끝난 뒤에 단다(D-108) — 먼저 달면 숨은 창을 주인으로 키 입력창이 뜬다.
    /// 세 부품(트레이 · 깨우기 · 절전)을 다 달고, 대기 한도는 Windows 가 기다려 주는 시간 안이다.
    /// App 은 시험에서 못 만들어(LL-087) 조립 연결은 IL 로만 지킨다 (비판 검토 R13).
    /// </summary>
    [Fact]
    public void 조립_루트는_시작_키_입력_뒤에_트레이_깨우기_절전을_모두_단다()
    {
        var compose = Il.Calls(StateMachineOf(typeof(App), "ComposeAsync")).ToList();
        var show = compose.FindIndex(m => m.Name == nameof(System.Windows.Window.Show));
        var start = compose.FindIndex(m => m.Name == nameof(TextBean.ViewModels.ShellViewModel.StartAsync));
        var attach = compose.FindIndex(m => m.Name == "AttachTrayResidency" && m.DeclaringType == typeof(App));
        Assert.True(show >= 0 && start > show, "창을 보인 뒤 키를 묻지 않는다");
        Assert.True(attach > start, "트레이 상주를 시작 키 입력보다 먼저 단다");

        var attachCalls = Il.Calls(typeof(App).GetMethod("AttachTrayResidency", BindingFlags.Instance | BindingFlags.NonPublic)!).ToList();
        foreach (var part in new[] { typeof(TrayController), typeof(WakeListener), typeof(SuspendLock) })
            Assert.Contains(attachCalls, m => m is ConstructorInfo && m.DeclaringType == part);

        TimeSpan Limit(string name) => (TimeSpan)typeof(App).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.InRange(Limit("SessionSaveLimit"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4.5));   // Windows 약 5초 [문서]
        Assert.InRange(Limit("SuspendLockLimit"), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.8));  // Windows 약 2초 [문서]

        // 깨우기 신호는 대기를 푼 뒤 해제한다
        var exit = Il.Calls(typeof(App).GetMethod("OnExit", BindingFlags.Instance | BindingFlags.NonPublic)!).ToList();
        Assert.Contains(exit, m => m.Name == nameof(IDisposable.Dispose) && m.DeclaringType == typeof(WaitHandle));
    }

    /// 시험 어셈블리는 가짜(FakeTrayIcon · FakePowerEvents)만 쓴다 (D-103).
    [Fact]
    public void 시험_어셈블리는_실제_트레이와_전원_알림을_만들지_않는다()
    {
        var offenders = typeof(StartupGuardTests).Assembly.GetTypes()
            .SelectMany(type => DeclaredMethods(type).SelectMany(method => Il.Calls(method)
                .Where(target => ResidencyKind(target) is not null)
                .Select(target => $"{type.FullName}.{method.Name} → {ResidencyKind(target)}")))
            .ToList();

        Assert.Empty(offenders);
    }

    /// 실제 트레이 · 전원 알림을 만드는 호출이면 그 이름. WinForms 는 시험 프로젝트가 참조하지 않아 이름으로 본다.
    private static string? ResidencyKind(MethodBase target) => target switch
    {
        ConstructorInfo c when c.DeclaringType == typeof(TextBean.Views.Platform.TrayIcon) => "TrayIcon",
        ConstructorInfo c when c.DeclaringType == typeof(TextBean.Services.SystemPowerEvents) => "SystemPowerEvents",
        ConstructorInfo c when c.DeclaringType?.FullName == "System.Windows.Forms.NotifyIcon" => "NotifyIcon",
        { Name: "add_PowerModeChanged" } m when m.DeclaringType?.FullName == "Microsoft.Win32.SystemEvents" => "PowerModeChanged 구독",
        _ => null
    };

    private static IEnumerable<MethodBase> DeclaredMethods(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        return [.. type.GetMethods(all), .. type.GetConstructors(all)];
    }

    /// 람다 · async 상태 기계는 컴파일러가 만든 중첩 형식에 들어간다 — 바깥 형식으로 판정한다.
    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }

    /// 시험이 일부러 낸 실패가 사용자의 실제 앱 로그(%APPDATA%\TextBean\logs)에 섞였다 (D-045 · D-046). 시작 때 임시 폴더로 돌린다.
    [Fact]
    public void 시험은_실제_앱_로그에_쓰지_않는다()
    {
        var tests = Path.Combine(Path.GetTempPath(), "TextBeanTests");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logs = TestAssemblySetup.LogDirectory;     // 지금 값은 AppLogTests 가 잠깐 바꾼다 — 돌린 직후 값을 본다

        Assert.StartsWith(tests, logs, StringComparison.OrdinalIgnoreCase);
        Assert.False(logs.StartsWith(appData, StringComparison.OrdinalIgnoreCase));
    }

    private static MethodBase StateMachineOf(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var machine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                      ?? throw new InvalidOperationException($"{methodName} 은 async 가 아니다");
        return machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    }

    /// <summary>
    /// IL 을 순서대로 걷는다 — 연산자와 그 자리, call · callvirt · newobj 이면 대상 메서드. 시험 전용 — 연산자 크기만 알면 된다.
    /// 해석하지 못한 토큰(제네릭 문맥 등)은 대상을 비운다. 많이 비면 시험이 아무것도 못 보는 것이라 결과에 섞지 않는다.
    /// </summary>
    private static class Il
    {
        private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        public readonly record struct Instruction(int Offset, OpCode Code, MethodBase? Target);

        public static IEnumerable<MethodBase> Calls(MethodBase method)
            => Instructions(method).Where(i => i.Target is not null).Select(i => i.Target!);

        public static IEnumerable<Instruction> Instructions(MethodBase method)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null) yield break;

            var module = method.Module;
            Type[]? typeArgs = method.DeclaringType is { IsGenericType: true } t ? t.GetGenericArguments() : null;
            Type[]? methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

            for (var i = 0; i < il.Length;)
            {
                var offset = i;
                short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
                i += il[i] == 0xFE ? 2 : 1;
                var code = Codes[value];

                MethodBase? target = null;
                if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                {
                    var token = BitConverter.ToInt32(il, i);
                    try { target = module.ResolveMethod(token, typeArgs, methodArgs); }
                    catch (ArgumentException) { }
                }
                yield return new Instruction(offset, code, target);

                i += OperandSize(code.OperandType, il, i);
            }
        }

        private static int OperandSize(OperandType type, byte[] il, int at) => type switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
            _ => 4
        };
    }
}
