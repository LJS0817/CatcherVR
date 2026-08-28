using UnityEngine;

/// <summary>
/// 포지션별 수비 범위를 부채꼴(Sector) 형태로 정의.
/// 홈플레이트를 원점으로, 각 포지션이 담당하는 각도 범위와 거리 범위를 설정한다.
/// 
/// 좌표계:
///   - 홈 → 2루 방향이 Z+ (0°)
///   - 좌측(3루 방향)이 X- (양의 각도: 0° ~ +180°)
///   - 우측(1루 방향)이 X+ (음의 각도: 0° ~ -180°)
/// </summary>
public class DefenseZone
{
    /// <summary>수비 범위 시작 각도 (도, 홈→2루=0°, 좌측=+, 우측=-)</summary>
    public float MinAngleDeg;
    /// <summary>수비 범위 끝 각도 (도)</summary>
    public float MaxAngleDeg;
    /// <summary>홈에서 최소 커버 거리 (m)</summary>
    public float MinDistance;
    /// <summary>홈에서 최대 커버 거리 (m)</summary>
    public float MaxDistance;

    public DefenseZone(float minAngle, float maxAngle, float minDist, float maxDist)
    {
        MinAngleDeg = minAngle;
        MaxAngleDeg = maxAngle;
        MinDistance = minDist;
        MaxDistance = maxDist;
    }

    /// <summary>
    /// 타구 낙하 지점이 이 수비 존 안에 있는지 판단한다.
    /// </summary>
    /// <param name="ballLandPos">타구 낙하(또는 도달) 예상 지점 (월드 좌표)</param>
    /// <param name="homePos">홈플레이트 위치 (월드 좌표)</param>
    /// <returns>존 안이면 true</returns>
    public bool IsInZone(Vector3 ballLandPos, Vector3 homePos)
    {
        Vector3 diff = ballLandPos - homePos;
        diff.y = 0f;

        float distance = diff.magnitude;
        if (distance < MinDistance || distance > MaxDistance) return false;

        // 홈→2루 방향(Z+)을 0°로 하는 각도 계산
        // X가 양수(우측=1루 쪽)이면 음의 각도, X가 음수(좌측=3루 쪽)이면 양의 각도
        float angleDeg = Mathf.Atan2(-diff.x, diff.z) * Mathf.Rad2Deg;

        return angleDeg >= MinAngleDeg && angleDeg <= MaxAngleDeg;
    }

    /// <summary>
    /// 존 안에서의 우선도를 계산. 존 중심에 가까울수록 높은 값(0~1).
    /// 존 밖이면 0을 반환.
    /// </summary>
    public float GetPriority(Vector3 ballLandPos, Vector3 homePos)
    {
        if (!IsInZone(ballLandPos, homePos)) return 0f;

        Vector3 diff = ballLandPos - homePos;
        diff.y = 0f;

        float distance = diff.magnitude;
        float angleDeg = Mathf.Atan2(-diff.x, diff.z) * Mathf.Rad2Deg;

        // 거리 중심으로부터의 정규화된 편차 (0=중심, 1=가장자리)
        float distCenter = (MinDistance + MaxDistance) * 0.5f;
        float distRange = (MaxDistance - MinDistance) * 0.5f;
        float distDeviation = distRange > 0f ? Mathf.Abs(distance - distCenter) / distRange : 0f;

        // 각도 중심으로부터의 정규화된 편차
        float angleCenter = (MinAngleDeg + MaxAngleDeg) * 0.5f;
        float angleRange = (MaxAngleDeg - MinAngleDeg) * 0.5f;
        float angleDeviation = angleRange > 0f ? Mathf.Abs(angleDeg - angleCenter) / angleRange : 0f;

        // 중심에 가까울수록 높은 우선도 (1.0이 최고)
        float priority = 1f - (distDeviation * 0.4f + angleDeviation * 0.4f);
        return Mathf.Clamp01(priority);
    }

    /// <summary>
    /// 공의 궤적(LineRenderer)이 이 존을 통과하는지 검사.
    /// </summary>
    public bool DoesTrajectoryPassThrough(LineRenderer line, Vector3 homePos, float maxHeight, out int interceptIndex)
    {
        interceptIndex = -1;
        if (line == null || line.positionCount == 0) return false;

        for (int i = 0; i < line.positionCount; i++)
        {
            Vector3 pos = line.GetPosition(i);
            if (pos.y > maxHeight || pos.y < -0.5f) continue;

            if (IsInZone(pos, homePos))
            {
                interceptIndex = i;
                return true;
            }
        }
        return false;
    }

