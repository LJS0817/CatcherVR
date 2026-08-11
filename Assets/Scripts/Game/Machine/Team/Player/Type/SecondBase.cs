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

    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        // 1루수가 공을 쫓으면 2루수가 1루 커버
        if (chaser.Type == PLAYER_TYPE.E_FIRST_BASE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            return true;
        }
        // 유격수가 공을 쫓으면 2루수가 2루 커버
        else if (chaser.Type == PLAYER_TYPE.E_SHORT_STOP)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
            return true;
        }
        return false;
    }
}
