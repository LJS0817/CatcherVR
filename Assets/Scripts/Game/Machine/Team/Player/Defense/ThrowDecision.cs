using System.Collections.Generic;
using UnityEngine;

public enum THROW_KIND
{
    NONE,
    HOLD,          // 던질 곳이 없음 — 공을 쥐고 플레이 종료
    TO_BASE,       // 베이스로 송구(포스/견제)
    RELAY,         // 중계맨에게 송구
    TAG_RUNNER,    // 눈앞의 주자를 직접 태그
    RUN_TO_BASE,   // 던지지 않고 직접 베이스를 밟으러 감
    TO_PITCHER     // 투수에게 반환
}

public struct ThrowPlan
{
    public THROW_KIND Kind;
    public BASE_TYPE TargetBase;
    public BaseballPlayer Receiver;
    public Vector3 AimPoint;
    public float FlightTime;
    public RunnerInfo TargetRunner;
    /// <summary>이 송구가 병살의 첫 번째 단계인가.</summary>
    public bool StartsDoublePlay;
    /// <summary>아웃 여유(초). 클수록 확실한 아웃.</summary>
    public float OutMargin;
    /// <summary>사용자가 지정한 베이스로 강제 송구하는 중인가.</summary>
    public bool IsUserCommanded;
    /// <summary>플레이 종료 후 투수에게 공을 돌려보내는 송구인가(설계도 12.2).</summary>
    public bool IsReturnThrow;
    /// <summary>송구 구속 상한(실측 m/s). 0이면 능력치의 최대 구속을 쓴다(반환 송구용).</summary>
    public float SpeedOverrideRaw;

    public static ThrowPlan Hold()
    {
        ThrowPlan p = new ThrowPlan();
        p.Kind = THROW_KIND.HOLD;
        p.TargetBase = BASE_TYPE.E_SELF;
        return p;
    }
}

/// <summary>
/// "지금 어디로 던져야 아웃을 잡는가" 를 계산한다.
///
/// 판단 순서 (설계도 6.1 의사결정 순서도)
///   0) 사용자가 베이스를 지정했는가?  →  <b>AI 자체 판단을 무시하고 강제 송구</b>
///   1) 눈앞에 태그할 수 있는 주자가 있는가?
///   2) 각 베이스별 「송구 도착 시간 vs 주자 ETA」 비교
///   3) 내가 직접 밟는 편이 빠른가?
///   4) 병살 연결이 가능한가?
///   5) 확실한 선행 아웃 &gt; 확실한 후행 아웃
///   6) 아웃이 불가능하면 진루 억제 송구, 그마저 늦으면 투수 반환(플레이 종료)
/// </summary>
public static class ThrowDecision
{
    /// <summary>병살 피벗(수구 → 재송구)에 걸리는 시간(초).</summary>
    public const float PIVOT_TIME = 0.62f;

    struct Candidate
    {
        public BASE_TYPE Base;
        public RunnerInfo Runner;
        public BaseballPlayer Receiver;
        public float ArriveTime;
        public float Margin;
        public float ThrowDistance;
        public bool Viable;
    }

    static readonly List<Candidate> _cands = new List<Candidate>(4);

