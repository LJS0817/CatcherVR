using UnityEngine;

/// <summary>수비 포메이션 종류. 설계도 4.4 / 13.1 의 <c>DefensiveShiftType</c>.</summary>
public enum DefensiveShiftType
{
    Standard,     // 표준 수비
    InfieldIn,    // 내야 전진 수비
    DoublePlay,   // 병살 수비
    NoDoubles     // 외야 장타 방지
}

/// <summary>
/// 플레이어(포수)의 상태 스냅샷. 설계도 13.1 의 <c>PlayerCatcherState</c>.
/// 실제 값은 <see cref="DefenseCommand"/> 가 보유하며, 이 구조체는 그 읽기 뷰다.
/// </summary>
public struct PlayerCatcherState
{
    public Transform CatcherTransform;
    /// <summary>홈플레이트 수비 위치를 점유하고 있는가.</summary>
    public bool IsAtHomePlate;
    /// <summary>번트/팝플라이를 잡으러 뛰어나갔는가.</summary>
    public bool IsChargingForBunt;
    /// <summary>컷(Cut) 명령 입력 여부.</summary>
    public bool CutCommandTriggered;
    /// <summary>흘려보내기(Let it go) 명령 여부.</summary>
    public bool LetGoCommandTriggered;

    public static PlayerCatcherState Current()
    {
        PlayerCatcherState s = new PlayerCatcherState();
        s.CatcherTransform = DefenseCommand.CatcherTransform;
        s.IsAtHomePlate = DefenseCommand.IsCatcherAtHome;
        s.IsChargingForBunt = DefenseCommand.IsCatcherCharging;
        s.CutCommandTriggered = DefenseCommand.CutRequested;
        s.LetGoCommandTriggered = DefenseCommand.LetGoRequested;
        return s;
    }
}

/// <summary>
/// 설계도 13.1 의 <c>GameTacticalContext</c> — 수비 판단에 필요한 경기 상황을 한 곳에 모은 컨텍스트.
///
/// 매 프레임 새로 만들지 않고 <see cref="Current"/> 로 프레임당 1회만 갱신한다.
/// 조율자 · 포메이션 · 송구 판단이 모두 같은 스냅샷을 본다.
///
/// 참고 — 설계도가 정의한 <c>BaseIndex</c> / <c>HitType</c> 은 이 프로젝트의
/// <see cref="BASE_TYPE"/> / <see cref="BATTED_BALL_TYPE"/> 와 1:1로 대응하므로
/// 중복 정의하지 않고 기존 열거형을 그대로 쓴다.
/// </summary>
public class GameTacticalContext
{
    // ── 포메이션 ──────────────────────────────────────────
    public DefensiveShiftType ActiveShift = DefensiveShiftType.Standard;

    // ── 카운트 / 주자 ─────────────────────────────────────
    public int OutCount;
    public bool RunnerOnFirst;
    public bool RunnerOnSecond;
    public bool RunnerOnThird;
    public int RunnerCount;

    // ── 경기 상황 ─────────────────────────────────────────
    public int Inning;
    public int ScoreDifference;

    /// <summary>내야 전진 수비 중인가(설계도 13.1 InfieldInDefense).</summary>
    public bool InfieldInDefense { get { return ActiveShift == DefensiveShiftType.InfieldIn; } }

    // ── 플레이어(포수) 연동 ───────────────────────────────
    public PlayerCatcherState PlayerCatcher;

    /// <summary>사용자가 지정한 송구 베이스(미지정 시 E_SELF).</summary>
    public BASE_TYPE UserCommandedBase { get { return DefenseCommand.UserCommandedBase; } }

    // ── 플레이 진행 상태 ──────────────────────────────────
    /// <summary>상황 종료 판정(설계도 12.1). Team 이 매 프레임 갱신한다.</summary>
    public bool IsPlayTerminated;

    /// <summary>만루인가.</summary>
    public bool BasesLoaded { get { return RunnerOnFirst && RunnerOnSecond && RunnerOnThird; } }

    /// <summary>득점권(2·3루) 주자가 있는가.</summary>
    public bool ScoringPosition { get { return RunnerOnSecond || RunnerOnThird; } }

    // ============================================================
    //  스냅샷
    // ============================================================

    static readonly GameTacticalContext _instance = new GameTacticalContext();
    static int _lastFrame = -1;

    /// <summary>현재 프레임의 전술 컨텍스트(프레임당 1회만 실제로 갱신).</summary>
    public static GameTacticalContext Current(bool force = false)
    {
        if (!force && _lastFrame == Time.frameCount) return _instance;
        _lastFrame = Time.frameCount;
        _instance.Refresh();
        return _instance;
    }

    void Refresh()
    {
        CountsProvider cp = CountsProvider.provider;
        OutCount = cp != null ? cp.GetCount(COUNT_TYPE.E_OUT) : 0;
        Inning = cp != null ? cp.CurrentInning : 1;
        ScoreDifference = cp != null ? (cp.TeamAScore - cp.TeamBScore) : 0;

        RunnerOnFirst = false;
        RunnerOnSecond = false;
        RunnerOnThird = false;
        RunnerCount = 0;

        System.Collections.Generic.List<RunnerInfo> runners = RunnerTracker.Snapshot();
        for (int i = 0; i < runners.Count; i++)
        {
            RunnerCount++;
            switch (runners[i].TargetBase)
            {
                case BASE_TYPE.E_FIRST_BASE: RunnerOnFirst = true; break;
                case BASE_TYPE.E_SECOND_BASE: RunnerOnSecond = true; break;
                case BASE_TYPE.E_THIRD_BASE: RunnerOnThird = true; break;
            }
        }

        // 주자 표시가 아직 갱신되지 않았을 수 있으므로 베이스 상태로도 보강한다.
        BasePositionProvider bp = BasePositionProvider.provider;
        if (bp != null && bp.Bases != null && bp.Bases.Count >= 3)
        {
            RunnerOnFirst |= bp.GetAttckerBaseState(BASE_TYPE.E_FIRST_BASE) != '_';
            RunnerOnSecond |= bp.GetAttckerBaseState(BASE_TYPE.E_SECOND_BASE) != '_';
            RunnerOnThird |= bp.GetAttckerBaseState(BASE_TYPE.E_THIRD_BASE) != '_';
        }

        ActiveShift = DefensiveAlignment.ActiveShift;
        PlayerCatcher = PlayerCatcherState.Current();
    }
}