    // ============================================================
    // 포지션별 기본 수비 존 팩토리 메서드
    // ============================================================
    // 
    // 각도 컨벤션:
    //   0° = 홈→2루 (센터 방향)
    //   양의 각도 = 좌측 (3루/좌익수 방향, X < 0)
    //   음의 각도 = 우측 (1루/우익수 방향, X > 0)

    /// <summary>1루수 수비 존: 우측 파울 깊숙한 곳(-135°) ~ 중앙쪽(+5°)</summary>
    public static DefenseZone CreateFirstBase()
    {
        // 1루수: 우측 파울 영역(-135°) ~ 중앙쪽 +5°, 거리 0~25m (파울 플라이 및 백스탑 부근 포구 포함)
        return new DefenseZone(-135f, 5f, 0f, 25f);
    }

    /// <summary>2루수 수비 존: 1·2루 사이 및 우중간 얕은 외야</summary>
    public static DefenseZone CreateSecondBase()
    {
        // 2루수: -40° ~ +10°, 거리 8~32m
        return new DefenseZone(-40f, 10f, 8f, 32f);
    }

    /// <summary>유격수 수비 존: 2·3루 사이 및 좌중간 얕은 외야</summary>
    public static DefenseZone CreateShortStop()
    {
        // 유격수: -10° ~ +40°, 거리 8~32m
        return new DefenseZone(-10f, 40f, 8f, 32f);
    }

    /// <summary>3루수 수비 존: 중앙쪽(-5°) ~ 좌측 파울 깊숙한 곳(+135°)</summary>
    public static DefenseZone CreateThirdBase()
    {
        // 3루수: -5° ~ 좌측 파울 영역 +135°, 거리 0~25m (파울 플라이 및 백스탑 부근 포구 포함)
        return new DefenseZone(-5f, 135f, 0f, 25f);
    }

    /// <summary>좌익수 수비 존 (좌측 파울 라인 외야 포함)</summary>
    public static DefenseZone CreateLeftField()
    {
        // 좌익수: +5° ~ +90°, 거리 15~70m
        return new DefenseZone(5f, 90f, 15f, 70f);
    }

    /// <summary>중견수 수비 존</summary>
    public static DefenseZone CreateCenterField()
    {
        // 중견수: -30° ~ +30°, 거리 20~75m
        return new DefenseZone(-30f, 30f, 20f, 75f);
    }

    /// <summary>우익수 수비 존 (우측 파울 라인 외야 포함)</summary>
    public static DefenseZone CreateRightField()
    {
        // 우익수: -90° ~ -5°, 거리 15~70m
        return new DefenseZone(-90f, -5f, 15f, 70f);
    }

    /// <summary>투수 수비 존 (마운드 주변 범위)</summary>
    public static DefenseZone CreatePitcher()
    {
        // 투수: -45° ~ +45°, 거리 0~12m
        return new DefenseZone(-45f, 45f, 0f, 12f);
    }

    /// <summary>PLAYER_TYPE에 따라 적절한 수비 존을 반환</summary>
    public static DefenseZone CreateForPosition(PLAYER_TYPE type)
    {
        switch (type)
        {
            case PLAYER_TYPE.E_FIRST_BASE:   return CreateFirstBase();
            case PLAYER_TYPE.E_SECOND_BASE:  return CreateSecondBase();
            case PLAYER_TYPE.E_SHORT_STOP:   return CreateShortStop();
            case PLAYER_TYPE.E_THIRD_BASE:   return CreateThirdBase();
            case PLAYER_TYPE.E_LEFT_FILED:   return CreateLeftField();
            case PLAYER_TYPE.E_CENTER_FIELD: return CreateCenterField();
            case PLAYER_TYPE.E_RIGHT_FILED:  return CreateRightField();
            case PLAYER_TYPE.E_PITCHER:      return CreatePitcher();
            default:                         return new DefenseZone(-45f, 45f, 0f, 30f);
        }
    }
}
