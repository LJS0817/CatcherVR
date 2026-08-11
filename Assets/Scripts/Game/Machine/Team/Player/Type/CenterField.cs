using UnityEngine;

public class CenterField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_CENTER_FIELD;
        base.init();
        _role.SetRange(20f);
    }

    protected override void update()
    {
        base.update();
    }

    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        // 좌익수, 우익수, 2루수, 유격수가 타구를 쫓을 때 중견수가 넓은 범위를 백업
        if (chaser.Type == PLAYER_TYPE.E_LEFT_FILED || chaser.Type == PLAYER_TYPE.E_RIGHT_FILED || 
            chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_SHORT_STOP)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 7f; // 7m 뒤에서 백업
            return true;
        }
        return false;
    }
}
