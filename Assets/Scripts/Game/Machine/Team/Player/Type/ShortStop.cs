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
        // 2루수가 타구를 쫓으러 갔다면 유격수가 2루를 커버
        if (chaser.Type == PLAYER_TYPE.E_SECOND_BASE)
        {
            DefRole = DEFENSIVE_ROLE.BASE_COVER;
            RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
            return true;
        }
        return false;
    }
}
