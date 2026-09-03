using UnityEngine;

/// <summary>
/// 타구 유형 분류. 발사각 · 타구 속도 · 비거리를 기준으로 판별한다.
/// </summary>
public enum BATTED_BALL_TYPE
{
    GROUND_BALL,     // 땅볼 (발사각 < 10°)
    LINE_DRIVE,      // 라인드라이브 (10° ~ 25°)
    FLY_BALL,        // 플라이볼 (25° ~ 50°)
    POP_FLY,         // 내야 뜬공 (50°+ & 짧은 거리)
    BUNT             // 번트 (극저속)
}

public static class BattedBallClassifier
{
    /// <summary>속도 벡터와 낙하 지점으로 타구 유형을 판별.</summary>
    public static BATTED_BALL_TYPE Classify(Vector3 velocity, Vector3 landPos, Vector3 homePos)
    {
        float speed = velocity.magnitude;

        if (speed < FieldMetrics.M(7f))
        {
            return BATTED_BALL_TYPE.BUNT;
        }

        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        float launchAngleDeg = Mathf.Atan2(velocity.y, Mathf.Max(0.001f, horizontalSpeed)) * Mathf.Rad2Deg;

        Vector3 diff = landPos - homePos;
        diff.y = 0f;
        float landDistance = diff.magnitude;

        if (launchAngleDeg > 50f && landDistance < FieldMetrics.M(38f))
        {
            return BATTED_BALL_TYPE.POP_FLY;
        }

        if (launchAngleDeg > 25f) return BATTED_BALL_TYPE.FLY_BALL;
        if (launchAngleDeg > 10f) return BATTED_BALL_TYPE.LINE_DRIVE;
        return BATTED_BALL_TYPE.GROUND_BALL;
    }

    /// <summary>
    /// 새 예측 궤적으로부터 분류. 발사각과 초속을 궤적이 직접 들고 있으므로
    /// LineRenderer 를 역산할 필요가 없다.
    /// </summary>
    public static BATTED_BALL_TYPE ClassifyFromTrajectory(BallTrajectory traj)
    {
        if (traj == null || !traj.IsValid) return BATTED_BALL_TYPE.GROUND_BALL;

        if (traj.LaunchSpeed < FieldMetrics.M(7f)) return BATTED_BALL_TYPE.BUNT;

        float angle = traj.LaunchAngleDeg;
        float dist = traj.CarryDistance;

        if (angle > 50f && dist < FieldMetrics.M(38f)) return BATTED_BALL_TYPE.POP_FLY;
        if (angle > 25f) return BATTED_BALL_TYPE.FLY_BALL;
        if (angle > 10f) return BATTED_BALL_TYPE.LINE_DRIVE;
        return BATTED_BALL_TYPE.GROUND_BALL;
    }

    /// <summary>하위 호환 : LineRenderer 기반 분류.</summary>
    public static BATTED_BALL_TYPE ClassifyFromTrajectory(LineRenderer line, Vector3 landPos, Vector3 homePos)
    {
        if (line == null || line.positionCount < 2)
        {
            return BATTED_BALL_TYPE.GROUND_BALL;
        }

        Vector3 p0 = line.GetPosition(0);
        Vector3 p1 = line.GetPosition(1);
        Vector3 estimatedVelocity = (p1 - p0) / BallTrajectory.DT;

        return Classify(estimatedVelocity, landPos, homePos);
    }

    /// <summary>타구 유형별 최대 포구 가능 높이(실측 m → 씬 단위).</summary>
    public static float GetMaxCatchHeight(BATTED_BALL_TYPE type, bool isOutfielder)
    {
        float raw;
        switch (type)
        {
            case BATTED_BALL_TYPE.GROUND_BALL: raw = 1.2f; break;
            case BATTED_BALL_TYPE.LINE_DRIVE: raw = isOutfielder ? 3.0f : 2.6f; break;
            case BATTED_BALL_TYPE.FLY_BALL: raw = isOutfielder ? 3.4f : 3.0f; break;
            case BATTED_BALL_TYPE.POP_FLY: raw = 3.6f; break;
            case BATTED_BALL_TYPE.BUNT: raw = 1.0f; break;
            default: raw = 2.6f; break;
        }
        return FieldMetrics.M(raw);
    }

    /// <summary>타구 유형별 수비수 반응 딜레이 범위(초).</summary>
    public static void GetReactionDelay(BATTED_BALL_TYPE type, out float minDelay, out float maxDelay)
    {
        switch (type)
        {
            case BATTED_BALL_TYPE.GROUND_BALL: minDelay = 0.12f; maxDelay = 0.20f; break;
            case BATTED_BALL_TYPE.LINE_DRIVE: minDelay = 0.08f; maxDelay = 0.16f; break;
            case BATTED_BALL_TYPE.FLY_BALL: minDelay = 0.20f; maxDelay = 0.32f; break;
            case BATTED_BALL_TYPE.POP_FLY: minDelay = 0.24f; maxDelay = 0.38f; break;
            case BATTED_BALL_TYPE.BUNT: minDelay = 0.16f; maxDelay = 0.28f; break;
            default: minDelay = 0.16f; maxDelay = 0.28f; break;
        }
    }

    /// <summary>타구 유형에 따른 반응 시간 배율(능력치 기반 반응 시간에 곱한다).</summary>
    public static float ReactionMultiplier(BATTED_BALL_TYPE type)
    {
        switch (type)
        {
            case BATTED_BALL_TYPE.LINE_DRIVE: return 0.7f;
            case BATTED_BALL_TYPE.GROUND_BALL: return 0.85f;
            case BATTED_BALL_TYPE.BUNT: return 0.9f;
            case BATTED_BALL_TYPE.FLY_BALL: return 1.15f;
            case BATTED_BALL_TYPE.POP_FLY: return 1.25f;
            default: return 1f;
        }
    }
}
