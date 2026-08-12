using UnityEngine;

public class RightField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_RIGHT_FILED;
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

        // 1. 내야 땅볼 시 1루 송구 백업 (우익수의 핵심 임무)
        if (!isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            
            // 타구 처리자(유격수 등)로부터 1루로 향하는 송구 선의 연장선상 15m 뒤로 이동
            Vector3 throwDir = (firstBasePos - chaser.transform.position).normalized;
            RoleTargetPosition = firstBasePos + throwDir * 15f; 
            return true;
        }

        // 2. 외야 타구 시 중견수 백업
        if (isOutfieldHit && chaser.Type == PLAYER_TYPE.E_CENTER_FIELD && landPos.x > 0) // 우중간 타구일 때
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 8f; // 8m 뒤에서 백업
            return true;
        }

        return false;
    }
}

