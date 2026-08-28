using UnityEngine;

public class ShortStop : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_SHORT_STOP;
        base.init(bT);
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 유격수 전용 수비 존 반환.
    /// 2·3루 사이, 좌중간 얕은 외야까지 커버. 내야에서 가장 넓은 수비 범위.
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateShortStop();
    }

    /// <summary>
    /// 타구 유형을 고려한 유격수 역할 분배.
    /// 
    /// 실제 야구에서 유격수의 핵심 역할:
    /// - 우측(2루수/1루수) 땅볼 → 2루 커버 (병살 피벗)
    /// - 3루수가 타구 처리 → 3루 커버
    /// - 번트 → 3루 커버 (3루수가 전진한 경우) 또는 2루 커버
    /// - 좌측 외야 타구 → 컷오프 (메인 릴레이맨)
    /// - 우측 외야 타구 → 2루 커버
    /// - 내야 뜬공 → 자기 존이면 CHASER, 아니면 커버
    /// - 투수 앞 타구 → 2루 커버
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (chaser == this) return false;

        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
        Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);

        // ============================================================
        // 1. 번트 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.BUNT)
        {
            // 3루수가 번트를 잡으러 전진 → 유격수가 3루 커버
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = thirdBasePos;
                return true;
            }
            // 투수/포수/1루수가 번트 처리 → 2루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // ============================================================
        // 2. 땅볼(GROUND_BALL) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL)
        {
            // 우측(2루수/1루수) 땅볼 → 2루 커버 (병살 피벗)
            if (chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = secondBasePos;
                return true;
            }

            // 3루수 타구 → 3루 커버 (3루수가 빈 자리)
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = thirdBasePos;
                return true;
            }

            // 투수 앞 타구 → 2루 커버
            if (chaser.Type == PLAYER_TYPE.E_PITCHER)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = secondBasePos;
                return true;
            }

            // 기본: 2루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // ============================================================
        // 3. 라인드라이브(LINE_DRIVE) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.LINE_DRIVE)
        {
            // 우측 타구 → 2루 커버
            if (chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = secondBasePos;
                return true;
            }

            // 3루수가 다이빙 등으로 자리를 비움 → 3루 커버
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = thirdBasePos;
                return true;
            }

            // 기본: 2루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // ============================================================
        // 4. 내야 뜬공(POP_FLY) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.POP_FLY)
        {
            // 3루수가 뜬공을 잡으러 가면 → 3루 커버
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = thirdBasePos;
                return true;
            }
            // 그 외 → 2루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // ============================================================
        // 5. 플라이볼(FLY_BALL) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.FLY_BALL)
        {
            bool isOutfieldHit = landPos.z > 25f;

            if (isOutfieldHit)
            {
                // 좌측/좌중간 외야 타구 → 유격수가 컷오프 (메인 릴레이맨)
                if (landPos.x < 0f)
                {
                    DefRole = DEFENSIVE_ROLE.CUTOFF;
                    // 외야수와 3루/홈 사이의 중계 지점
                    Vector3 cutoffPos = Vector3.Lerp(thirdBasePos, landPos, 0.3f);
                    cutoffPos.y = 0f;
                    RoleTargetPosition = cutoffPos;
                    return true;
                }
                else
                {
                    // 우측 외야 타구 → 2루 커버 (2루수가 컷오프)
                    DefRole = DEFENSIVE_ROLE.BASE_COVER;
                    RoleTargetPosition = secondBasePos;
                    return true;
                }
            }

            // 내야 범위 플라이 → 2루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // 기본 폴백: 2루 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = secondBasePos;
        return true;
    }

    /// <summary>
    /// 기존 호환용 (BATTED_BALL_TYPE 없이 호출 시)
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        if (chaser == this) return false;

        // 우측(2루수/1루수) 타구 시 2루 베이스 커버
        if (chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
            return true;
        }

        // 3루수가 타구를 쫓아 베이스를 비웠을 때 3루 커버
        if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
            return true;
        }

        return false;
    }
}
