using UnityEngine;

/// <summary>
/// 포지션별 수비 능력치. 값은 실제 야구 기준(m, s, m/s)으로 정의하고,
/// 프로퍼티로 읽을 때 FieldMetrics.Scale 을 적용해 씬 단위로 변환한다.
///
/// 실책 모델은 사용하지 않는다(완벽한 AI). 능력치는 "물리적으로 가능한 플레이의 한계"와
/// 동작의 질감(반응 속도, 팔 슬롯, 크로우 홉 길이 등)을 결정하는 용도로만 쓰인다.
/// </summary>
public class FielderAttributes
{
    // ── 실측 단위(원본) ─────────────────────────────────
    public float ReactionTimeRaw = 0.20f;   // s : 타구를 읽고 첫 발이 나가기까지
    public float AccelerationRaw = 5.6f;    // m/s^2
    public float TopSpeedRaw = 8.0f;        // m/s
    public float GloveReachRaw = 1.05f;     // m : 몸 중심에서 글러브까지 수평 리치
    public float VerticalReachRaw = 2.55f;  // m : 제자리에서 글러브가 닿는 최대 높이
    public float JumpReachRaw = 0.72f;      // m : 점프로 추가되는 높이
    public float DiveExtensionRaw = 1.55f;  // m : 다이빙으로 추가되는 수평 리치
    public float ArmSpeedRaw = 36f;         // m/s : 송구 초속
    public float MaxThrowRangeRaw = 95f;    // m : 노바운드 송구 한계

    /// <summary>
    /// 설계도 11.1 「캐치 반경 내 100% 포구 보장」의 반경(m).
    /// 이 안으로 공이 들어오면 어떤 상태에서든 반드시 잡는다.
    /// </summary>
    public float CatchRadiusRaw = DefenseTuning.GUARANTEED_CATCH_RADIUS;

    // ── 시간 계열(스케일 무관) ───────────────────────────
    public float TransferTime = 0.36f;      // s : 글러브 → 던지는 손
    public float SetFeetTime = 0.18f;       // s : 발 세팅(외야는 크로우 홉)

    // ── 방향별 이동 효율 ────────────────────────────────
    public float BackpedalFactor = 0.74f;   // 뒤로 물러날 때 속도 비율
    public float LateralFactor = 0.90f;     // 옆으로 갈 때 속도 비율

    /// <summary>콜 우선순위. 낮을수록 우선(중견수가 가장 높음).</summary>
    public int CallPriority = 5;

    public PLAYER_TYPE Position;

    // ── 씬 단위로 변환된 값 ──────────────────────────────
    public float ReactionTime { get { return ReactionTimeRaw; } }
    public float Acceleration { get { return FieldMetrics.M(AccelerationRaw); } }
    public float TopSpeed { get { return FieldMetrics.M(TopSpeedRaw); } }
    public float GloveReach { get { return FieldMetrics.M(GloveReachRaw); } }
    public float VerticalReach { get { return FieldMetrics.M(VerticalReachRaw); } }
    public float JumpReach { get { return FieldMetrics.M(JumpReachRaw); } }
    public float DiveExtension { get { return FieldMetrics.M(DiveExtensionRaw); } }
    public float ArmSpeed { get { return FieldMetrics.M(ArmSpeedRaw); } }
    public float MaxThrowRange { get { return FieldMetrics.M(MaxThrowRangeRaw); } }
    public float CatchRadius { get { return FieldMetrics.M(CatchRadiusRaw); } }

    /// <summary>다이빙 · 슬라이딩 · 스트레치 중 일시 확장되는 캐치 반경.</summary>
    public float ExtendedCatchRadius
    {
        get { return FieldMetrics.M(Mathf.Max(CatchRadiusRaw, DefenseTuning.GUARANTEED_CATCH_RADIUS_EXTENDED)); }
    }

    public bool IsOutfielder
    {
        get
        {
            return Position == PLAYER_TYPE.E_LEFT_FILED ||
                   Position == PLAYER_TYPE.E_CENTER_FIELD ||
                   Position == PLAYER_TYPE.E_RIGHT_FILED;
        }
    }

    public bool IsInfielder
    {
        get
        {
            return Position == PLAYER_TYPE.E_FIRST_BASE ||
                   Position == PLAYER_TYPE.E_SECOND_BASE ||
                   Position == PLAYER_TYPE.E_SHORT_STOP ||
                   Position == PLAYER_TYPE.E_THIRD_BASE;
        }
    }

