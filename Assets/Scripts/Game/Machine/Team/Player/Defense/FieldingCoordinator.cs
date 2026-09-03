using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 팀 단위 수비 조율자.
///
/// 공이 살아있는 동안 주기적으로 다시 판단하며 다음을 배정한다.
///   PRIMARY   : 타구를 처리할 야수 (도달 시간 + 포지션 콜 우선순위 + 수비 존)
///   SECONDARY : 뒤를 받치는 트레일 백업
///   CUTOFF    : 송구 라인 위의 중계맨 (외야 → 베이스 / 홈)
///   BASE_COVER: 각 베이스를 지키는 야수 (상황별 담당 테이블 · 번트 대응 포함)
///   BACKUP    : 악송구 대비 베이스 뒤쪽 백업 (설계도 5.5 매트릭스 기준)
///
/// 플레이어(포수)와의 연동 (설계도 3.2-5 / 5.4)
///   · 홈 근처 타구를 플레이어가 잡으러 나오면 AI 는 즉시 양보(Yield)한다.
///   · 그때 투수는 비어 있는 홈플레이트로 들어가 홈 커버를 수행한다.
///   · 홈 송구 컷오프맨은 "외야수 - 플레이어(포수)" 를 잇는 직선 위에 정렬한다.
/// </summary>
public class FieldingCoordinator
{
    readonly Team _team;

    readonly List<BaseballPlayer> _fielders = new List<BaseballPlayer>(9);
    readonly Dictionary<PLAYER_TYPE, BaseballPlayer> _byType = new Dictionary<PLAYER_TYPE, BaseballPlayer>(9);
    readonly HashSet<BaseballPlayer> _assigned = new HashSet<BaseballPlayer>();
    readonly Dictionary<BASE_TYPE, BaseballPlayer> _baseCover = new Dictionary<BASE_TYPE, BaseballPlayer>(4);

    BaseballPlayer _primary;
    BaseballPlayer _secondary;
    InterceptResult[] _results;
    Vector3 _ballSpotHint;
    bool _useHint;

    /// <summary>이번 타구를 플레이어(포수)에게 양보했는가.</summary>
    public bool YieldedToPlayer { get; private set; }

    public BaseballPlayer Primary { get { return _primary; } }
    public BASE_TYPE ExpectedThrowBase { get; private set; }
    public BATTED_BALL_TYPE BallType { get; private set; }
    public bool Live { get; private set; }

    /// <summary>현재 배정된 중계맨(컷오프). 컷/통과 지시의 대상이다.</summary>
    public BaseballPlayer CutoffMan { get; private set; }

    /// <summary>설계도 5.5 매트릭스에서 판별된 현재 타구 코스.</summary>
    public HIT_COURSE CurrentCourse { get; private set; }

    float _lastEvalTime = -99f;

    // GC 를 만들지 않도록 미리 잡아두는 버퍼들
    readonly bool[] _needBase = new bool[5];

    public FieldingCoordinator(Team team)
    {
        _team = team;
        ExpectedThrowBase = BASE_TYPE.E_FIRST_BASE;
    }

    // ============================================================
    //  진입점
    // ============================================================

    /// <summary>타구 발생. 즉시 역할을 배정하고 라이브 상태로 전환.</summary>
    public void OnBattedBall(BallTrajectory traj, BATTED_BALL_TYPE type)
    {
        BallType = type;
        Live = true;
        _primary = null;
        _secondary = null;
        _lastEvalTime = -99f;
        Evaluate(traj, 0f, true);
    }

    /// <summary>공이 살아있는 동안 주기적으로 호출. 필요할 때만 실제로 재계산한다.</summary>
    public void Tick(BallTrajectory traj, float elapsed)
    {
        if (!Live) return;
        if (Time.time - _lastEvalTime < DefenseTuning.ROLE_REEVALUATE_INTERVAL) return;
        Evaluate(traj, elapsed, false);
    }