    /// <summary>
    /// 최선의 후속 플레이를 결정한다.
    /// </summary>
    /// <param name="me">공을 쥔 야수</param>
    /// <param name="attr">그 야수의 능력치</param>
    /// <param name="ballPos">공(글러브)의 현재 위치</param>
    /// <param name="myTeam">수비 팀</param>
    /// <param name="extraDelay">포구 자세에 따른 추가 지연(초)</param>
    public static ThrowPlan Decide(BaseballPlayer me, FielderAttributes attr, Vector3 ballPos, Team myTeam, float extraDelay)
    {
        ThrowPlan plan = ThrowPlan.Hold();
        if (me == null || attr == null || myTeam == null) return plan;

        // 방금 아웃된 주자가 목록에 남지 않도록 항상 새로 수집한다.
        List<RunnerInfo> runners = RunnerTracker.Snapshot(true);

        float releaseDelay = attr.ReleaseTime + Mathf.Max(0f, extraDelay);

        // ══════════════════════════════════════════════════════════
        //  0. 사용자 지정 베이스 (최우선 오버라이드) — 설계도 6.2
        // ══════════════════════════════════════════════════════════
        BASE_TYPE commanded = DefenseCommand.UserCommandedBase;
        if (commanded != BASE_TYPE.E_SELF)
        {
            ThrowPlan forced;
            if (TryUserCommandedThrow(me, attr, ballPos, myTeam, commanded, releaseDelay, out forced))
            {
                return forced;
            }
            // 지정 베이스가 물리적으로 불가능한 경우(사거리 초과 등)에만 자체 판단으로 내려간다.
        }

        if (runners.Count == 0)
        {
            plan.Kind = THROW_KIND.TO_PITCHER;
            return plan;
        }

        // ── 1. 눈앞에 태그할 수 있는 주자가 있으면 그게 최우선 ──
        for (int i = 0; i < runners.Count; i++)
        {
            if (RunnerTracker.IsTaggable(runners[i], me.transform.position))
            {
                plan.Kind = THROW_KIND.TAG_RUNNER;
                plan.TargetRunner = runners[i];
                plan.TargetBase = runners[i].TargetBase;
                return plan;
            }
        }

        // ── 2. 각 베이스별로 송구 아웃 가능성 평가 ──
        _cands.Clear();
        for (int rank = 4; rank >= 1; rank--)
        {
            BASE_TYPE b = RankToBase(rank);
            RunnerInfo runner = RunnerTracker.RunnerHeadingTo(b);
            if (!runner.IsValid) continue;
            if (runner.IsSafe) continue;              // 이미 안착 — 던져도 소용 없음
            if (!runner.IsForced) continue;           // 포스가 아니면 베이스 터치로 아웃 안 됨

            Candidate c;
            if (!BuildCandidate(me, attr, ballPos, myTeam, b, runner, releaseDelay, out c)) continue;
            _cands.Add(c);
        }

        // ── 3. 내가 직접 베이스를 밟는 편이 빠른가 ──
        for (int i = 0; i < _cands.Count; i++)
        {
            Candidate c = _cands[i];
            Vector3 basePos = FieldMetrics.BasePos(c.Base);
            float myDist = FieldMetrics.FlatDistance(me.transform.position, basePos);
            if (myDist > FieldMetrics.M(DefenseTuning.RUN_TO_BASE_MAX_DIST)) continue;

            float myTime = attr.TravelTime(Mathf.Max(0f, myDist - FieldMetrics.M(DefenseTuning.BASE_TOUCH_RANGE)),
                                           1f, false);
            if (myTime < c.Runner.Eta - 0.05f && (!c.Viable || myTime < c.ArriveTime))
            {
                plan.Kind = THROW_KIND.RUN_TO_BASE;
                plan.TargetBase = c.Base;
                plan.TargetRunner = c.Runner;
                plan.AimPoint = basePos;
                plan.OutMargin = c.Runner.Eta - myTime;
                return plan;
            }
        }

        // ── 4. 병살 판단 : 2루(또는 3루) 포스를 잡고 1루로 이어지는가 ──
        Candidate bestDp = default(Candidate);
        bool hasDp = false;
        for (int i = 0; i < _cands.Count; i++)
        {
            Candidate lead = _cands[i];
            if (!lead.Viable) continue;
            if (RunnerTracker.BaseRank(lead.Base) < 2) continue;   // 1루는 병살의 시작이 될 수 없음

            // 선행 아웃 이후 1루 후속 송구 가능성
            Candidate first = FindCandidate(BASE_TYPE.E_FIRST_BASE);
            if (first.Base != BASE_TYPE.E_FIRST_BASE) continue;

            Vector3 leadBase = FieldMetrics.BasePos(lead.Base);
            Vector3 firstBase = FieldMetrics.BasePos(BASE_TYPE.E_FIRST_BASE);
            float relayDist = FieldMetrics.FlatDistance(leadBase, firstBase);

            FielderAttributes pivotAttr = lead.Receiver != null ? lead.Receiver.Attributes : attr;
            float dpArrive = lead.ArriveTime + PIVOT_TIME + pivotAttr.ThrowFlightTime(relayDist);

            if (first.Runner.Eta - dpArrive > 0.02f)
            {
                if (!hasDp || lead.Margin > bestDp.Margin) { bestDp = lead; hasDp = true; }
            }
        }

        if (hasDp)
        {
            plan = MakeThrow(bestDp, myTeam);
            plan.StartsDoublePlay = true;
            return plan;
        }

        // ── 5. 병살이 안 되면 : 확실한 선행 아웃 > 확실한 후행 아웃 ──
        Candidate best = default(Candidate);
        bool found = false;
        for (int rank = 4; rank >= 1; rank--)
        {
            for (int i = 0; i < _cands.Count; i++)
            {
                if (RunnerTracker.BaseRank(_cands[i].Base) != rank) continue;
                if (!_cands[i].Viable) continue;
                if (_cands[i].Margin < 0.12f && rank > 1) continue;   // 아슬아슬한 선행 아웃은 포기
                best = _cands[i];
                found = true;
                break;
            }
            if (found) break;
        }

        if (!found)
        {
            // 여유는 없지만 그나마 가능성 있는 곳
            float bestMargin = float.NegativeInfinity;
            for (int i = 0; i < _cands.Count; i++)
            {
                if (!_cands[i].Viable) continue;
                if (_cands[i].Margin > bestMargin) { bestMargin = _cands[i].Margin; best = _cands[i]; found = true; }
            }
        }

        if (found) return MakeThrow(best, myTeam);

        // ── 6. 아웃을 잡을 수 없음 : 진루 억제 송구 또는 투수 반환 ──
        //     단, 이미 크게 늦은 상황에서 던지면 악송구·추가 진루만 부르므로
        //     설계도 12.1 「더 이상 아웃 불가」 기준(-0.5s)을 넘으면 던지지 않는다.
        RunnerInfo lead2 = RunnerTracker.LeadRunner();
        if (lead2.IsValid && !lead2.IsSafe && lead2.IsRunning)
        {
            BASE_TYPE ahead = lead2.TargetBase;
            Vector3 aheadPos = FieldMetrics.BasePos(ahead);
            float d = FieldMetrics.FlatDistance(ballPos, aheadPos);
            if (d <= attr.MaxThrowRange && d > FieldMetrics.M(2f))
            {
                float arrive = releaseDelay + attr.ThrowFlightTime(d);
                float margin = lead2.Eta - arrive;
                if (margin > DefenseTuning.SUPPRESS_THROW_MIN_MARGIN)
                {
                    Candidate c = new Candidate();
                    c.Base = ahead;
                    c.Runner = lead2;
                    c.Receiver = myTeam.GetBaseCoverFielder(ahead, me);
                    c.ThrowDistance = d;
                    c.ArriveTime = arrive;
                    c.Margin = margin;
                    return MakeThrow(c, myTeam);
                }
            }
        }

        plan.Kind = THROW_KIND.TO_PITCHER;
        return plan;
    }

