using UnityEngine;

/// <summary>
/// 타구 유형 분류. 공의 초기 속도 벡터를 분석하여 타구 유형을 판별한다.
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
    /// <summary>
    /// 공의 속도 벡터와 낙하 지점으로부터 타구 유형을 판별한다.
    /// </summary>
    /// <param name="velocity">공의 초기 속도 벡터</param>
    /// <param name="landPos">낙하 예상 지점</param>
    /// <param name="homePos">홈플레이트 위치</param>
    /// <returns>분류된 타구 유형</returns>
    public static BATTED_BALL_TYPE Classify(Vector3 velocity, Vector3 landPos, Vector3 homePos)
    {
        float speed = velocity.magnitude;
        
        // 번트 판별: 극저속 (5 m/s 미만)
        if (speed < 5f)
        {
            return BATTED_BALL_TYPE.BUNT;
        }

        // 발사각 계산 (수평 속도 대비 수직 속도의 각도)
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        float launchAngleDeg = Mathf.Atan2(velocity.y, horizontalSpeed) * Mathf.Rad2Deg;

        // 낙하 거리 (홈에서 낙하 지점까지의 XZ 거리)
        Vector3 diff = landPos - homePos;
        diff.y = 0f;
        float landDistance = diff.magnitude;

        // 내야 뜬공 (인필드 플라이): 높은 발사각 + 짧은 비거리
        if (launchAngleDeg > 50f && landDistance < 25f)
        {
            return BATTED_BALL_TYPE.POP_FLY;
        }

        // 플라이볼: 25° ~ 50° (또는 50° 이상이지만 먼 거리)
        if (launchAngleDeg > 25f)
        {
            return BATTED_BALL_TYPE.FLY_BALL;
        }

        // 라인드라이브: 10° ~ 25°
        if (launchAngleDeg > 10f)
        {
            return BATTED_BALL_TYPE.LINE_DRIVE;
        }

        // 땅볼: 10° 미만
        return BATTED_BALL_TYPE.GROUND_BALL;
    }

    /// <summary>
    /// LineRenderer의 궤적으로부터 타구 유형을 판별 (속도 벡터를 직접 모를 때 사용)
    /// </summary>
    public static BATTED_BALL_TYPE ClassifyFromTrajectory(LineRenderer line, Vector3 landPos, Vector3 homePos)
    {
        if (line == null || line.positionCount < 2)
        {
            return BATTED_BALL_TYPE.GROUND_BALL;
        }

        // 초기 속도 방향은 첫 두 점의 차이로 추정
        Vector3 p0 = line.GetPosition(0);
        Vector3 p1 = line.GetPosition(1);
        Vector3 estimatedVelocity = (p1 - p0) / 0.1f; // BallProjectileCalculator에서 time step이 0.1f

        return Classify(estimatedVelocity, landPos, homePos);
    }

    /// <summary>
    /// 타구 유형에 따른 최대 포구 가능 높이를 반환
    /// </summary>
    public static float GetMaxCatchHeight(BATTED_BALL_TYPE type, bool isOutfielder)
    {
        switch (type)
        {
            case BATTED_BALL_TYPE.GROUND_BALL:
                return 1.2f; // 땅볼은 바닥 근처
            case BATTED_BALL_TYPE.LINE_DRIVE:
                return isOutfielder ? 3.0f : 2.5f;
            case BATTED_BALL_TYPE.FLY_BALL:
                return isOutfielder ? 3.5f : 3.0f;
            case BATTED_BALL_TYPE.POP_FLY:
                return 4.0f; // 뜬공은 높이 올라감
            case BATTED_BALL_TYPE.BUNT:
                return 1.0f;
            default:
                return 2.5f;
        }
    }

    /// <summary>
    /// 타구 유형에 따른 수비수 반응 딜레이 (첫 발 멈칫) 범위를 반환
    /// </summary>
    public static void GetReactionDelay(BATTED_BALL_TYPE type, out float minDelay, out float maxDelay)
    {
        switch (type)
        {
            case BATTED_BALL_TYPE.GROUND_BALL:
                minDelay = 0.15f; maxDelay = 0.25f; // 땅볼은 빠른 반응
                break;
            case BATTED_BALL_TYPE.LINE_DRIVE:
                minDelay = 0.1f; maxDelay = 0.2f; // 라인드라이브는 가장 빠른 반응 필요
                break;
            case BATTED_BALL_TYPE.FLY_BALL:
                minDelay = 0.25f; maxDelay = 0.4f; // 플라이볼은 판단 시간 여유
                break;
            case BATTED_BALL_TYPE.POP_FLY:
                minDelay = 0.3f; maxDelay = 0.5f; // 뜬공은 판단할 시간이 많음
                break;
            case BATTED_BALL_TYPE.BUNT:
                minDelay = 0.2f; maxDelay = 0.35f;
                break;
            default:
                minDelay = 0.2f; maxDelay = 0.35f;
                break;
        }
    }
}
