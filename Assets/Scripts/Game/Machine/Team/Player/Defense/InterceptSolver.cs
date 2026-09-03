using UnityEngine;

/// <summary>포구에 필요한 노력의 수준.</summary>
public enum CATCH_EFFORT
{
    NONE,        // 도달 불가
    CAMP,        // 여유롭게 미리 가서 기다림
    ROUTINE,     // 평범하게 달려가서 잡음
    HUSTLE,      // 전력질주로 간신히
    BACKHAND,    // 글러브 반대편으로 뻗어서
    JUMP,        // 점프 캐치
    DIVE         // 다이빙 캐치
}

/// <summary>인터셉트 계산 결과.</summary>
public struct InterceptResult
{
    /// <summary>공을 잡을 수 있는가.</summary>
    public bool CanReach;
    /// <summary>포구(또는 최선의 접근) 지점. y는 공의 높이.</summary>
    public Vector3 Point;
    /// <summary>공이 그 지점에 도달하는 시각(예측 시작 기준, 초).</summary>
    public float BallTime;
    /// <summary>내가 그 지점에 도달하는 데 걸리는 시간(초).</summary>
    public float MyTime;
    /// <summary>여유 시간. 양수면 먼저 도착.</summary>
    public float Slack;
    /// <summary>노바운드 포구인가.</summary>
    public bool IsAirCatch;
    /// <summary>몇 번째 바운드 이후인가.</summary>
    public int Bounces;
    /// <summary>필요한 노력 수준.</summary>
    public CATCH_EFFORT Effort;
    /// <summary>궤적 표본 인덱스.</summary>
    public int SampleIndex;
    /// <summary>도달 불가일 때에도 가장 근접할 수 있는 추격 지점.</summary>
    public Vector3 PursuitPoint;

    public static InterceptResult None(Vector3 fallback)
    {
        InterceptResult r = new InterceptResult();
        r.CanReach = false;
        r.Point = fallback;
        r.PursuitPoint = fallback;
        r.BallTime = 999f;
        r.MyTime = 999f;
        r.Slack = -999f;
        r.Effort = CATCH_EFFORT.NONE;
        r.SampleIndex = -1;
        return r;
    }

    /// <summary>이 결과가 다른 결과보다 좋은 플레이인가(더 빨리, 더 확실하게 처리).</summary>
    public float Quality
    {
        get
        {
            if (!CanReach) return -999f;
            // 빨리 처리할수록(BallTime 이 작을수록) 좋고, 여유가 많을수록 좋다.
            float effortPenalty;
            switch (Effort)
            {
                case CATCH_EFFORT.CAMP: effortPenalty = 0f; break;
                case CATCH_EFFORT.ROUTINE: effortPenalty = 0.15f; break;
                case CATCH_EFFORT.BACKHAND: effortPenalty = 0.35f; break;
                case CATCH_EFFORT.HUSTLE: effortPenalty = 0.55f; break;
                case CATCH_EFFORT.JUMP: effortPenalty = 0.75f; break;
                case CATCH_EFFORT.DIVE: effortPenalty = 1.10f; break;
                default: effortPenalty = 3f; break;
            }
            float airBonus = IsAirCatch ? 1.2f : 0f;   // 노바운드 아웃이 압도적으로 가치 있음
            return airBonus - BallTime * 0.35f - effortPenalty;
        }
    }
}

/// <summary>
/// "저 공을 잡을 수 있는가" 를 푸는 단일 진실 원천.
///
/// 기존 코드는 PhysicsMoves / ApplyDefensiveRole / BallEventListener 세 곳에서
/// 서로 다른 하드코딩 속도(5.6 / 5.0 / 4.5 / 4.2)로 제각각 계산했다.
/// 여기서는 반응 시간 · 가속 · 최고 속도 · 이동 방향(후진 페널티) · 글러브 리치 ·
/// 다이빙/점프 연장까지 한 번에 고려한다.
/// </summary>
public static class InterceptSolver
{
    /// <summary>궤적 표본을 몇 개 건너뛰며 검사할지(성능/정밀도 트레이드오프). 0.02s * 3 = 0.06s 해상도.</summary>
    const int STRIDE = 3;