    // ============================================================
    //  사용자 지정 송구 (User Override)
    // ============================================================

    /// <summary>
    /// 사용자가 지정한 베이스로 강제 송구 계획을 만든다.
    /// 커버 요원이 아직 베이스에 도착하지 못했더라도 <b>송구를 멈추지 않고</b>
    /// 베이스(=커버 요원의 리드 위치)를 겨냥해 유도 송구한다(설계도 6.2 IMPORTANT).
    /// </summary>
    static bool TryUserCommandedThrow(BaseballPlayer me, FielderAttributes attr, Vector3 ballPos,
                                      Team myTeam, BASE_TYPE commanded, float releaseDelay, out ThrowPlan plan)
    {
        plan = ThrowPlan.Hold();

        Vector3 basePos = FieldMetrics.BasePos(commanded);
        float dist = FieldMetrics.FlatDistance(ballPos, basePos);

        // 홈 지정인데 플레이어(포수)가 홈을 비웠다면 홈 커버에 들어간 야수를 겨냥한다.
        BaseballPlayer receiver = myTeam.GetBaseCoverFielder(commanded, me);

        // 내가 그 베이스 바로 옆이면 던지지 말고 직접 밟는다(설계도 6.2 예외 처리).
        float myDist = FieldMetrics.FlatDistance(me.transform.position, basePos);
        if (myDist <= FieldMetrics.M(DefenseTuning.RUN_TO_BASE_MAX_DIST) &&
            (receiver == null || receiver == me || dist < FieldMetrics.M(3f)))
        {
            plan.Kind = THROW_KIND.RUN_TO_BASE;
            plan.TargetBase = commanded;
            plan.TargetRunner = RunnerTracker.RunnerHeadingTo(commanded);
            plan.AimPoint = basePos;
            plan.IsUserCommanded = true;
            return true;
        }

        if (dist > attr.MaxThrowRange)
        {
            // 노바운드로 닿지 않는다 — 중계맨을 거쳐서라도 지시를 이행한다.
            BaseballPlayer relay = myTeam.GetRelayFielder(ballPos, basePos, me);
            if (relay == null) return false;

            Transform glove = relay.GetItem();
            plan.Kind = THROW_KIND.RELAY;
            plan.TargetBase = commanded;
            plan.Receiver = relay;
            plan.AimPoint = glove != null ? glove.position : relay.transform.position;
            plan.IsUserCommanded = true;
            return true;
        }

        RunnerInfo target = RunnerTracker.RunnerHeadingTo(commanded);

        plan.Kind = THROW_KIND.TO_BASE;
        plan.TargetBase = commanded;
        plan.Receiver = receiver;
        plan.TargetRunner = target;
        plan.AimPoint = AimPointFor(commanded, receiver);
        plan.OutMargin = target.IsValid ? target.Eta - (releaseDelay + attr.ThrowFlightTime(dist)) : 0f;
        plan.IsUserCommanded = true;
        return true;
    }

