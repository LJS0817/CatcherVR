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
