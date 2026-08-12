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
        if (chaser == this) return false;

        bool isOutfieldHit = landPos.z > 25f;

        // 1. 내야 땅볼 시 2루 베이스 송구 백업
        if (!isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
            
            // 송구 방향 연장선상 15m 뒤로 백업
            Vector3 throwDir = (secondBasePos - chaser.transform.position).normalized;
            RoleTargetPosition = secondBasePos + throwDir * 15f;
            return true;
        }

        // 2. 외야 타구 시 좌익수/우익수 백업
        if (isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_LEFT_FILED || chaser.Type == PLAYER_TYPE.E_RIGHT_FILED))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 8f; // 8m 뒤에서 백업
            return true;
        }

        return false;
    }
}