    // ============================================================
    //  투수 반환 (설계도 12.2)
    // ============================================================

    /// <summary>
    /// 상황 종료 시 공을 투수에게 돌려보내는 계획.
    ///   외야에서 주웠으면 → 가장 가까운 내야수에게 1차 중계(24m/s)
    ///   내야에서 주웠으면 → 투수에게 부드러운 직송구(16m/s)
    ///   투수 본인이면    → 송구 없음(HOLD → 마운드 복귀)
    /// </summary>
    public static ThrowPlan BuildReturnToPitcher(BaseballPlayer me, FielderAttributes attr, Vector3 ballPos, Team myTeam)
    {
        ThrowPlan plan = ThrowPlan.Hold();
        if (me == null || attr == null || myTeam == null) return plan;

        if (attr.Position == PLAYER_TYPE.E_PITCHER)
        {
            plan.Kind = THROW_KIND.HOLD;
            return plan;
        }

        BaseballPlayer pitcher = myTeam.GetPlayer(PLAYER_TYPE.E_PITCHER);
        if (pitcher == null)
        {
            plan.Kind = THROW_KIND.HOLD;
            return plan;
        }

        Transform pitcherGlove = pitcher.GetItem();
        Vector3 pitcherPos = pitcherGlove != null ? pitcherGlove.position : pitcher.transform.position;
        float dist = FieldMetrics.FlatDistance(ballPos, pitcherPos);

        bool needRelay = attr.IsOutfielder || dist > FieldMetrics.M(DefenseTuning.RETURN_DIRECT_MAX_DISTANCE);

        if (needRelay)
        {
            BaseballPlayer cutoff = myTeam.GetNearestInfielder(ballPos, me);
            if (cutoff != null && cutoff != pitcher)
            {
                Transform g = cutoff.GetItem();
                plan.Kind = THROW_KIND.RELAY;
                plan.TargetBase = BASE_TYPE.E_SELF;
                plan.Receiver = cutoff;
                plan.AimPoint = g != null ? g.position : cutoff.transform.position;
                plan.SpeedOverrideRaw = DefenseTuning.RETURN_RELAY_SPEED;
                plan.IsReturnThrow = true;
                return plan;
            }
        }

        plan.Kind = THROW_KIND.TO_PITCHER;
        plan.TargetBase = BASE_TYPE.E_SELF;
        plan.Receiver = pitcher;
        plan.AimPoint = pitcherPos;
        plan.SpeedOverrideRaw = DefenseTuning.RETURN_GENTLE_SPEED;
        plan.IsReturnThrow = true;
        return plan;
    }

    // ============================================================
    //  보조
    // ============================================================