    /// <summary>
    /// 이동 거리를 달리는 데 걸리는 시간. 반응 시간 + 가속 구간 + 순항 구간.
    /// </summary>
    /// <param name="distance">이동 거리(씬 단위)</param>
    /// <param name="directionFactor">방향 효율(1=정면, 0.9=측면, 0.74=후진)</param>
    /// <param name="includeReaction">반응 시간을 포함할지(이미 달리는 중이면 false)</param>
    public float TravelTime(float distance, float directionFactor, bool includeReaction)
    {
        if (distance <= 0.01f) return includeReaction ? ReactionTime : 0f;

        float factor = Mathf.Clamp(directionFactor, 0.35f, 1.15f);
        float vMax = TopSpeed * factor;
        float a = Acceleration * factor;
        if (vMax <= 0.01f || a <= 0.01f) return 999f;

        float tAcc = vMax / a;
        float dAcc = 0.5f * a * tAcc * tAcc;

        float t;
        if (distance <= dAcc) t = Mathf.Sqrt(2f * distance / a);
        else t = tAcc + (distance - dAcc) / vMax;

        return t + (includeReaction ? ReactionTime : 0f);
    }

    /// <summary>
    /// 현재 바라보는 방향 대비 이동 방향의 효율 계수.
    /// 정면 1.0, 측면 LateralFactor, 후면 BackpedalFactor 로 보간.
    /// </summary>
    public float DirectionFactor(Vector3 facing, Vector3 moveDir)
    {
        facing.y = 0f;
        moveDir.y = 0f;
        if (facing.sqrMagnitude < 0.0001f || moveDir.sqrMagnitude < 0.0001f) return 1f;

        float dot = Vector3.Dot(facing.normalized, moveDir.normalized); // -1 ~ 1
        if (dot >= 0f) return Mathf.Lerp(LateralFactor, 1f, dot);
        return Mathf.Lerp(LateralFactor, BackpedalFactor, -dot);
    }

    /// <summary>송구가 목표까지 날아가는 데 걸리는 시간(포물선 보정 포함).</summary>
    public float ThrowFlightTime(float distance)
    {
        if (distance <= 0.1f) return 0.05f;
        float v = ArmSpeed;
        // 거리가 멀수록 아치를 그려야 하므로 유효 속도가 떨어진다.
        float arcPenalty = 1f + Mathf.Clamp01(distance / Mathf.Max(1f, MaxThrowRange)) * 0.28f;
        return (distance / v) * arcPenalty;
    }

    /// <summary>
    /// 지정한 구속(실측 m/s)으로 던졌을 때의 비행 시간.
    /// 투수 반환처럼 일부러 약하게 던지는 송구에 쓴다(설계도 12.2).
    /// </summary>
    public float ThrowFlightTimeAt(float distance, float rawSpeed)
    {
        if (distance <= 0.1f) return 0.05f;
        float v = FieldMetrics.M(Mathf.Max(4f, rawSpeed));
        float arcPenalty = 1f + Mathf.Clamp01(distance / Mathf.Max(1f, MaxThrowRange)) * 0.28f;
        return (distance / v) * arcPenalty;
    }

    /// <summary>포구부터 릴리스까지 걸리는 시간(글러브 전환 + 발 세팅).</summary>
    public float ReleaseTime { get { return TransferTime + SetFeetTime; } }

    // ============================================================
    //  포지션별 프리셋
    // ============================================================

