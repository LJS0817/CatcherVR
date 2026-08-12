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
