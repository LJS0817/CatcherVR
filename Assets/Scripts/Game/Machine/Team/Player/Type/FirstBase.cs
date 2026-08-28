using UnityEngine;

public class FirstBase : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_FIRST_BASE;
        base.init(BASE_TYPE.E_FIRST_BASE);
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 1루수 전용 수비 존 반환.
    /// 1루 라인 파울 영역까지 커버하도록 우측을 넓게 잡음.
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateFirstBase();
    }

    /// <summary>
    /// 타구 유형을 고려한 1루수 역할 분배
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        // 자신이 체이서라면 기본 행동 수행
        if (chaser == this) return false;

        // 1. 땅볼이 다른 내야수 존 → 1루 베이스로 이동하여 송구 대기 (스트레치 준비)
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL || ballType == BATTED_BALL_TYPE.BUNT)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            return true;
        }

        // 2. 내야 뜬공 (POP_FLY) — 파울 영역 근처이면 1루수가 추적할 수 있음
        if (ballType == BATTED_BALL_TYPE.POP_FLY)
        {
            // 1루 파울 영역 쪽 뜬공이면 1루수가 커버 (이 경우 Team에서 CHASER로 지정할 수도 있음)
            // 그렇지 않으면 1루 커버
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            return true;
        }

        // 3. 라인드라이브 — 자신의 존 안이 아니면 1루 커버
        if (ballType == BATTED_BALL_TYPE.LINE_DRIVE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            return true;
        }

        // 4. 우측 깊은 장타 시 1루수 오버런 방지 백업
        if (landPos.x > 5f && landPos.z > 25f)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            // 1루 뒤쪽으로 약간 빠져서 백업 위치 잡음
            Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            RoleTargetPosition = firstBasePos + new Vector3(3f, 0, -3f);
            return true;
        }

        // 5. 기본적으로 1루 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        return true;
    }

    /// <summary>
    /// 기존 호환용 (BATTED_BALL_TYPE 없이 호출 시)
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        // 타구 유형 모를 때 기본 동작 (하위 호환)
        if (chaser == this) return false;

        // 우측 깊은 장타 시 백업
        if (landPos.x > 5f && landPos.z > 25f)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            RoleTargetPosition = firstBasePos + new Vector3(3f, 0, -3f);
            return true;
        }

        // 기본적으로 1루 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        return true;
    }
}
