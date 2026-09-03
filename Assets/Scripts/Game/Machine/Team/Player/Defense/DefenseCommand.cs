using UnityEngine;

/// <summary>
/// 플레이어(포수)가 수비 AI 에게 내리는 지휘 명령의 단일 저장소.
///
/// 설계도 6장 「사용자 송구 지휘 / User Throw Override」 구현부.
/// AI 수비수는 공을 잡은 뒤 <see cref="UserCommandedBase"/> 가 지정되어 있으면
/// 자체 최적 판단을 <b>무시하고</b> 해당 베이스로 강제 송구한다.
///
/// ─────────────────────────────────────────────────────────────
///  ▣ 입력 연결 방법 (이 클래스는 순수 API 이므로 입력 바인딩은 프로젝트 쪽에서 한다)
///
///   // 베이스 지정 (△=2루, □=3루, ○=1루, ×=홈)
///   DefenseCommand.CommandThrowTo(BASE_TYPE.E_SECOND_BASE);
///
///   // 컷오프 지시
///   DefenseCommand.CommandCut();      // 중계맨이 가로채서 재송구
///   DefenseCommand.CommandLetGo();    // 중계맨이 흘려보내 홈 다이렉트 승부
///
///   // 플레이어(포수) 트랜스폼 등록 — 컷오프 1자 정렬 / 투수 홈커버 판정에 쓰인다
///   DefenseCommand.RegisterPlayerCatcher(myCatcherTransform);
///
///   // 번트·팝플라이를 잡으러 홈을 비울 때(선택). 등록만 해두면 위치로도 자동 판정한다.
///   DefenseCommand.SetCatcherCharging(true);
///
///  플레이가 끝나면 Team 이 <see cref="ResetAll"/> 을 호출해 자동으로 비운다.
/// ─────────────────────────────────────────────────────────────
/// </summary>
public static class DefenseCommand
{
    // ────────────────────────────── 송구 베이스 지정 ──────────────────────────────

    /// <summary>사용자가 마지막으로 지정한 베이스(미지정 = E_SELF).</summary>
    static BASE_TYPE _commandedBase = BASE_TYPE.E_SELF;
    static float _commandTime = -999f;

    /// <summary>지정 명령의 유효 시간(초). 이 시간이 지나면 자동 만료된다.</summary>
    public const float COMMAND_LIFETIME = 8f;

    /// <summary>
    /// 지금 유효한 사용자 지정 베이스. 미지정이거나 만료되었으면 E_SELF.
    /// 설계도 13.1 의 <c>UserCommandedBase</c> 에 해당한다.
    /// </summary>
    public static BASE_TYPE UserCommandedBase
    {
        get
        {
            if (_commandedBase == BASE_TYPE.E_SELF) return BASE_TYPE.E_SELF;
            if (Time.time - _commandTime > COMMAND_LIFETIME) return BASE_TYPE.E_SELF;
            return _commandedBase;
        }
    }

    public static bool HasBaseCommand { get { return UserCommandedBase != BASE_TYPE.E_SELF; } }

    /// <summary>베이스 지정. 타격 직후부터 릴리스 직전까지 언제든 갱신할 수 있다.</summary>
    public static void CommandThrowTo(BASE_TYPE b)
    {
        if (b == BASE_TYPE.E_BALL) b = BASE_TYPE.E_SELF;
        _commandedBase = b;
        _commandTime = Time.time;
    }

    /// <summary>지정 해제 — 이후에는 AI 자체 판단으로 돌아간다.</summary>
    public static void ClearThrowCommand()
    {
        _commandedBase = BASE_TYPE.E_SELF;
        _commandTime = -999f;
    }

    /// <summary>
    /// 지정 베이스로 송구가 실제로 실행된 뒤 호출. 한 번 쓴 명령은 소비되어
    /// 다음 송구(예: 병살 후속 송구)는 다시 AI 판단을 따르게 한다.
    /// </summary>
    public static void ConsumeThrowCommand()
    {
        ClearThrowCommand();
    }

    // ────────────────────────────── 컷 / 통과 지시 ──────────────────────────────

    static bool _cut;
    static bool _letGo;
    static float _callTime = -999f;

    /// <summary>컷·통과 지시의 유효 시간(초).</summary>
    public const float CALL_LIFETIME = 4f;

    static bool CallAlive { get { return Time.time - _callTime <= CALL_LIFETIME; } }

    /// <summary>중계맨이 송구를 가로채야 하는가.</summary>
    public static bool CutRequested { get { return _cut && CallAlive; } }

    /// <summary>중계맨이 송구를 흘려보내야 하는가(홈 다이렉트).</summary>
    public static bool LetGoRequested { get { return _letGo && CallAlive; } }

    public static void CommandCut() { _cut = true; _letGo = false; _callTime = Time.time; }
    public static void CommandLetGo() { _letGo = true; _cut = false; _callTime = Time.time; }

    public static void ClearCallCommands()
    {
        _cut = false;
        _letGo = false;
        _callTime = -999f;
    }

    // ────────────────────────────── 플레이어(포수) 상태 ──────────────────────────────

    /// <summary>플레이어가 조작하는 포수의 트랜스폼(등록되지 않으면 null).</summary>
    public static Transform CatcherTransform { get; private set; }

    static bool _chargingFlag;
    static float _chargingFlagTime = -999f;

    public static void RegisterPlayerCatcher(Transform t) { CatcherTransform = t; }

    /// <summary>번트/팝플라이 처리를 위해 홈을 비웠음을 명시적으로 알린다.</summary>
    public static void SetCatcherCharging(bool charging)
    {
        _chargingFlag = charging;
        _chargingFlagTime = Time.time;
    }

    /// <summary>플레이어 포수의 현재 위치(등록되지 않았으면 홈플레이트).</summary>
    public static Vector3 CatcherPosition
    {
        get { return CatcherTransform != null ? CatcherTransform.position : FieldMetrics.Home; }
    }

    /// <summary>
    /// 포수가 홈을 비우고 전진했는가.
    /// 명시적 플래그가 있으면 그것을 따르고, 없으면 홈플레이트로부터의 거리로 자동 판정한다.
    /// (설계도 5.4 : 포수 쇄도 시 투수가 홈 커버)
    /// </summary>
    public static bool IsCatcherCharging
    {
        get
        {
            if (_chargingFlag && Time.time - _chargingFlagTime <= COMMAND_LIFETIME) return true;
            if (CatcherTransform == null) return false;
            return FieldMetrics.FlatDistance(CatcherTransform.position, FieldMetrics.Home)
                   > FieldMetrics.M(DefenseTuning.CATCHER_AWAY_FROM_HOME);
        }
    }

    /// <summary>포수가 홈플레이트를 지키고 있는가.</summary>
    public static bool IsCatcherAtHome { get { return !IsCatcherCharging; } }

    // ────────────────────────────── 초기화 ──────────────────────────────

    /// <summary>플레이 종료 시 모든 지휘 명령을 비운다(포수 등록은 유지).</summary>
    public static void ResetAll()
    {
        ClearThrowCommand();
        ClearCallCommands();
        _chargingFlag = false;
        _chargingFlagTime = -999f;
    }
}
