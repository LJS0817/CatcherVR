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

    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        if (chaser == this) return false;

        // 좌측 깊은 타구로 유격수가 컷오프를 나간 상황이라면, 3루수는 3루를 지키면서 약간 좌측으로 백업 각도를 잡음
        if (landPos.x < -5f && landPos.z > 25f)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
            RoleTargetPosition = thirdBasePos + new Vector3(-3f, 0, 3f);
            return true;
        }

        // 기본적으로 3루 커버
        DefRole = DEFENSIVE_ROLE.BASE_COVER;
        RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
        return true;
    }
}