    public static FielderAttributes For(PLAYER_TYPE t)
    {
        FielderAttributes a = new FielderAttributes();
        a.Position = t;

        switch (t)
        {
            case PLAYER_TYPE.E_PITCHER:
                a.ReactionTimeRaw = 0.24f;   // 투구 폼 후 균형 회복 시간
                a.TopSpeedRaw = 7.1f;
                a.AccelerationRaw = 5.0f;
                a.GloveReachRaw = 1.00f;
                a.DiveExtensionRaw = 1.35f;
                a.TransferTime = 0.40f;
                a.SetFeetTime = 0.20f;
                a.ArmSpeedRaw = 33f;
                a.MaxThrowRangeRaw = 70f;
                a.CallPriority = 7;
                break;

            case PLAYER_TYPE.E_CATCHER:
                a.ReactionTimeRaw = 0.30f;   // 포수는 자세를 풀고 나가야 함
                a.TopSpeedRaw = 6.7f;
                a.AccelerationRaw = 4.6f;
                a.GloveReachRaw = 1.00f;
                a.VerticalReachRaw = 2.45f;
                a.DiveExtensionRaw = 1.20f;
                a.CatchRadiusRaw = DefenseTuning.GUARANTEED_CATCH_RADIUS_LONG;
                a.TransferTime = 0.30f;      // 포수는 볼 빼기가 가장 빠름(팝타임)
                a.SetFeetTime = 0.14f;
                a.ArmSpeedRaw = 35f;
                a.MaxThrowRangeRaw = 62f;
                a.CallPriority = 8;
                break;

            case PLAYER_TYPE.E_FIRST_BASE:
                a.ReactionTimeRaw = 0.20f;
                a.TopSpeedRaw = 7.3f;
                a.AccelerationRaw = 5.2f;
                a.GloveReachRaw = 1.18f;     // 미트가 크고 스트레치 리치가 김
                a.VerticalReachRaw = 2.70f;
                a.CatchRadiusRaw = DefenseTuning.GUARANTEED_CATCH_RADIUS_LONG;
                a.DiveExtensionRaw = 1.60f;
                a.TransferTime = 0.38f;
                a.SetFeetTime = 0.18f;
                a.ArmSpeedRaw = 31f;
                a.MaxThrowRangeRaw = 68f;
                a.CallPriority = 6;
                break;

            case PLAYER_TYPE.E_SECOND_BASE:
                a.ReactionTimeRaw = 0.17f;
                a.TopSpeedRaw = 7.9f;
                a.AccelerationRaw = 5.8f;
                a.GloveReachRaw = 1.02f;
                a.DiveExtensionRaw = 1.60f;
                a.TransferTime = 0.32f;      // 병살 피벗용 퀵 트랜스퍼
                a.SetFeetTime = 0.16f;
                a.ArmSpeedRaw = 33f;
                a.MaxThrowRangeRaw = 72f;
                a.CallPriority = 4;
                break;

            case PLAYER_TYPE.E_SHORT_STOP:
                a.ReactionTimeRaw = 0.16f;
                a.TopSpeedRaw = 8.1f;
                a.AccelerationRaw = 6.0f;
                a.GloveReachRaw = 1.05f;
                a.DiveExtensionRaw = 1.70f;  // 내야 최고의 수비 범위
                a.TransferTime = 0.34f;
                a.SetFeetTime = 0.18f;
                a.ArmSpeedRaw = 38f;
                a.MaxThrowRangeRaw = 85f;
                a.CallPriority = 3;
                break;

            case PLAYER_TYPE.E_THIRD_BASE:
                a.ReactionTimeRaw = 0.14f;   // 핫코너, 반응이 가장 빨라야 함
                a.TopSpeedRaw = 7.5f;
                a.AccelerationRaw = 5.6f;
                a.GloveReachRaw = 1.05f;
                a.DiveExtensionRaw = 1.65f;
                a.TransferTime = 0.38f;
                a.SetFeetTime = 0.20f;
                a.ArmSpeedRaw = 38f;
                a.MaxThrowRangeRaw = 88f;
                a.CallPriority = 5;
                break;

            case PLAYER_TYPE.E_LEFT_FILED:
                a.ReactionTimeRaw = 0.26f;
                a.TopSpeedRaw = 8.2f;
                a.AccelerationRaw = 5.4f;
                a.GloveReachRaw = 1.10f;
                a.VerticalReachRaw = 2.75f;
                a.JumpReachRaw = 0.80f;
                a.DiveExtensionRaw = 1.85f;
                a.CatchRadiusRaw = 1.35f;
                a.TransferTime = 0.50f;
                a.SetFeetTime = 0.40f;       // 크로우 홉
                a.ArmSpeedRaw = 38f;
                a.MaxThrowRangeRaw = 100f;
                a.BackpedalFactor = 0.80f;   // 외야수는 뒤로 도는 스텝이 숙련됨
                a.CallPriority = 1;
                break;

            case PLAYER_TYPE.E_CENTER_FIELD:
                a.ReactionTimeRaw = 0.24f;
                a.TopSpeedRaw = 8.5f;        // 가장 빠름
                a.AccelerationRaw = 5.6f;
                a.GloveReachRaw = 1.10f;
                a.VerticalReachRaw = 2.80f;
                a.JumpReachRaw = 0.85f;
                a.DiveExtensionRaw = 1.90f;
                a.CatchRadiusRaw = 1.35f;
                a.TransferTime = 0.50f;
                a.SetFeetTime = 0.40f;
                a.ArmSpeedRaw = 39f;
                a.MaxThrowRangeRaw = 102f;
                a.BackpedalFactor = 0.82f;
                a.CallPriority = 0;          // 외야의 사령탑 — 모든 콜을 이긴다
                break;

            case PLAYER_TYPE.E_RIGHT_FILED:
                a.ReactionTimeRaw = 0.26f;
                a.TopSpeedRaw = 8.2f;
                a.AccelerationRaw = 5.4f;
                a.GloveReachRaw = 1.10f;
                a.VerticalReachRaw = 2.75f;
                a.JumpReachRaw = 0.80f;
                a.DiveExtensionRaw = 1.85f;
                a.CatchRadiusRaw = 1.35f;
                a.TransferTime = 0.50f;
                a.SetFeetTime = 0.40f;
                a.ArmSpeedRaw = 41f;         // 우익수는 3루 송구를 위해 어깨가 가장 강함
                a.MaxThrowRangeRaw = 108f;
                a.BackpedalFactor = 0.80f;
                a.CallPriority = 1;
                break;

            default:
                a.CallPriority = 9;
                break;
        }

        return a;
    }
}
