using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 팀 단위 수비 조율자.
///
/// 기존 구조에서는 Team.AssignDefensiveRoles 가 타구 순간 한 번만 돌고,
/// 실제 판단은 각 포지션 클래스의 거대한 if-else(AssignSpecialRole)에 흩어져 있었다.
/// 그래서 (1) 타구가 튀거나 예측이 바뀌어도 역할이 고정되고,
/// (2) "누가 부르는가(call-off)" 개념이 없었으며,
/// (3) 중계·백업 위치가 상황과 무관한 상수였다.
///
/// 이 클래스는 공이 살아있는 동안 주기적으로 다시 판단하며 다음을 배정한다.
///   PRIMARY   : 타구를 처리할 야수 (도달 시간 + 포지션 콜 우선순위)
///   SECONDARY : 뒤를 받치는 트레일 백업
///   CUTOFF    : 송구 라인 위의 중계맨 (외야 → 베이스)
///   BASE_COVER: 각 베이스를 지키는 야수 (상황별 담당 테이블)
///   BACKUP    : 악송구 대비 베이스 뒤쪽 백업
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

    public BaseballPlayer Primary { get { return _primary; } }
    public BASE_TYPE ExpectedThrowBase { get; private set; }
    public BATTED_BALL_TYPE BallType { get; private set; }
    public bool Live { get; private set; }

    float _lastEvalTime = -99f;
    float _liveStartTime;

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
        _liveStartTime = Time.time;
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
        ExpectedThrowBase = targetBase;
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
            SelectPrimary(results, force);
        }
        else
        {
            for (int i = 0; i < _fielders.Count; i++)
                results[i] = InterceptResult.None(_fielders[i].transform.position);
        }

        Vector3 ballSpot = _useHint ? _ballSpotHint :
                           (_primary != null ? _primary.transform.position :
                           (traj != null && traj.IsValid ? traj.LandPosition : FieldMetrics.Home));
        if (_primary != null && !_useHint)
        {
            int pi = _fielders.IndexOf(_primary);
            if (pi >= 0 && results[pi].CanReach) ballSpot = results[pi].Point;
            else if (pi >= 0) ballSpot = results[pi].PursuitPoint;
        }

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

        // ── 8. 남은 야수는 원위치 대기 ───────────────────────────
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;
            FieldAssignment a = FieldAssignment.Idle(f.HomePosition);
            a.FaceTarget = ballSpot;
            a.Live = true;
            f.SetAssignment(a);
        }
    }

    // ------------------------------------------------------------
    //  1차 처리자 선정 (콜 우선순위 + 히스테리시스)
    // ------------------------------------------------------------
    void SelectPrimary(InterceptResult[] results, bool force)
    {
        // (a) 도달 가능한 야수들 중 가장 이른 처리 시점
        float bestTime = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            if (!results[i].CanReach) continue;
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

                int pr = _fielders[i].Attributes.CallPriority;
                float q = results[i].Quality;
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
                float gap = results[i].MyTime - results[i].BallTime;
                if (results[i].SampleIndex < 0)
                {
                    gap = FieldMetrics.FlatDistance(_fielders[i].transform.position, results[i].PursuitPoint);
                }
                if (gap < bestGap) { bestGap = gap; candidate = _fielders[i]; }
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
            if (f.Attributes.Position == PLAYER_TYPE.E_PITCHER) continue;
            if (f.Attributes.Position == PLAYER_TYPE.E_CATCHER) continue;

            // 처리 지점보다 바깥(외야 쪽)에 있는 야수가 트레일에 적합
            float depthMine = Vector3.Dot(f.transform.position - FieldMetrics.Home, outward);
            float depthBall = Vector3.Dot(ballSpot - FieldMetrics.Home, outward);
            if (depthMine < depthBall - FieldMetrics.M(4f)) continue;

            float d = FieldMetrics.FlatDistance(f.transform.position, ballSpot);
            if (d < best) { best = d; _secondary = f; }
        }

        if (_secondary == null) return;

        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.BACKUP;
        a.IsSecondary = true;
        a.Target = ClampToField(ballSpot + outward * FieldMetrics.M(DefenseTuning.TRAIL_BACKUP_DISTANCE));
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

        Vector3 targetBasePos = FieldMetrics.BasePos(ExpectedThrowBase);
        float throwDist = FieldMetrics.FlatDistance(ballSpot, targetBasePos);

        bool primaryIsOF = _primary.Attributes.IsOutfielder;
        bool needRelay = primaryIsOF &&
                         (throwDist > FieldMetrics.M(DefenseTuning.RELAY_TRIGGER_DISTANCE) ||
                          throwDist > _primary.Attributes.MaxThrowRange * 0.92f);

        // 홈 송구는 거리와 무관하게 항상 중계맨을 세운다(실제 야구의 컷오프맨)
        bool homeCutoff = primaryIsOF && ExpectedThrowBase == BASE_TYPE.E_HOME_BASE;

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

        BaseballPlayer relay = GetFree(mainType);
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

        // 더블 컷오프 : 아주 깊은 타구는 보조 중계맨을 뒤에 하나 더 세운다
        if (needRelay && total > FieldMetrics.M(78f))
        {
            PLAYER_TYPE trailType = leftSide ? PLAYER_TYPE.E_SECOND_BASE : PLAYER_TYPE.E_SHORT_STOP;
            BaseballPlayer trail = GetFree(trailType);
            if (trail != null)
            {
                FieldAssignment b = new FieldAssignment();
                b.Role = DEFENSIVE_ROLE.CUTOFF;
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
    static readonly PLAYER_TYPE[] _coverThird = {
        PLAYER_TYPE.E_THIRD_BASE, PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_SECOND_BASE };
    static readonly PLAYER_TYPE[] _coverHome = {
        PLAYER_TYPE.E_CATCHER, PLAYER_TYPE.E_PITCHER, PLAYER_TYPE.E_FIRST_BASE, PLAYER_TYPE.E_THIRD_BASE };

    void AssignBaseCoverage(Vector3 ballSpot)
    {
        List<RunnerInfo> runners = RunnerTracker.Snapshot();

        // 어떤 베이스를 지켜야 하는가 — 주자가 향하는 모든 베이스 + 1루는 항상
        bool[] need = new bool[5]; // index = BaseRank
        need[1] = true;
        for (int i = 0; i < runners.Count; i++)
        {
            int rank = RunnerTracker.BaseRank(runners[i].TargetBase);
            if (rank >= 1 && rank <= 4) need[rank] = true;
            // 진루 중인 주자가 있으면 그 다음 베이스도 견제 대상
            if (runners[i].IsRunning && rank + 1 <= 4) need[rank + 1] = true;
        }

        // 홈에 가까운(중요한) 베이스부터 배정
        for (int rank = 4; rank >= 1; rank--)
        {
            if (!need[rank]) continue;
            BASE_TYPE b = ThrowDecision.RankToBase(rank);

            PLAYER_TYPE[] pref = PreferenceFor(b, ballSpot);
            BaseballPlayer cover = null;
            for (int i = 0; i < pref.Length; i++)
            {
                cover = GetFree(pref[i]);
                if (cover != null) break;
            }
            if (cover == null) cover = NearestFree(FieldMetrics.BasePos(b));
            if (cover == null) continue;

            FieldAssignment a = new FieldAssignment();
            a.Role = DEFENSIVE_ROLE.BASE_COVER;
            a.CoverBase = b;
            a.Target = FieldMetrics.BasePos(b);
            a.FaceTarget = ballSpot;
            a.Live = true;
            cover.SetAssignment(a);
            _assigned.Add(cover);
            _baseCover[b] = cover;
        }
    }

    PLAYER_TYPE[] PreferenceFor(BASE_TYPE b, Vector3 ballSpot)
    {
        switch (b)
        {
            case BASE_TYPE.E_FIRST_BASE: return _coverFirst;
            case BASE_TYPE.E_THIRD_BASE: return _coverThird;
            case BASE_TYPE.E_HOME_BASE: return _coverHome;
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
                    return ballToRight
                        ? new PLAYER_TYPE[] { PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_PITCHER }
                        : new PLAYER_TYPE[] { PLAYER_TYPE.E_SECOND_BASE, PLAYER_TYPE.E_SHORT_STOP, PLAYER_TYPE.E_PITCHER };
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
        Vector3 targetBasePos = FieldMetrics.BasePos(ExpectedThrowBase);

        // (a) 투수 : 외야로 나간 타구는 3루/홈 백업, 내야 타구는 1루 백업
        BaseballPlayer pitcher = GetFree(PLAYER_TYPE.E_PITCHER);
        if (pitcher != null)
        {
            BASE_TYPE backupBase;
            if (_primary != null && _primary.Attributes.IsOutfielder)
                backupBase = (ExpectedThrowBase == BASE_TYPE.E_HOME_BASE) ? BASE_TYPE.E_HOME_BASE : BASE_TYPE.E_THIRD_BASE;
            else
                backupBase = ExpectedThrowBase;

            SetBackupBehindBase(pitcher, backupBase, ballSpot);
        }

        // (b) 남은 야수 : 송구 목표 베이스 뒤쪽 백업
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;

            if (f.Attributes.IsOutfielder)
            {
                // 외야수는 타구 처리자 뒤쪽 또는 자기 방향의 베이스 뒤로
                Vector3 outward = ballSpot - FieldMetrics.Home;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.01f) outward = FieldMetrics.Forward;
                outward.Normalize();

                FieldAssignment a = new FieldAssignment();
                a.Role = DEFENSIVE_ROLE.BACKUP;
                a.Target = ClampToField(Vector3.Lerp(f.transform.position, ballSpot + outward * FieldMetrics.M(14f), 0.65f));
                a.FaceTarget = ballSpot;
                a.CoverBase = BASE_TYPE.E_SELF;
                a.Live = true;
                f.SetAssignment(a);
                _assigned.Add(f);
            }
            else
            {
                SetBackupBehindBase(f, ExpectedThrowBase, ballSpot);
            }
        }
    }

    void SetBackupBehindBase(BaseballPlayer f, BASE_TYPE b, Vector3 ballSpot)
    {
        Vector3 basePos = FieldMetrics.BasePos(b);
        Vector3 dir = basePos - ballSpot;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = FieldMetrics.Forward;
        dir.Normalize();

        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.BACKUP;
        a.Target = ClampToField(basePos + dir * FieldMetrics.M(DefenseTuning.BACKUP_BEHIND_BASE));
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

    BaseballPlayer NearestFree(Vector3 pos)
    {
        BaseballPlayer best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < _fielders.Count; i++)
        {
            BaseballPlayer f = _fielders[i];
            if (_assigned.Contains(f)) continue;
            float d = FieldMetrics.FlatDistance(f.transform.position, pos);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
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
}
