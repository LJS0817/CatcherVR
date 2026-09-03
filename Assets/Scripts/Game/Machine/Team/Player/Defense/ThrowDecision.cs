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
/// 기존 BasePositionProvider.GetThrowTarget 은 베이스 이름 문자열만 보고
/// 거리 기반 점수로 골랐기 때문에, 주자가 이미 세이프인데도 던지거나
/// 잡을 수 없는 주자에게 송구하는 일이 잦았다.
/// 여기서는 <b>송구 도착 시간 vs 주자 도착 시간</b>을 직접 비교한다.
///     송구 도착 = 볼 빼기 + 발 세팅 + 거리/구속
///     주자 도착 = 현재 속도와 가속을 반영한 ETA
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
    /// <param name="extraDelay">이미 소모한 시간을 뺀 추가 지연(초)</param>
    public static ThrowPlan Decide(BaseballPlayer me, FielderAttributes attr, Vector3 ballPos, Team myTeam, float extraDelay)
    {
        ThrowPlan plan = ThrowPlan.Hold();
        if (me == null || attr == null || myTeam == null) return plan;

        // 방금 아웃된 주자가 목록에 남지 않도록 항상 새로 수집한다.
        List<RunnerInfo> runners = RunnerTracker.Snapshot(true);
        if (runners.Count == 0)
        {
            plan.Kind = THROW_KIND.TO_PITCHER;
            return plan;
        }

        // ── 0. 눈앞에 태그할 수 있는 주자가 있으면 그게 최우선 ──
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

        float releaseDelay = attr.ReleaseTime + Mathf.Max(0f, extraDelay);

        // ── 1. 각 베이스별로 송구 아웃 가능성 평가 ──
        _cands.Clear();
        for (int rank = 4; rank >= 1; rank--)
        {
            BASE_TYPE b = RankToBase(rank);
            RunnerInfo runner = RunnerTracker.RunnerHeadingTo(b);
            if (!runner.IsValid) continue;
            if (runner.IsSafe) continue;              // 이미 안착 — 던져도 소용 없음
            if (!runner.IsForced) continue;           // 포스가 아니면 베이스 터치로 아웃 안 됨

            Vector3 basePos = FieldMetrics.BasePos(b);
            float dist = FieldMetrics.FlatDistance(ballPos, basePos);
            if (dist > attr.MaxThrowRange) continue;  // 노바운드로 닿지 않는 거리

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

            Candidate c = new Candidate();
            c.Base = b;
            c.Runner = runner;
            c.Receiver = receiver;
            c.ArriveTime = arrive;
            c.ThrowDistance = dist;
            c.Margin = runner.Eta - arrive;
            c.Viable = c.Margin > DefenseTuning.THROW_GIVE_UP_MARGIN;
            _cands.Add(c);
        }

        // ── 2. 내가 직접 베이스를 밟는 편이 빠른가 ──
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

        // ── 3. 병살 판단 : 2루(또는 3루) 포스를 잡고 1루로 이어지는가 ──
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

        // ── 4. 병살이 안 되면 : 확실한 선행 아웃 > 확실한 후행 아웃 ──
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

        // ── 5. 아웃을 잡을 수 없음 : 진루 억제 송구 또는 투수 반환 ──
        RunnerInfo lead2 = RunnerTracker.LeadRunner();
        if (lead2.IsValid && !lead2.IsSafe)
        {
            BASE_TYPE ahead = lead2.TargetBase;
            Vector3 aheadPos = FieldMetrics.BasePos(ahead);
            float d = FieldMetrics.FlatDistance(ballPos, aheadPos);
            if (d <= attr.MaxThrowRange && d > 1.0f)
            {
                Candidate c = new Candidate();
                c.Base = ahead;
                c.Runner = lead2;
                c.Receiver = myTeam.GetBaseCoverFielder(ahead, me);
                c.ThrowDistance = d;
                c.ArriveTime = releaseDelay + attr.ThrowFlightTime(d);
                c.Margin = lead2.Eta - c.ArriveTime;
                return MakeThrow(c, myTeam);
            }
        }

        plan.Kind = THROW_KIND.TO_PITCHER;
        return plan;
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
    /// 아직 오는 중이면 베이스 위 가슴 높이 허공을 겨냥한다(실제 야구와 동일).
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
}
