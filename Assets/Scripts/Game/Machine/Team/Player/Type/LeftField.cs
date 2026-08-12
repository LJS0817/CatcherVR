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
        if (chaser == this) return false;

        bool isOutfieldHit = landPos.z > 25f;

        // 1. 3루 송구 백업 (좌익수의 주요 임무)
        // 유격수, 3루수 타구 처리 시 또는 우익수/1루수 쪽 타구에서 3루로 송구가 올 수 있는 상황
        if (!isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
            
            // 타구 처리자로부터 3루로 향하는 송구 선의 연장선상 15m 뒤로 이동 (파울 라인 밖으로)
            Vector3 throwDir = (thirdBasePos - chaser.transform.position).normalized;
            RoleTargetPosition = thirdBasePos + throwDir * 15f; 
            return true;
        }

        // 2. 외야 타구 시 중견수 백업
        if (isOutfieldHit && chaser.Type == PLAYER_TYPE.E_CENTER_FIELD && landPos.x < 0) // 좌중간 타구일 때
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 8f; // 8m 뒤에서 백업
            return true;
        }

        return false;
    }
}
