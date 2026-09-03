using UnityEngine;

/// <summary>
/// 설계도 10.4 「물리 엔진 콜라이더 및 판정 알고리즘」 구현.
///
/// 설계도의 판정 규약을 그대로 옮기되, 씬에 전용 콜라이더가 없어도 동작하도록
/// <b>콜라이더가 있으면 콜라이더 교차, 없으면 동등한 거리 판정</b>으로 자동 폴백한다.
///
///   Base Trigger Collider : 각 베이스 Transform 의 Collider (없으면 0.38m 상자 가정)
///   Fielder Cleat         : 야수 발 위치 (루트 위치의 지면 투영)
///   Glove Ball Trigger    : 야수 글러브 구체 트리거 (반경 0.25m)
///   Runner Body Hitbox    : 주자 몸통 캡슐 (반경 0.35m)
///
/// 판정의 핵심은 <b>타임스탬프 비교</b>다.
///   포스 아웃 : 야수의 포구 시각 &lt; 주자의 베이스 터치 시각
///   태그 아웃 : 주자가 베이스에 닿기 <b>전에</b> 미트가 몸에 접촉
/// </summary>
public static class OutJudge
{
    /// <summary>글러브 트리거 반경(실측 m). 설계도 10.4-A.</summary>
    public const float GLOVE_TRIGGER_RADIUS = 0.25f;

    /// <summary>주자 몸통 히트박스 반경(실측 m).</summary>
    public const float RUNNER_HITBOX_RADIUS = 0.35f;

    /// <summary>베이스 판 한 변(실측 m). 설계도 10.4-A (0.38m).</summary>
    public const float BASE_SIZE = 0.38f;

    /// <summary>야수 스파이크가 베이스에 닿았다고 보는 여유(실측 m).</summary>
    public const float CLEAT_TOLERANCE = 0.55f;

    /// <summary>동시 도달을 아웃으로 인정하는 허용 오차(초). 야구 규칙상 동타는 주자 세이프.</summary>
    public const float TIE_EPSILON = 0.0f;

    // ============================================================
    //  베이스 접촉
    // ============================================================

    /// <summary>해당 베이스의 Collider(있으면). 없으면 null.</summary>
    static Collider BaseCollider(BASE_TYPE b)
    {
        BasePositionProvider bp = BasePositionProvider.provider;
        if (bp == null || bp.Bases == null) return null;

        int idx = (int)b - (int)BASE_TYPE.E_FIRST_BASE;
        if (idx < 0 || idx >= bp.Bases.Count) return null;

        Transform t = bp.Bases[idx];
        return t != null ? t.GetComponent<Collider>() : null;
    }

    /// <summary>
    /// 한 점이 베이스에 닿아 있는가.
    /// 베이스에 Collider 가 있으면 bounds 교차로, 없으면 0.38m 판 + 여유 거리로 판정한다.
    /// </summary>
    public static bool TouchingBase(Vector3 point, BASE_TYPE b, float tolerance)
    {
        Vector3 basePos = FieldMetrics.BasePos(b);

        Collider col = BaseCollider(b);
        if (col != null)
        {
            Bounds bd = col.bounds;
            bd.Expand(FieldMetrics.M(tolerance) * 2f);
            Vector3 p = new Vector3(point.x, Mathf.Clamp(point.y, bd.min.y, bd.max.y), point.z);
            if (bd.Contains(p)) return true;
        }

        float reach = FieldMetrics.M(BASE_SIZE * 0.5f + tolerance);
        return FieldMetrics.FlatDistance(point, basePos) <= reach;
    }

    /// <summary>야수의 발이 베이스를 밟고 있는가(설계도 10.4-B 2번).</summary>
    public static bool FielderTouchingBase(BaseballPlayer fielder, BASE_TYPE b)
    {
        if (fielder == null) return false;
        return TouchingBase(fielder.transform.position, b, CLEAT_TOLERANCE);
    }

    /// <summary>주자의 발이 베이스에 닿아 있는가.</summary>
    public static bool RunnerTouchingBase(RunnerInfo runner, BASE_TYPE b)
    {
        if (!runner.IsValid) return false;
        return TouchingBase(runner.Player.transform.position, b, RUNNER_HITBOX_RADIUS);
    }

    // ============================================================
    //  포스 아웃 (설계도 10.4-B)
    // ============================================================