    /// <summary>
    /// 야수가 궤적을 가로챌 수 있는 가장 이른 지점을 찾는다.
    /// </summary>
    /// <param name="traj">예측 궤적</param>
    /// <param name="fielderPos">야수 현재 위치</param>
    /// <param name="facing">야수가 현재 바라보는 방향</param>
    /// <param name="attr">능력치</param>
    /// <param name="elapsed">궤적 예측 시작 이후 이미 흐른 시간(초)</param>
    /// <param name="alreadyMoving">이미 달리고 있어서 반응 시간이 필요 없는가</param>
    /// <param name="allowDive">다이빙/점프를 허용할지</param>
    /// <param name="preferAir">노바운드 포구를 우선할지(내야 땅볼은 false)</param>
    public static InterceptResult Solve(
        BallTrajectory traj,
        Vector3 fielderPos,
        Vector3 facing,
        FielderAttributes attr,
        float elapsed,
        bool alreadyMoving,
        bool allowDive,
        bool preferAir)
    {
        InterceptResult best = InterceptResult.None(fielderPos);
        if (traj == null || !traj.IsValid || attr == null) return best;

        float groundY = FieldMetrics.GroundY;
        float reach = attr.GloveReach;
        float standingHeight = attr.VerticalReach;
        float jumpHeight = standingHeight + attr.JumpReach;
        float diveReach = reach + attr.DiveExtension;

        float bestPursuitGap = float.MaxValue;
        Vector3 pursuitPoint = traj.LandPosition;
        bool foundAny = false;

        for (int i = 0; i < traj.Count; i += STRIDE)
        {
            TrajectoryPoint p = traj[i];
            float ballTime = p.Time - elapsed;
            if (ballTime < 0f) continue;                  // 이미 지나간 구간

            float height = p.Position.y - groundY;
            if (height > jumpHeight) continue;            // 점프해도 못 닿는 높이

            Vector3 flat = new Vector3(p.Position.x, fielderPos.y, p.Position.z);
            Vector3 toPoint = flat - fielderPos;
            toPoint.y = 0f;
            float dist = toPoint.magnitude;

            float dirFactor = attr.DirectionFactor(facing, toPoint);
            float rawTime = attr.TravelTime(Mathf.Max(0f, dist - reach), dirFactor, !alreadyMoving);

            // 추격 대안 지점 갱신(도달 불가할 때 어디로 뛸지)
            float gap = rawTime - ballTime;
            if (gap < bestPursuitGap)
            {
                bestPursuitGap = gap;
                pursuitPoint = new Vector3(p.Position.x, groundY, p.Position.z);
            }

            bool isAir = p.InAir && height > 0.05f;
            CATCH_EFFORT effort = CATCH_EFFORT.NONE;
            bool ok = false;

            if (rawTime <= ballTime)
            {
                // 제자리에서 잡을 수 있음
                if (height <= standingHeight)
                {
                    float slack = ballTime - rawTime;
                    if (slack > DefenseTuning.CAMP_UNDER_BALL_SLACK) effort = CATCH_EFFORT.CAMP;
                    else if (slack > 0.12f) effort = CATCH_EFFORT.ROUTINE;
                    else effort = CATCH_EFFORT.HUSTLE;
                    ok = true;
                }
                else if (allowDive)
                {
                    effort = CATCH_EFFORT.JUMP;
                    ok = true;
                }
            }
            else if (allowDive)
            {
                // 점프 : 높은 공을 아슬아슬하게
                if (height > standingHeight * 0.75f &&
                    rawTime - ballTime <= DefenseTuning.JUMP_TIME_WINDOW)
                {
                    effort = CATCH_EFFORT.JUMP;
                    ok = true;
                }
                else
                {
                    // 다이빙 : 리치가 늘어나므로 이동 거리를 다시 계산
                    float diveTime = attr.TravelTime(Mathf.Max(0f, dist - diveReach), dirFactor, !alreadyMoving);
                    if (diveTime <= ballTime + DefenseTuning.DIVE_TIME_WINDOW &&
                        height <= standingHeight * 0.85f)
                    {
                        effort = CATCH_EFFORT.DIVE;
                        rawTime = diveTime;
                        ok = true;
                    }
                }
            }

            if (!ok) continue;

            InterceptResult cand = new InterceptResult();
            cand.CanReach = true;
            cand.Point = p.Position;
            cand.BallTime = ballTime;
            cand.MyTime = rawTime;
            cand.Slack = ballTime - rawTime;
            cand.IsAirCatch = isAir;
            cand.Bounces = p.Bounces;
            cand.Effort = effort;
            cand.SampleIndex = i;

            // 노바운드 포구를 우선하는 상황(플라이/라인드라이브)에서는
            // 공중 포구가 가능하면 그걸 최우선으로 확정한다.
            if (preferAir && isAir)
            {
                cand.PursuitPoint = new Vector3(p.Position.x, groundY, p.Position.z);
                return cand;
            }

            if (!foundAny || cand.Quality > best.Quality)
            {
                best = cand;
                foundAny = true;
            }

            // 땅볼 처리: 가장 이른 차단 지점을 찾으면 그걸로 확정
            if (!preferAir && !isAir)
            {
                best.PursuitPoint = new Vector3(p.Position.x, groundY, p.Position.z);
                return best;
            }
        }

        if (!foundAny)
        {
            best = InterceptResult.None(pursuitPoint);
        }
        best.PursuitPoint = pursuitPoint;
        return best;
    }

    /// <summary>
    /// 야수가 특정 지점까지 이동하는 데 걸리는 시간(간이 계산).
    /// 베이스 커버 도착 시간 등에 사용.
    /// </summary>
    public static float TimeToPoint(Vector3 from, Vector3 to, Vector3 facing, FielderAttributes attr, bool alreadyMoving)
    {
        if (attr == null) return 999f;
        Vector3 d = to - from;
        d.y = 0f;
        return attr.TravelTime(d.magnitude, attr.DirectionFactor(facing, d), !alreadyMoving);
    }
}