    /// <summary>플레이 종료. 모두 원위치 지시.</summary>
    public void EndPlay()
    {
        Live = false;
        _primary = null;
        _secondary = null;
        CutoffMan = null;
        YieldedToPlayer = false;
        CollectFielders();
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer p = _fielders[i];
            p.SetAssignment(FieldAssignment.Idle(p.HomePosition));
        }
    }

    /// <summary>누군가 공을 잡았다. 나머지는 추격을 멈추고 커버/백업 태세로.</summary>
    public void OnBallSecured(BaseballPlayer catcher, BallTrajectory traj)
    {
        _primary = catcher;
        _useHint = false;
        _lastEvalTime = -99f;
        Evaluate(traj, 0f, true, true);
    }

    /// <summary>
    /// 송구가 출발했다. 던진 야수는 더 이상 1차 처리자가 아니므로 역할을 다시 나눈다.
    /// (이 처리를 빼면 던진 야수가 자기 송구를 쫓아가는 현상이 생긴다.)
    /// </summary>
    public void OnThrowStarted(BaseballPlayer thrower, BASE_TYPE targetBase, Vector3 aim)
    {
        _primary = null;
        _secondary = null;
        _ballSpotHint = aim;
        _useHint = true;
        _lastEvalTime = -99f;
        Evaluate(null, 0f, true, true);
        if (targetBase != BASE_TYPE.E_SELF) ExpectedThrowBase = targetBase;
        _useHint = false;
    }

    // ============================================================
    //  핵심 : 역할 배정
    // ============================================================

    void Evaluate(BallTrajectory traj, float elapsed, bool force, bool ballSecured = false)
    {
        _lastEvalTime = Time.time;

        CollectFielders();
        if (_fielders.Count == 0) return;

        FieldMetrics.Refresh();
        _assigned.Clear();
        _baseCover.Clear();
        CutoffMan = null;

        // ── 1. 각 야수의 인터셉트 계산 ───────────────────────────
        bool preferAir = BallType == BATTED_BALL_TYPE.FLY_BALL ||
                         BallType == BATTED_BALL_TYPE.POP_FLY ||
                         BallType == BATTED_BALL_TYPE.LINE_DRIVE;

        if (_results == null || _results.Length < _fielders.Count)
            _results = new InterceptResult[Mathf.Max(9, _fielders.Count)];
        InterceptResult[] results = _results;

        if (!ballSecured && traj != null && traj.IsValid)
        {
            for (int i = 0; i < _fielders.Count; i++)
            {
                BaseballPlayer f = _fielders[i];
                results[i] = InterceptSolver.Solve(
                    traj,
                    f.transform.position,
                    f.transform.forward,
                    f.Attributes,
                    elapsed,
                    f.IsRunningNow,
                    true,
                    preferAir);
            }
            SelectPrimary(results, force, traj);
        }
        else
        {
            for (int i = 0; i < _fielders.Count; i++)
                results[i] = InterceptResult.None(_fielders[i].transform.position);
        }

        Vector3 ballSpot = _useHint ? _ballSpotHint :
                           (_primary != null ? _primary.transform.position :
                           (traj != null && traj.IsValid ? traj.LandPosition : FieldMetrics.Home));
        
        // 공을 이미 쥔 상태(ballSecured)라면 위에서 설정한 실제 위치를 유지하고,
        // 그렇지 않을 때만 예측된 차단 지점(Point/PursuitPoint)을 공통 목표 지점(ballSpot)으로 쓴다.
        if (_primary != null && !_useHint && !ballSecured)
        {
            int pi = _fielders.IndexOf(_primary);
            if (pi >= 0 && results[pi].CanReach) ballSpot = results[pi].Point;
            else if (pi >= 0) ballSpot = results[pi].PursuitPoint;
        }
        else if (_primary == null && YieldedToPlayer && traj != null && traj.IsValid)
        {
            ballSpot = traj.LandPosition;
        }

        // ── 1-b. 설계도 5.5 : 12개 타구 코스 판별 ────────────────
        CurrentCourse = HitCourseMatrix.Classify(ballSpot, BallType);

        // ── 2. 송구 목표 베이스 예측 ─────────────────────────────
        ExpectedThrowBase = PredictThrowBase(ballSpot);

        // ── 3. PRIMARY 지시 ──────────────────────────────────────
        if (_primary != null && !ballSecured)
        {
            int pi = _fielders.IndexOf(_primary);
            InterceptResult ir = pi >= 0 ? results[pi] : InterceptResult.None(_primary.transform.position);

            FieldAssignment a = new FieldAssignment();
            a.Role = DEFENSIVE_ROLE.CHASER;
            a.IsPrimary = true;
            a.Intercept = ir;
            a.Target = ir.CanReach ? new Vector3(ir.Point.x, FieldMetrics.GroundY, ir.Point.z) : ir.PursuitPoint;
            a.Target = ApplyRoutingBias(_primary, a.Target, ir);
            a.FaceTarget = ballSpot;
            a.CoverBase = BASE_TYPE.E_SELF;
            a.Live = true;
            _primary.SetAssignment(a);
            _assigned.Add(_primary);
        }
        else if (_primary != null && ballSecured)
        {
            // 공을 쥔 야수는 커버/백업 지시를 받지 않는다(송구 판단이 우선).
            FieldAssignment a = new FieldAssignment();
            a.Role = DEFENSIVE_ROLE.CHASER;
            a.IsPrimary = true;
            a.Target = _primary.transform.position;
            a.FaceTarget = FieldMetrics.BasePos(ExpectedThrowBase);
            a.CoverBase = BASE_TYPE.E_SELF;
            a.Intercept = InterceptResult.None(_primary.transform.position);
            a.Live = true;
            _primary.SetAssignment(a);
            _assigned.Add(_primary);
        }

        // ── 4. SECONDARY(트레일 백업) ───────────────────────────
        if (!ballSecured) SelectSecondary(results, ballSpot);

        // ── 5. 중계맨(CUTOFF / RELAY) ───────────────────────────
        AssignCutoff(ballSpot);

        // ── 6. 베이스 커버 ───────────────────────────────────────
        AssignBaseCoverage(ballSpot);

        // ── 7. 백업 ─────────────────────────────────────────────
        AssignBackups(ballSpot);

        // ── 8. 남은 야수도 멈춰 있지 않는다 (설계도 14장 「AI 8인 유휴 제로」) ──
        //     원위치를 지키되 공과 주자를 계속 주시하는 OBSERVER 로 둔다.
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;
            SetObserver(f, ballSpot);
        }
    }

    // ------------------------------------------------------------
    //  1차 처리자 선정 (콜 우선순위 + 수비 존 + 히스테리시스)
    // ------------------------------------------------------------
    void SelectPrimary(InterceptResult[] results, bool force, BallTrajectory traj)
    {
        // (0) 플레이어(포수) 양보 판정 — 홈 근처 타구는 플레이어가 우선권을 갖는다
        if (ShouldYieldToPlayer(results, traj))
        {
            YieldedToPlayer = true;
            _primary = null;
            return;
        }
        YieldedToPlayer = false;

        // 1루/1-2루간 짧은 땅볼 판단 (투수가 처리하고 1루수가 베이스를 지키게 유도)
        Vector3 dropPoint = traj != null && traj.IsValid ? traj.LandPosition : FieldMetrics.Home;
        bool isShortRightGrounder = (BallType == BATTED_BALL_TYPE.GROUND_BALL || BallType == BATTED_BALL_TYPE.BUNT) &&
                                    dropPoint.x > 0f && FieldMetrics.FlatDistance(dropPoint, FieldMetrics.Home) < FieldMetrics.M(20f);
        
        bool pitcherCanReach = false;
        if (isShortRightGrounder)
        {
            for (int i = 0; i < _fielders.Count; i++)
            {
                if (_fielders[i].Type == PLAYER_TYPE.E_PITCHER && results[i].CanReach)
                {
                    pitcherCanReach = true;
                    break;
                }
            }
        }

        // (a) 도달 가능한 야수들 중 가장 이른 처리 시점
        float bestTime = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            if (!results[i].CanReach) continue;
            
            if (isShortRightGrounder && pitcherCanReach && _fielders[i].Type == PLAYER_TYPE.E_FIRST_BASE)
            {
                continue; // 투수가 잡을 수 있는 짧은 우측 땅볼은 1루수가 양보하여 베이스 커버에 전념한다.
            }

            if (results[i].BallTime < bestTime) bestTime = results[i].BallTime;
        }

        BaseballPlayer candidate = null;

        if (bestTime < float.MaxValue)
        {
            // (b) 동등 구간 안에서는 포지션 콜 우선순위로 결정
            //     — 중견수가 유격수를 부르고, 외야수가 내야수를 부르는 실제 규칙
            int bestPriority = int.MaxValue;
            float bestQuality = float.NegativeInfinity;
            for (int i = 0; i < _fielders.Count; i++)
            {
                if (!results[i].CanReach) continue;
                if (results[i].BallTime > bestTime + DefenseTuning.CALLOFF_TIE_WINDOW) continue;

                if (isShortRightGrounder && pitcherCanReach && _fielders[i].Type == PLAYER_TYPE.E_FIRST_BASE)
                {
                    continue; // 1루수 양보
                }

                int pr = _fielders[i].Attributes.CallPriority;

                // 자기 수비 존 안의 타구면 품질에 가산점 — 설계도 4.3 중첩 구역 규칙
                float q = results[i].Quality + ZoneBonus(_fielders[i], results[i]);

                if (pr < bestPriority || (pr == bestPriority && q > bestQuality))
                {
                    bestPriority = pr;
                    bestQuality = q;
                    candidate = _fielders[i];
                }
            }
        }
        else
        {
            // (c) 아무도 못 잡는 타구 — 가장 빨리 따라붙는 야수가 처리
            float bestGap = float.MaxValue;
            for (int i = 0; i < _fielders.Count; i++)
            {
                BaseballPlayer f = _fielders[i];

                // 내야수가 무조건 포기하는 Zone 체크 로직 삭제:
                // 공이 멈추거나 매우 느릴 때 내야수가 코앞에 두고도 외야수에게 양보하는 버그 유발.
                // 대신, 거리(gap) 기반으로 자연스럽게 양보를 유도한다.

                float gap = results[i].MyTime - results[i].BallTime;
                if (results[i].SampleIndex < 0)
                {
                    gap = FieldMetrics.FlatDistance(f.transform.position, results[i].PursuitPoint);
                    
                    // 내야수는 외야로 빠진 타구에 대해 외야수보다 거리 계산에 페널티를 주어 자연스러운 양보를 유도한다.
                    if (f.Attributes.IsInfielder)
                    {
                        gap *= 1.2f;
                    }
                }
                if (gap < bestGap) { bestGap = gap; candidate = f; }
            }
        }

        if (candidate == null) return;

        // (d) 히스테리시스 — 이미 달리고 있는 야수를 사소한 차이로 바꾸지 않는다
        if (!force && _primary != null && _primary != candidate)
        {
            int oldIdx = _fielders.IndexOf(_primary);
            int newIdx = _fielders.IndexOf(candidate);
            if (oldIdx >= 0 && newIdx >= 0 && results[oldIdx].CanReach)
            {
                if (results[newIdx].BallTime > results[oldIdx].BallTime - DefenseTuning.PRIMARY_SWITCH_HYSTERESIS)
                {
                    return; // 기존 처리자 유지
                }
            }
        }

        _primary = candidate;
    }

    /// <summary>
    /// 설계도 3.2-5 : 홈 근처 팝플라이 · 약한 번트에서 플레이어(포수)가 잡으러 나오고 있으면
    /// AI(투수 · 1루수 · 3루수)는 길을 비켜준다.
    /// 플레이어가 등록되어 있지 않거나 움직이지 않으면 양보하지 않는다(AI 가 정상 처리).
    /// </summary>
    bool ShouldYieldToPlayer(InterceptResult[] results, BallTrajectory traj)
    {
        if (DefenseCommand.CatcherTransform == null) return false;
        if (traj == null || !traj.IsValid) return false;

        Vector3 spot = traj.LandPosition;
        if (FieldMetrics.FlatDistance(spot, FieldMetrics.Home) > FieldMetrics.M(DefenseTuning.PLAYER_YIELD_RADIUS))
            return false;

        Vector3 catcherPos = DefenseCommand.CatcherPosition;
        FielderAttributes catcherAttr = FielderAttributes.For(PLAYER_TYPE.E_CATCHER);
        float playerTime = InterceptSolver.TimeToPoint(
            catcherPos, spot, DefenseCommand.CatcherTransform.forward, catcherAttr, DefenseCommand.IsCatcherCharging);

        // AI 중 가장 빠른 처리 시간
        float bestAi = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            if (!results[i].CanReach) continue;
            if (results[i].MyTime < bestAi) bestAi = results[i].MyTime;
        }
        if (bestAi >= float.MaxValue) return false;

        return playerTime <= bestAi + DefenseTuning.PLAYER_YIELD_WINDOW;
    }

    /// <summary>
    /// 설계도 4.3 「포지션별 수비 범위 스펙」 기반 가산점.
    /// 담당 커버리지 안이면 중심에 가까울수록, 1차 유효 반경 안이면 추가로 가산한다.
    /// 중첩 구역에서 콜 우선순위가 같을 때의 타이브레이커로 쓰인다.
    /// </summary>
    static float ZoneBonus(BaseballPlayer f, InterceptResult ir)
    {
        Vector3 p = ir.CanReach ? ir.Point : ir.PursuitPoint;

        float bonus = PositionCoverage.Priority(f.Type, p) * 0.25f;

        // 자기 기본 위치의 1차 유효 반경 안이면 확실한 자기 타구다
        if (PositionCoverage.WithinPrimaryRadius(f.Type, f.HomePosition, p)) bonus += 0.20f;

        return bonus;
    }

    // ------------------------------------------------------------
    //  2차 처리자(트레일)
    // ------------------------------------------------------------
    void SelectSecondary(InterceptResult[] results, Vector3 ballSpot)
    {
        _secondary = null;
        if (_primary == null) return;

        // 타구 진행 방향(홈 → 처리 지점) 뒤쪽을 받치는 야수를 고른다.
        Vector3 outward = ballSpot - FieldMetrics.Home;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f) outward = FieldMetrics.Forward;
        outward.Normalize();

        float best = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (f == _primary || _assigned.Contains(f)) continue;
            
            // 내야수는 2차 처리자(트레일 백업)에서 완전히 제외 (베이스 커버에 전념)
            if (f.Attributes.IsInfielder || f.Attributes.Position == PLAYER_TYPE.E_PITCHER || f.Attributes.Position == PLAYER_TYPE.E_CATCHER) 
                continue;

            // 처리 지점보다 바깥(외야 쪽)에 있는 야수가 트레일에 적합
            float depthMine = Vector3.Dot(f.transform.position - FieldMetrics.Home, outward);
            float depthBall = Vector3.Dot(ballSpot - FieldMetrics.Home, outward);
            if (depthMine < depthBall - FieldMetrics.M(4f)) continue;

            float d = FieldMetrics.FlatDistance(f.transform.position, ballSpot);
            if (d < best) { best = d; _secondary = f; }
        }

        if (_secondary == null) return;

        Vector3 target = ballSpot + outward * FieldMetrics.M(DefenseTuning.TRAIL_BACKUP_DISTANCE);
        if (_secondary.Attributes.IsOutfielder)
        {
            float minDist = FieldMetrics.M(40f);
            if (FieldMetrics.FlatDistance(target, FieldMetrics.Home) < minDist)
            {
                target = FieldMetrics.Home + outward * minDist;
            }
        }

        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.BACKUP;
        a.IsSecondary = true;
        a.Target = ClampToField(target);
        a.FaceTarget = ballSpot;
        a.CoverBase = BASE_TYPE.E_SELF;
        a.Live = true;
        _secondary.SetAssignment(a);
        _assigned.Add(_secondary);
    }

    // ------------------------------------------------------------
    //  중계(컷오프 / 릴레이)
    // ------------------------------------------------------------
    void AssignCutoff(Vector3 ballSpot)
    {
        if (_primary == null) return;

        bool primaryIsOF = _primary.Attributes.IsOutfielder;
        bool homeCutoff = primaryIsOF && ExpectedThrowBase == BASE_TYPE.E_HOME_BASE;

        // 홈 컷오프는 "외야수 - 플레이어(포수)" 직선 위에 정렬한다(설계도 5.3 동적 1자 정렬).
        Vector3 targetBasePos = homeCutoff
            ? DefenseCommand.CatcherPosition
            : FieldMetrics.BasePos(ExpectedThrowBase);

        float throwDist = FieldMetrics.FlatDistance(ballSpot, targetBasePos);

        bool needRelay = primaryIsOF &&
                         (throwDist > FieldMetrics.M(DefenseTuning.RELAY_TRIGGER_DISTANCE) ||
                          throwDist > _primary.Attributes.MaxThrowRange * 0.92f);

        if (!needRelay && !homeCutoff) return;

        bool leftSide = FieldMetrics.SprayAngle(ballSpot) > 0f;

        PLAYER_TYPE mainType;
        if (homeCutoff)
        {
            // 홈 송구 컷오프 : 좌익수 쪽이면 3루수, 우/중견 쪽이면 1루수
            mainType = leftSide ? PLAYER_TYPE.E_THIRD_BASE : PLAYER_TYPE.E_FIRST_BASE;
        }
        else
        {
            // 베이스 릴레이 : 좌측 타구는 유격수, 우측 타구는 2루수
            mainType = leftSide ? PLAYER_TYPE.E_SHORT_STOP : PLAYER_TYPE.E_SECOND_BASE;
        }

        // 설계도 5.5 매트릭스에 이 코스의 컷오프 담당이 명시되어 있으면 그것을 따른다.
        BaseballPlayer relay = MatrixAssignee(COURSE_ACTION.CUTOFF, ExpectedThrowBase);
        if (relay == null) relay = GetFree(mainType);
        if (relay == null)
        {
            PLAYER_TYPE alt = leftSide ? PLAYER_TYPE.E_SECOND_BASE : PLAYER_TYPE.E_SHORT_STOP;
            relay = GetFree(alt);
        }
        if (relay == null) return;

        Vector3 dir = ballSpot - targetBasePos;
        dir.y = 0f;
        float total = dir.magnitude;
        if (total < 0.5f) return;
        dir /= total;

        float ratio = homeCutoff ? 0.30f : DefenseTuning.RELAY_LINE_RATIO;
        Vector3 relayPos = targetBasePos + dir * (total * ratio);

        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.CUTOFF;
        a.Target = ClampToField(relayPos);
        a.FaceTarget = ballSpot;
        a.CoverBase = BASE_TYPE.E_SELF;
        a.Live = true;
        relay.SetAssignment(a);
        _assigned.Add(relay);
        CutoffMan = relay;

        // 더블 컷오프 : 아주 깊은 타구는 보조 중계맨(트레일러)을 뒤에 하나 더 세운다
        if (needRelay && total > FieldMetrics.M(78f))
        {
            PLAYER_TYPE trailType = leftSide ? PLAYER_TYPE.E_SECOND_BASE : PLAYER_TYPE.E_SHORT_STOP;
            BaseballPlayer trail = GetFree(trailType);
            if (trail != null)
            {
                FieldAssignment b = new FieldAssignment();
                b.Role = DEFENSIVE_ROLE.RELAY;      // 장거리 2차 중계(RelayMan)
                b.Target = ClampToField(relayPos + dir * FieldMetrics.M(9f));
                b.FaceTarget = ballSpot;
                b.CoverBase = BASE_TYPE.E_SELF;
                b.Live = true;
                trail.SetAssignment(b);
                _assigned.Add(trail);
            }
        }
    }

    // ------------------------------------------------------------
    //  베이스 커버
    // ------------------------------------------------------------
    static readonly PLAYER_TYPE[] _coverFirst = {
        PLAYER_TYPE.E_FIRST_BASE, PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_SHORT_STOP };
    // 1루수가 타구를 처리하러 나갔을 때의 1루 커버 : 투수가 베이스로 달려가 토스를 받는다
    // (설계도 5.1 「P-1B 토스 커버」)
    static readonly PLAYER_TYPE[] _coverFirstByPitcher = {
        PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_FIRST_BASE };
    static readonly PLAYER_TYPE[] _coverThird = {
        PLAYER_TYPE.E_THIRD_BASE, PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_SECOND_BASE };
    static readonly PLAYER_TYPE[] _coverHome = {
        PLAYER_TYPE.E_CATCHER, PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_FIRST_BASE, PLAYER_TYPE.E_THIRD_BASE };

    // 번트 수비 : 1·3루수와 투수가 차징하므로 베이스는 키스톤 콤비가 대신 커버한다(설계도 5.4)
    static readonly PLAYER_TYPE[] _buntCoverFirst = {
        PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_FIRST_BASE, PLAYER_TYPE.E_SHORT_STOP };
    static readonly PLAYER_TYPE[] _buntCoverThird = {
        PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_THIRD_BASE, PLAYER_TYPE.E_SECOND_BASE };
    // 포수가 쇄도했을 때의 홈 커버 : 투수가 최우선
    static readonly PLAYER_TYPE[] _coverHomeByPitcher = {
        PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_FIRST_BASE, PLAYER_TYPE.E_THIRD_BASE, PLAYER_TYPE.E_CATCHER };

    // 2루 커버 후보(방향별) — 매 프레임 배열을 새로 만들지 않도록 캐시해 둔다
    static readonly PLAYER_TYPE[] _coverSecondBySs = {
        PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_PITCHER };
    static readonly PLAYER_TYPE[] _coverSecondBy2b = {
        PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_PITCHER };

    void AssignBaseCoverage(Vector3 ballSpot)
    {
        List<RunnerInfo> runners = RunnerTracker.Snapshot();

        // 어떤 베이스를 지켜야 하는가 — 주자가 향하는 모든 베이스 + 1루는 항상
        for (int i = 0; i < _needBase.Length; i++) _needBase[i] = false;
        _needBase[1] = true;

        for (int i = 0; i < runners.Count; i++)
        {
            int rank = RunnerTracker.BaseRank(runners[i].TargetBase);
            if (rank >= 1 && rank <= 4) _needBase[rank] = true;
            // 진루 중인 주자가 있으면 그 다음 베이스도 견제 대상
            if (runners[i].IsRunning && rank + 1 <= 4) _needBase[rank + 1] = true;
        }

        // 플레이어(포수)가 홈을 비웠으면 무조건 누군가 홈을 지켜야 한다(설계도 5.4)
        if (DefenseCommand.IsCatcherCharging || YieldedToPlayer) _needBase[4] = true;

        // 홈에 가까운(중요한) 베이스부터 배정
        for (int rank = 4; rank >= 1; rank--)
        {
            if (!_needBase[rank]) continue;
            BASE_TYPE b = ThrowDecision.RankToBase(rank);

            // 설계도 5.5 매트릭스가 이 코스에서 이 베이스의 담당자를 지정했으면 그것을 최우선으로 따른다.
            BaseballPlayer cover = MatrixAssignee(COURSE_ACTION.COVER, b);

            if (cover == null)
            {
                PLAYER_TYPE[] pref = PreferenceFor(b, ballSpot);
                for (int i = 0; i < pref.Length; i++)
                {
                    cover = GetFree(pref[i]);
                    if (cover != null) break;
                }
            }
            // 외야수가 내야까지 달려와서 베이스 커버를 하는 기행 방지
            if (cover == null) cover = NearestFree(FieldMetrics.BasePos(b), false);
            if (cover == null) continue;

            FieldAssignment a = new FieldAssignment();
            a.Role = DEFENSIVE_ROLE.BASE_COVER;
            a.CoverBase = b;
            a.Target = BaseCoverSpot(b, ballSpot);
            a.FaceTarget = ballSpot;
            a.Live = true;
            cover.SetAssignment(a);
            _assigned.Add(cover);
            _baseCover[b] = cover;
        }
    }

    /// <summary>
    /// 베이스 커버 시 실제로 서야 하는 자리.
    ///
    ///  · 포스 플레이  : 주자의 슬라이딩 주로(베이스 중앙)를 비우고 <b>안쪽 모서리</b>에 선다(설계도 10.1).
    ///  · 태그 플레이  : 베이스 <b>바로 앞</b>에서 주자를 마주보고 스트래들한다(설계도 10.2).
    /// </summary>
    Vector3 BaseCoverSpot(BASE_TYPE b, Vector3 ballSpot)
    {
        Vector3 basePos = FieldMetrics.BasePos(b);

        RunnerInfo runner = RunnerTracker.RunnerHeadingTo(b);
        bool tagPlay = runner.IsValid && !runner.IsForced;

        if (tagPlay)
        {
            // 주자가 들어오는 방향의 베이스 앞쪽 — 미트를 지면에 고정할 자리
            BASE_TYPE prev = RundownPlay.PreviousBase(b);
            Vector3 from = (prev != BASE_TYPE.E_SELF) ? FieldMetrics.BasePos(prev) : FieldMetrics.Home;
            Vector3 dir = from - basePos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return basePos;
            return basePos + dir.normalized * FieldMetrics.M(DefenseTuning.TAG_STRADDLE_OFFSET);
        }

        // 포스 플레이 : 공이 오는 쪽(=송구 라인 쪽) 안쪽 모서리에 발을 댄다
        Vector3 toBall = ballSpot - basePos;
        toBall.y = 0f;
        if (toBall.sqrMagnitude < 0.01f) return basePos;
        return basePos + toBall.normalized * FieldMetrics.M(DefenseTuning.FORCE_OUT_EDGE_OFFSET);
    }

    PLAYER_TYPE[] PreferenceFor(BASE_TYPE b, Vector3 ballSpot)
    {
        bool bunt = BallType == BATTED_BALL_TYPE.BUNT;

        switch (b)
        {
            case BASE_TYPE.E_FIRST_BASE:
                {
                    if (bunt) return _buntCoverFirst;
                    // 1루수가 타구를 처리 중이면 투수가 1루 커버로 달려간다
                    bool firstBaseFielding = _primary != null &&
                                             _primary.Attributes.Position == PLAYER_TYPE.E_FIRST_BASE;
                    return firstBaseFielding ? _coverFirstByPitcher : _coverFirst;
                }

            case BASE_TYPE.E_THIRD_BASE:
                return bunt ? _buntCoverThird : _coverThird;

            case BASE_TYPE.E_HOME_BASE:
                // 플레이어(포수)가 홈을 비웠다면 투수가 즉시 홈 커버로 들어간다
                return (DefenseCommand.IsCatcherCharging || YieldedToPlayer) ? _coverHomeByPitcher : _coverHome;

            case BASE_TYPE.E_SECOND_BASE:
                {
                    // 2루 커버는 타구 방향으로 결정한다.
                    //   우측(1루 쪽) 타구  → 유격수가 커버 (2루수가 타구를 처리)
                    //   좌측(3루 쪽) 타구  → 2루수가 커버 (유격수가 타구를 처리)
                    bool ballToRight = FieldMetrics.SprayAngle(ballSpot) < 0f;
                    if (_primary != null)
                    {
                        if (_primary.Attributes.Position == PLAYER_TYPE.E_SECOND_BASE) ballToRight = true;
                        else if (_primary.Attributes.Position == PLAYER_TYPE.E_SHORT_STOP) ballToRight = false;
                    }
                    return ballToRight ? _coverSecondBySs : _coverSecondBy2b;
                }

            default:
                return _coverFirst;
        }
    }

    // ------------------------------------------------------------
    //  백업
    // ------------------------------------------------------------
    void AssignBackups(Vector3 ballSpot)
    {
        bool infieldPlay = _primary == null || !_primary.Attributes.IsOutfielder;

        // (a) 투수 : 외야로 나간 타구는 3루/홈 백업, 내야 타구는 1루 백업
        BaseballPlayer pitcher = GetFree(PLAYER_TYPE.E_PITCHER);
        if (pitcher != null)
        {
            BASE_TYPE backupBase;
            if (!infieldPlay)
                backupBase = (ExpectedThrowBase == BASE_TYPE.E_HOME_BASE) ? BASE_TYPE.E_HOME_BASE : BASE_TYPE.E_THIRD_BASE;
            else
                backupBase = ExpectedThrowBase;

            SetBackupBehindBase(pitcher, backupBase, ballSpot);
        }

        // (b) 남은 야수 : 설계도 5.5 매트릭스 셀을 그대로 이행한다.
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;

            CourseCell cell = HitCourseMatrix.Cell(CurrentCourse, f.Type);

            if (cell.Action == COURSE_ACTION.BACKUP)
            {
                if (cell.Target == BASE_TYPE.E_BALL)
                {
                    SetTrailBackup(f, ballSpot);          // [B-대상]이 타구 처리 지점인 셀
                }
                else
                {
                    // 1차 송구 목표(ExpectedThrowBase)가 현재 백업하려는 베이스와 일치할 때만 타구 위치(ballSpot) 기준 송구선을 그린다.
                    // 일치하지 않는다면 불필요하게 대각선으로 뛰지 않고, 홈 기준(정면 외야)에서 수비 베이스 후방을 커버한다.
                    Vector3 backupOrigin = (ExpectedThrowBase == cell.Target || ExpectedThrowBase == BASE_TYPE.E_HOME_BASE) ? ballSpot : FieldMetrics.Home;
                    SetBackupBehindBase(f, cell.Target, backupOrigin);
                }
                continue;
            }

            if (cell.Action == COURSE_ACTION.OBSERVE)
            {
                SetObserver(f, ballSpot);                 // [O] 제자리에서 2차 바운드/주자 주시
                continue;
            }

            // 매트릭스에 셀이 없거나(주자 상황 등으로 다른 배정이 이미 나간 경우)
            // 코스 판별이 되지 않은 경우 — 기존 동적 로직으로 처리한다.
            if (f.Attributes.IsOutfielder)
            {
                BASE_TYPE fixedBase = FixedOutfieldBackup(f.Type, infieldPlay, ballSpot, ExpectedThrowBase);
                if (fixedBase != BASE_TYPE.E_SELF)
                {
                    Vector3 backupOrigin = (ExpectedThrowBase == fixedBase || ExpectedThrowBase == BASE_TYPE.E_HOME_BASE) ? ballSpot : FieldMetrics.Home;
                    SetBackupBehindBase(f, fixedBase, backupOrigin);
                    continue;
                }
                SetTrailBackup(f, ballSpot);
            }
            else
            {
                // 포수의 1루 송구 백업 예외
                if (f.Type == PLAYER_TYPE.E_CATCHER && ExpectedThrowBase == BASE_TYPE.E_FIRST_BASE)
                {
                    SetBackupBehindBase(f, BASE_TYPE.E_FIRST_BASE, ballSpot);
                    continue;
                }
                
                // 나머지 내야수들은 무작정 송구 베이스 뒤로 뛰지 않고 제자리를 지키며 상황을 주시한다
                SetObserver(f, ballSpot);
            }
        }
    }

    /// <summary>타구 처리 지점 뒤쪽(외야 방향 연장선)을 받치는 트레일 백업.</summary>
    void SetTrailBackup(BaseballPlayer f, Vector3 ballSpot)
    {
        Vector3 outward = ballSpot - FieldMetrics.Home;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f) outward = FieldMetrics.Forward;
        outward.Normalize();

        Vector3 target = ballSpot + outward * FieldMetrics.M(DefenseTuning.TRAIL_BACKUP_DISTANCE);
        if (f.Attributes.IsOutfielder)
        {
            float minDist = FieldMetrics.M(40f);
            if (FieldMetrics.FlatDistance(target, FieldMetrics.Home) < minDist)
            {
                target = FieldMetrics.Home + outward * minDist;
            }
        }

        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.BACKUP;
        a.Target = ClampToField(target);
        a.FaceTarget = ballSpot;
        a.CoverBase = BASE_TYPE.E_SELF;
        a.Live = true;
        f.SetAssignment(a);
        _assigned.Add(f);
    }

    /// <summary>[O] 제자리 주시 — 2차 바운드와 주자 움직임을 살핀다(설계도 5.5 표기 규칙).</summary>
    void SetObserver(BaseballPlayer f, Vector3 ballSpot)
    {
        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.OBSERVER;
        a.Target = f.HomePosition;
        a.FaceTarget = ballSpot;
        a.CoverBase = BASE_TYPE.E_SELF;
        a.Live = true;
        f.SetAssignment(a);
        _assigned.Add(f);
    }

    /// <summary>
    /// 설계도 5.5 매트릭스에서 현재 코스의 지정 담당자를 찾는다.
    /// 이미 다른 역할로 배정된 야수는 제외한다.
    /// </summary>
    BaseballPlayer MatrixAssignee(COURSE_ACTION action, BASE_TYPE target)
    {
        if (CurrentCourse == HIT_COURSE.NONE) return null;

        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;

            CourseCell c = HitCourseMatrix.Cell(CurrentCourse, f.Type);
            if (c.Action == action && c.Target == target) return f;
        }
        return null;
    }

    /// <summary>
    /// 설계도 5.5-A : 내야 땅볼일 때 외야수의 고정 백업 담당.
    /// 실제 야구 고증에 따라, 2루 송구 방향에 맞춰 코너 외야수(좌/우익수)도 이중 백업을 들어간다.
    /// </summary>
    static BASE_TYPE FixedOutfieldBackup(PLAYER_TYPE t, bool infieldPlay, Vector3 ballSpot, BASE_TYPE expectedBase)
    {
        if (!infieldPlay) return BASE_TYPE.E_SELF;

        if (expectedBase == BASE_TYPE.E_SECOND_BASE)
        {
            // 우측 내야(1,2루수)에서 2루 송구 시 궤적이 좌익수 쪽으로 향한다.
            // -> 좌익수가 1차 백업(15m), 중견수가 2차 깊은 백업(25m)을 맡는다.
            if (ballSpot.x > 0f)
            {
                if (t == PLAYER_TYPE.E_LEFT_FILED) return BASE_TYPE.E_SECOND_BASE;
                if (t == PLAYER_TYPE.E_CENTER_FIELD) return BASE_TYPE.E_SECOND_BASE;
            }
            // 좌측 내야(3루수,유격수)에서 2루 송구 시 궤적이 우익수 쪽으로 향한다.
            // -> 우익수가 1차 백업(15m), 중견수가 2차 깊은 백업(25m)을 맡는다.
            else
            {
                if (t == PLAYER_TYPE.E_RIGHT_FILED) return BASE_TYPE.E_SECOND_BASE;
                if (t == PLAYER_TYPE.E_CENTER_FIELD) return BASE_TYPE.E_SECOND_BASE;
            }
        }

        switch (t)
        {
            case PLAYER_TYPE.E_RIGHT_FILED: return BASE_TYPE.E_FIRST_BASE;
            case PLAYER_TYPE.E_LEFT_FILED: return BASE_TYPE.E_THIRD_BASE;
            case PLAYER_TYPE.E_CENTER_FIELD: return expectedBase == BASE_TYPE.E_SECOND_BASE ? BASE_TYPE.E_SECOND_BASE : BASE_TYPE.E_SELF;
            default: return BASE_TYPE.E_SELF;
        }
    }

    /// <summary>
    /// 설계도 7.2 백업 기하학.
    ///   V_throw       = P_receiver - P_fielder
    ///   P_backupTarget = P_receiver + normalize(V_throw) * BackupDistance
    /// BackupDistance 는 12~18m, 허용 각도는 송구선 연장선 기준 ±15°.
    /// </summary>
    void SetBackupBehindBase(BaseballPlayer f, BASE_TYPE b, Vector3 ballSpot)
    {
        Vector3 basePos = FieldMetrics.BasePos(b);
        Vector3 dir = basePos - ballSpot;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = FieldMetrics.Forward;
        dir.Normalize();

        float backupDist = FieldMetrics.M(DefenseTuning.BACKUP_BEHIND_BASE);
        bool isOutfielder = f.Attributes.IsOutfielder;
        
        if (isOutfielder)
        {
            // 외야수는 내야수와 동선이 겹치지 않게 기본 10m 더 뒤(2차 백업 라인)에 선다.
            backupDist += FieldMetrics.M(10f);
        }

        // 2루 이중 백업 시, 궤적상 더 먼 중견수는 코너 외야수보다 10m 더 뒤로 물러나 3차 백업 라인을 구축한다.
        bool isCenterDeepBackup = b == BASE_TYPE.E_SECOND_BASE && f.Type == PLAYER_TYPE.E_CENTER_FIELD;
        if (isCenterDeepBackup)
        {
            backupDist += FieldMetrics.M(10f);
        }

        Vector3 ideal = basePos + dir * backupDist;

        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.BACKUP;
        
        if (isOutfielder)
        {
            // 깊은 백업(외야수)은 ClampToBackupCone 거리 제한(12~18m)에 묶이지 않고 필드 끝까지 허용된다.
            a.Target = ClampToField(ideal);
        }
        else
        {
            a.Target = ClampToBackupCone(basePos, dir, ClampToField(ideal));
        }
        
        a.FaceTarget = ballSpot;
        a.CoverBase = BASE_TYPE.E_SELF;
        a.Live = true;
        f.SetAssignment(a);
        _assigned.Add(f);
    }

    // ------------------------------------------------------------
    //  보조
    // ------------------------------------------------------------

    /// <summary>
    /// 땅볼을 잡을 때 몸이 이미 송구 방향을 향하도록 살짝 우회(라운딩)시킨다.
    /// 정면으로 달려들어 잡으면 잡은 뒤 몸을 돌려야 해서 한 박자 늦는다.
    /// </summary>
    Vector3 ApplyRoutingBias(BaseballPlayer f, Vector3 target, InterceptResult ir)
    {
        if (ir.IsAirCatch) return target;
        if (BallType == BATTED_BALL_TYPE.POP_FLY || BallType == BATTED_BALL_TYPE.FLY_BALL) return target;
        if (ir.Effort == CATCH_EFFORT.DIVE || ir.Effort == CATCH_EFFORT.HUSTLE) return target;
        if (ir.Slack < 0.25f) return target;      // 여유가 없으면 최단 경로로

        Vector3 basePos = FieldMetrics.BasePos(ExpectedThrowBase);
        Vector3 toBase = basePos - target;
        toBase.y = 0f;
        if (toBase.sqrMagnitude < 0.01f) return target;
        toBase.Normalize();

        // 송구 방향의 반대쪽으로 살짝 돌아 들어가서, 잡는 순간 몸이 베이스를 향하게 한다.
        float amount = Mathf.Min(FieldMetrics.M(DefenseTuning.GROUNDER_ROUNDING_MAX),
                                 ir.Slack * FieldMetrics.M(DefenseTuning.GROUNDER_ROUNDING_WEIGHT) * 6f);
        Vector3 biased = target - toBase * amount;
        biased.y = FieldMetrics.GroundY;
        return biased;
    }

    BASE_TYPE PredictThrowBase(Vector3 ballSpot)
    {
        // 사용자가 베이스를 지정했다면 커버·백업 대형도 그 지시에 맞춘다.
        BASE_TYPE commanded = DefenseCommand.UserCommandedBase;
        if (commanded != BASE_TYPE.E_SELF) return commanded;

        List<RunnerInfo> runners = RunnerTracker.Snapshot();
        if (runners.Count == 0) return BASE_TYPE.E_FIRST_BASE;

        bool outfieldHit = FieldMetrics.DistFromHome(ballSpot) > FieldMetrics.M(48f);

        // 포스 상황에서 가장 앞선(홈에 가까운) 베이스를 우선 목표로
        int bestRank = 0;
        BASE_TYPE best = BASE_TYPE.E_FIRST_BASE;
        for (int i = 0; i < runners.Count; i++)
        {
            if (runners[i].IsSafe) continue;
            if (!outfieldHit && !runners[i].IsForced) continue;
            int rank = RunnerTracker.BaseRank(runners[i].TargetBase);
            if (rank > bestRank) { bestRank = rank; best = runners[i].TargetBase; }
        }

        if (bestRank == 0) return outfieldHit ? BASE_TYPE.E_SECOND_BASE : BASE_TYPE.E_FIRST_BASE;
        return best;
    }

    void CollectFielders()
    {
        _fielders.Clear();
        _byType.Clear();
        if (_team == null) return;

        for (int i = 0; i < _team.PlayerCount; i++)
        {
            BaseballPlayer p = _team.GetPlayerAt(i);
            if (p == null || !p.gameObject.activeSelf) continue;
            if (!(p.GetRole() is Defencer)) continue;
            _fielders.Add(p);
            if (!_byType.ContainsKey(p.Type)) _byType[p.Type] = p;
        }
    }

    BaseballPlayer GetFree(PLAYER_TYPE t)
    {
        BaseballPlayer p;
        if (!_byType.TryGetValue(t, out p)) return null;
        if (p == null || _assigned.Contains(p)) return null;
        return p;
    }

    BaseballPlayer NearestFree(Vector3 pos, bool includeOutfielders = true)
    {
        BaseballPlayer best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;
            if (!includeOutfielders && f.Attributes.IsOutfielder) continue;
            float d = FieldMetrics.FlatDistance(f.transform.position, pos);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    /// <summary>
    /// 백업 지점을 송구선 연장선 기준 ±15° 부채꼴, 12~18m 구간 안으로 되돌린다(설계도 7.2).
    /// 펜스 클램프 등으로 라인에서 벗어났을 때 다시 유효 범위로 투영한다.
    /// </summary>
    Vector3 ClampToBackupCone(Vector3 receiverPos, Vector3 lineDir, Vector3 candidate)
    {
        Vector3 rel = candidate - receiverPos;
        rel.y = 0f;

        float dist = rel.magnitude;
        float minD = FieldMetrics.M(DefenseTuning.BACKUP_DISTANCE_MIN);
        float maxD = FieldMetrics.M(DefenseTuning.BACKUP_DISTANCE_MAX);

        if (dist < 0.01f) return receiverPos + lineDir * minD;

        Vector3 dirNow = rel / dist;
        float angle = Vector3.Angle(lineDir, dirNow);

        // 허용 각도를 벗어나면 연장선 쪽으로 회전시켜 되돌린다
        if (angle > DefenseTuning.BACKUP_TOLERANCE_ANGLE)
        {
            float t = DefenseTuning.BACKUP_TOLERANCE_ANGLE / Mathf.Max(0.01f, angle);
            dirNow = Vector3.Slerp(lineDir, dirNow, t).normalized;
        }

        float clamped = Mathf.Clamp(dist, minD, maxD);
        Vector3 result = receiverPos + dirNow * clamped;
        result.y = FieldMetrics.GroundY;
        return result;
    }

    /// <summary>백업 위치가 펜스 밖으로 나가지 않도록 클램프.</summary>
    Vector3 ClampToField(Vector3 p)
    {
        Vector3 d = p - FieldMetrics.Home;
        d.y = 0f;
        float max = FieldMetrics.M(DefenseTuning.OUTFIELD_FENCE_DISTANCE);
        if (d.magnitude > max) d = d.normalized * max;
        Vector3 r = FieldMetrics.Home + d;
        r.y = FieldMetrics.GroundY;
        return r;
    }

    /// <summary>특정 베이스를 커버하도록 배정된 야수. 없으면 가장 가까운 야수.</summary>
    public BaseballPlayer GetBaseCoverFielder(BASE_TYPE b, BaseballPlayer exclude)
    {
        BaseballPlayer p;
        if (_baseCover.TryGetValue(b, out p) && p != null && p != exclude) return p;

        CollectFielders();
        Vector3 basePos = FieldMetrics.BasePos(b);
        BaseballPlayer best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            if (_fielders[i] == exclude) continue;
            float d = FieldMetrics.FlatDistance(_fielders[i].transform.position, basePos);
            if (d < bestD) { bestD = d; best = _fielders[i]; }
        }
        return best;
    }

    /// <summary>공 위치 → 목표 지점 사이에 있는 중계 가능한 야수.</summary>
    public BaseballPlayer GetRelayFielder(Vector3 from, Vector3 to, BaseballPlayer exclude)
    {
        CollectFielders();

        Vector3 line = to - from;
        line.y = 0f;
        float total = line.magnitude;
        if (total < 1f) return null;
        line /= total;

        BaseballPlayer best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer p = _fielders[i];
            if (p == null || p == exclude) continue;

            Vector3 rel = p.transform.position - from;
            rel.y = 0f;
            float along = Vector3.Dot(rel, line);
            if (along < total * 0.12f || along > total * 0.88f) continue;

            float off = Vector3.Cross(line, rel).magnitude;
            float score = off + Mathf.Abs(along - total * 0.45f) * 0.4f;
            if (score < bestScore) { bestScore = score; best = p; }
        }

        return best;
    }

    /// <summary>가장 가까운 내야수(투수 제외). 외야 → 투수 반환 중계에 쓴다.</summary>
    public BaseballPlayer GetNearestInfielder(Vector3 from, BaseballPlayer exclude)
    {
        CollectFielders();

        BaseballPlayer best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer p = _fielders[i];
            if (p == null || p == exclude) continue;
            if (!p.Attributes.IsInfielder) continue;
            float d = FieldMetrics.FlatDistance(p.transform.position, from);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }
}
