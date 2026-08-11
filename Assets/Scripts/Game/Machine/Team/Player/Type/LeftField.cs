using UnityEngine;

public class LeftField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_LEFT_FILED;

        base.init();

        _role.SetRange(20f);
    }

    protected override void update()
    {
        base.update();
    }

    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        // 중견수나 3루수가 타구를 쫓을 때 백업
        if (chaser.Type == PLAYER_TYPE.E_CENTER_FIELD || chaser.Type == PLAYER_TYPE.E_THIRD_BASE)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 7f; // 7m 뒤에서 백업
            return true;
        }
        return false;
    }
}
