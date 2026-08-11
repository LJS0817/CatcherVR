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
        // 1루수는 자기가 타구를 쫓는 게 아니라면 1루 베이스 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        return true;
    }
}