    /// <summary>
    /// 포스 아웃 판정.
    ///
    ///   1) 해당 주자가 지금도 포스 상태인가 (포스 해제 시 태그로만 처리 가능)
    ///   2) 야수가 공을 확보한 채 베이스를 밟고 있는가
    ///   3) 포구 시각이 주자의 베이스 도달 시각보다 빠른가
    /// </summary>
    /// <param name="fielder">공을 쥔 야수</param>
    /// <param name="controlTime">
    /// 야수가 <b>공과 베이스를 동시에</b> 확보한 시각(Time.time).
    /// 설계도의 <c>BallCatchTimestamp</c> 를 그대로 쓰면
    /// "멀리서 먼저 잡고 뒤늦게 베이스를 밟은" 경우를 아웃으로 오판하므로,
    /// 포구 시각과 베이스 접촉 시각 중 <b>늦은 쪽</b>을 넘겨야 한다.
    /// </param>
    /// <param name="runner">대상 주자</param>
    /// <param name="targetBase">터치한 베이스</param>
    public static bool EvaluateForceOut(BaseballPlayer fielder, float controlTime,
                                        RunnerInfo runner, BASE_TYPE targetBase)
    {
        if (fielder == null || !runner.IsValid) return false;
        if (targetBase == BASE_TYPE.E_SELF || targetBase == BASE_TYPE.E_BALL) return false;

        // 1. 포스 상태 확인 — 해제되었으면 베이스 터치만으로는 아웃이 아니다.
        if (!runner.IsForced) return false;
        if (!runner.Role.IsOnBaseOrRunning) return false;
        if (runner.IsSafe) return false;

        // 2. 야수가 공을 확보한 채 베이스를 밟고 있는가
        if (!FielderTouchingBase(fielder, targetBase)) return false;

        // 3. 주자가 아직 이 베이스에 닿지 않았어야 아웃이다.
        if (!RunnerTouchingBase(runner, targetBase)) return true;

        // 4. 이미 닿아 있다면 — 야수의 확보가 더 빨랐을 때만 아웃(동타는 주자 세이프).
        float runnerTouchTime = runner.Role.BaseTouchTimestamp;
        if (runnerTouchTime <= 0f) return false;      // 도달 시각을 모르면 안전하게 세이프

        return controlTime + TIE_EPSILON < runnerTouchTime;
    }

    // ============================================================
    //  태그 아웃 (설계도 10.4-C)
    // ============================================================

    /// <summary>
    /// 태그 아웃 판정.
    ///
    ///   1) 야수가 공을 완벽히 쥐고 있는가
    ///   2) 미트 트리거와 주자 바디 히트박스가 물리적으로 겹치는가
    ///   3) 주자가 베이스에 닿아 있지 않은가 (닿아 있으면 태그해도 세이프)
    /// </summary>
    /// <param name="hasBall">공을 쥐고 있는가</param>
    /// <param name="gloveWorldPosition">미트(포구 판정점)의 월드 위치</param>
    /// <param name="runner">대상 주자</param>
    public static bool EvaluateTagOut(bool hasBall, Vector3 gloveWorldPosition, RunnerInfo runner)
    {
        // 1. 포구 우선 원칙 — 공이 없으면 블라인드 태그 금지
        if (!hasBall) return false;
        if (!runner.IsValid) return false;
        if (!runner.Role.IsOnBaseOrRunning) return false;

        // 2. 미트 트리거 ↔ 주자 히트박스 오버랩
        Vector3 runnerPos = runner.Player.transform.position;
        float contact = FieldMetrics.M(GLOVE_TRIGGER_RADIUS + RUNNER_HITBOX_RADIUS);

        Vector3 gloveFlat = new Vector3(gloveWorldPosition.x, 0f, gloveWorldPosition.z);
        Vector3 runnerFlat = new Vector3(runnerPos.x, 0f, runnerPos.z);
        if (Vector3.Distance(gloveFlat, runnerFlat) > contact) return false;

        // 3. 주자가 어느 베이스든 밟고 있으면 세이프
        if (runner.IsSafe) return false;
        if (RunnerTouchingAnyBase(runner)) return false;

        return true;
    }

    /// <summary>주자가 어느 베이스든 밟고 있는가.</summary>
    public static bool RunnerTouchingAnyBase(RunnerInfo runner)
    {
        if (!runner.IsValid) return false;
        for (int rank = 1; rank <= 4; rank++)
        {
            BASE_TYPE b = ThrowDecision.RankToBase(rank);
            if (RunnerTouchingBase(runner, b)) return true;
        }
        return false;
    }

    /// <summary>
    /// 태그 시도 가능 거리인가(스윕 태그 모션을 시작할 시점).
    /// 실제 아웃 인정은 <see cref="EvaluateTagOut"/> 가 판정한다.
    /// </summary>
    public static bool InTagReach(Vector3 gloveWorldPosition, RunnerInfo runner)
    {
        if (!runner.IsValid) return false;
        return FieldMetrics.FlatDistance(gloveWorldPosition, runner.Player.transform.position)
               <= FieldMetrics.M(DefenseTuning.TAG_RANGE);
    }
}
