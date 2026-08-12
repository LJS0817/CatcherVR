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

    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        // 자신이 체이서라면 기본 행동 수행
        if (chaser == this) return false;

        // 우측 깊은 장타 시 1루수 오버런 방지 백업
        if (landPos.x > 5f && landPos.z > 25f)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            // 1루 뒤쪽으로 약간 빠져서 백업 위치 잡음
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
