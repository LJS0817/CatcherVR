using UnityEngine;

public class SecondBase : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_SECOND_BASE;
        base.init(BASE_TYPE.E_SECOND_BASE);
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 2루수 전용 수비 존 반환.
    /// 1·2루 사이, 우중간 앝은 외야까지 커버.
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateSecondBase();
    }

    /// <summary>
    /// 타구 유형을 고려한 2루수 역할 분배.
    /// 
    /// 실제 야구에서 2루수의 핵심 역할:
    /// - 좌측(유격수/3루수) 타구 시 → 2루 베이스 커버 (병살 피벗)
    /// - 우측(1루수) 땅볼 시 → 1루 백업 또는 2루 커버
    /// - 번트 시 → 1루 커버 (1루수가 전진한 경우)
    /// - 외야 타구 시 → 우중간은 컷오프, 좌측은 2루 커버
    /// - 내야 뜬공 → 자기 존이면 CHASER, 아니면 2루 커버
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (chaser == this) return false;

        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);

        // ============================================================
        // 1. 번트 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.BUNT)
        {
            // 1루수가 번트를 잡으러 전진한 경우 → 2루수가 1루 커버
            if (chaser.Type == PLAYER_TYPE.E_FIRST_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = firstBasePos;
                return true;
            }
            // 3루수/포수가 번트를 잡는 경우 → 2루 커버 유지
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // ============================================================
        // 2. 땅볼(GROUND_BALL) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL)
        {
            // 좌측(유격수/3루수) 땅볼 → 2루 베이스 커버 (병살 피벗 준비)
            if (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = secondBasePos;
                return true;
            }

            // 1루수 앞 짧은 땅볼 → 1루 백업
            if (chaser.Type == PLAYER_TYPE.E_FIRST_BASE && landPos.z < 12f)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = firstBasePos;
                return true;
            }

            // 투수 타구 → 2루 커버 (유격수가 3루 커버를 볼 수 있으므로)
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
            // 좌측 타구 → 2루 커버
            if (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = secondBasePos;
                return true;
            }

            // 우측/중앙 라인드라이브가 내야를 통과했으면 → 백업
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = secondBasePos;
            return true;
        }

        // ============================================================
        // 4. 내야 뜬공(POP_FLY) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.POP_FLY)
        {
            // 뜬공이 자기 존이 아니면 2루 커버
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
                // 우중간 외야 타구 → 컷오프 포지션 (Team.cs에서 처리)
                // 여기서는 Team에서 CUTOFF를 할당하지 못한 경우의 폴백
                if (landPos.x > 0f) // 우측 타구
                {
                    DefRole = DEFENSIVE_ROLE.CUTOFF;
                    // 외야수와 2루 베이스 사이의 중간 지점
                    Vector3 cutoffPos = Vector3.Lerp(secondBasePos, landPos, 0.35f);
                    cutoffPos.y = 0f;
                    RoleTargetPosition = cutoffPos;
                    return true;
                }
                else
                {
                    // 좌측 외야 타구 → 2루 커버 (유격수가 컷오프)
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

        // 기본 폴백
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

        // 좌측(유격수/3루수) 타구 시 2루 베이스 커버
        if (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
            return true;
        }

        // 번트나 극단적 1루 앞 짧은 타구에서 투수가 커버를 못갈 경우 대비해 2루수가 백업 들어갈 수 있음
        if (chaser.Type == PLAYER_TYPE.E_FIRST_BASE && landPos.z < 10f)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE) + new Vector3(2f, 0, 2f);
            return true;
        }

        return false;
    }
}
