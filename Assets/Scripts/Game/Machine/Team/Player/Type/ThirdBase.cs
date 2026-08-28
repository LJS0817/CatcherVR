using UnityEngine;

public class ThirdBase : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_THIRD_BASE;
        base.init(BASE_TYPE.E_THIRD_BASE);
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 3루수 전용 수비 존 반환.
    /// 3루 파울라인 안팎 및 3루-유격수 간 공간 커버.
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateThirdBase();
    }

    /// <summary>
    /// 타구 유형을 고려한 3루수 역할 분배.
    /// 
    /// 실제 야구에서 3루수(Hot Corner)의 핵심 역할:
    /// - 번트 시: 자신이 전진 처리하지 않는 경우(투수/포수/1루수 처리) 3루 베이스 커버
    /// - 타 내야수(유격수, 2루수, 1루수, 투수) 땅볼 시: 3루 베이스 커버
    /// - 좌측 외야 장타 시: 유격수가 릴레이(컷오프) 나간 사이 3루 베이스 지키며 송구 수구 대기
    /// - 우측 외야 타구 시: 주자의 3루 진루(태그업 등)를 막기 위해 3루 베이스 커버
    /// - 내야 뜬공: 자기 존 밖이면 3루 커버
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        // 자신이 체이서라면 기본 행동 수행
        if (chaser == this) return false;

        Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);

        // ============================================================
        // 1. 번트(BUNT) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.BUNT)
        {
            // 투수나 포수, 1루수가 번트 타구를 잡으러 간 경우 3루수는 3루를 지킴
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = thirdBasePos;
            return true;
        }

        // ============================================================
        // 2. 땅볼(GROUND_BALL) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL)
        {
            // 유격수가 3루-유격수 간 깊은 타구를 쫓아간 경우에도 3루수는 3루로 복귀하여 송구 대기
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = thirdBasePos;
            return true;
        }

        // ============================================================
        // 3. 라인드라이브(LINE_DRIVE) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.LINE_DRIVE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = thirdBasePos;
            return true;
        }

        // ============================================================
        // 4. 내야 뜬공(POP_FLY) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.POP_FLY)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = thirdBasePos;
            return true;
        }

        // ============================================================
        // 5. 플라이볼(FLY_BALL) 처리
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.FLY_BALL)
        {
            bool isOutfieldHit = landPos.z > 25f;

            // 좌측 깊은 장타 시 유격수가 컷오프를 위해 나가므로 3루수는 3루 베이스를 확고히 커버
            if (isOutfieldHit)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = thirdBasePos;
                return true;
            }

            // 내야 플라이 시 3루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = thirdBasePos;
            return true;
        }

        // 기본: 3루 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = thirdBasePos;
        return true;
    }

    /// <summary>
    /// 기존 호환용 (BATTED_BALL_TYPE 없이 호출 시)
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        if (chaser == this) return false;

        // 기본적으로 3루 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
        return true;
    }
}