    static bool BuildCandidate(BaseballPlayer me, FielderAttributes attr, Vector3 ballPos, Team myTeam,
                               BASE_TYPE b, RunnerInfo runner, float releaseDelay, out Candidate c)
    {
        c = new Candidate();

        Vector3 basePos = FieldMetrics.BasePos(b);
        float dist = FieldMetrics.FlatDistance(ballPos, basePos);
        if (dist > attr.MaxThrowRange) return false;   // 노바운드로 닿지 않는 거리

        BaseballPlayer receiver = myTeam.GetBaseCoverFielder(b, me);
        float receiverEta = 0f;
        if (receiver != null && receiver != me)
        {
            float rDist = FieldMetrics.FlatDistance(receiver.transform.position, basePos);
            if (rDist > FieldMetrics.M(DefenseTuning.RECEIVER_AT_BASE_TOLERANCE))
            {
                receiverEta = InterceptSolver.TimeToPoint(
                    receiver.transform.position, basePos, receiver.transform.forward,
                    receiver.Attributes, true);
            }
        }

        float flight = attr.ThrowFlightTime(dist);
        float arrive = Mathf.Max(releaseDelay + flight, receiverEta);

        c.Base = b;
        c.Runner = runner;
        c.Receiver = receiver;
        c.ArriveTime = arrive;
        c.ThrowDistance = dist;
        c.Margin = runner.Eta - arrive;
        c.Viable = c.Margin > DefenseTuning.THROW_GIVE_UP_MARGIN;
        return true;
    }

    static Candidate FindCandidate(BASE_TYPE b)
    {
        for (int i = 0; i < _cands.Count; i++)
        {
            if (_cands[i].Base == b) return _cands[i];
        }
        return default(Candidate);
    }

    static ThrowPlan MakeThrow(Candidate c, Team team)
    {
        ThrowPlan p = new ThrowPlan();
        p.Kind = THROW_KIND.TO_BASE;
        p.TargetBase = c.Base;
        p.Receiver = c.Receiver;
        p.TargetRunner = c.Runner;
        p.OutMargin = c.Margin;
        p.AimPoint = AimPointFor(c.Base, c.Receiver);
        return p;
    }

    /// <summary>
    /// 송구 도착점. 수구자가 이미 베이스에 도착해 있으면 글러브를 겨냥하고,
    /// 아직 오는 중이면 베이스 위 가슴 높이(=커버 요원의 리드 위치)를 겨냥한다.
    /// 설계도 6.2 : "달려오고 있는 커버 수비수의 리드 위치를 향해 유도 송구".
    /// </summary>
    public static Vector3 AimPointFor(BASE_TYPE b, BaseballPlayer receiver)
    {
        Vector3 basePos = FieldMetrics.BasePos(b);
        Vector3 aim = basePos + Vector3.up * FieldMetrics.M(DefenseTuning.THROW_TARGET_HEIGHT);

        if (receiver != null)
        {
            float d = FieldMetrics.FlatDistance(receiver.transform.position, basePos);
            if (d <= FieldMetrics.M(DefenseTuning.RECEIVER_AT_BASE_TOLERANCE))
            {
                Transform glove = receiver.GetItem();
                if (glove != null) return glove.position;
            }
        }
        return aim;
    }

    public static BASE_TYPE RankToBase(int rank)
    {
        switch (rank)
        {
            case 1: return BASE_TYPE.E_FIRST_BASE;
            case 2: return BASE_TYPE.E_SECOND_BASE;
            case 3: return BASE_TYPE.E_THIRD_BASE;
            case 4: return BASE_TYPE.E_HOME_BASE;
            default: return BASE_TYPE.E_SELF;
        }
    }

    /// <summary>
    /// 설계도 12.1 「더 이상 아웃시킬 수 없는 상태」 판정.
    /// 살아있는 모든 주자에 대해 어떤 베이스로 던져도 확실히 늦으면 true.
    /// </summary>
    public static bool NoFurtherOutPossible(BaseballPlayer me, FielderAttributes attr, Vector3 ballPos, Team myTeam)
    {
        if (me == null || attr == null || myTeam == null) return true;

        List<RunnerInfo> runners = RunnerTracker.Snapshot(true);
        if (runners.Count == 0) return true;

        float releaseDelay = attr.ReleaseTime;

        for (int i = 0; i < runners.Count; i++)
        {
            RunnerInfo r = runners[i];
            if (r.IsSafe) continue;

            // 태그로 잡을 수 있는 거리면 아직 플레이가 남아 있다
            if (RunnerTracker.IsTaggable(r, me.transform.position)) return false;

            Vector3 basePos = FieldMetrics.BasePos(r.TargetBase);
            float dist = FieldMetrics.FlatDistance(ballPos, basePos);
            if (dist > attr.MaxThrowRange) continue;

            float arrive = releaseDelay + attr.ThrowFlightTime(dist);
            if (r.Eta - arrive > DefenseTuning.NO_OUT_POSSIBLE_MARGIN) return false;
        }

        return true;
    }
}
