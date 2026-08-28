using UnityEngine;

public class RightField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_RIGHT_FILED;
        base.init();
        _role.SetRange(25f);
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 우익수 전용 수비 존 반환 (우측 파울라인 ~ 우중간 외야)
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateRightField();
    }

    /// <summary>
    /// 타구 유형을 고려한 우익수 특수 역할 분배
    /// 
    /// 실제 야구에서 우익수(Right Fielder)의 핵심 역할:
    /// 1. 1루 송구 악송구 백업 (우익수의 가장 중요한 임무! 1루 뒤쪽 15m)
    /// 2. 2루수/1루수 뒤쪽 땅볼 백업 (1·2루간 관통 타구 및 알까기 대비)
    /// 3. 중견수(Center Field) 우중간 타구 백업 (8~10m 뒤)
    /// 4. 2루 베이스 송구 백업 (우측에서 2루로 향하는 송구 백업)
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (chaser == this) return false;

        bool isOutfieldHit = landPos.z > 25f;
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);

        // ============================================================
        // 1. 외야 타구 시 중견수 백업
        // ============================================================
        if (isOutfieldHit)
        {
            // 중견수가 우중간 또는 중앙 타구를 처리할 때
            if (chaser.Type == PLAYER_TYPE.E_CENTER_FIELD)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                // 중견수 뒤쪽(홈플레이트 반대 방향) 8m 지점으로 신속하게 백업
                Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
                RoleTargetPosition = chaser.transform.position + backupDir * 8f;
                return true;
            }

            // 좌익수 쪽 깊은 타구인 경우 -> 2루 베이스 송구 백업 준비
            if (chaser.Type == PLAYER_TYPE.E_LEFT_FILED)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                Vector3 throwDir = (secondBasePos - chaser.transform.position).normalized;
                RoleTargetPosition = secondBasePos + throwDir * 15f;
                return true;
            }
        }

        // ============================================================
        // 2. 내야 땅볼 및 번트 (★ 1루 송구 악송구 백업 및 2루수 뒤 백업)
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL || ballType == BATTED_BALL_TYPE.BUNT)
        {
            // 2루수 또는 1루수 쪽 땅볼일 때 -> 1·2루 사이 관통 및 알까기 1차 백업
            if (chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                Vector3 toFielder = (chaser.transform.position - transform.position).normalized;
                // 해당 내야수 뒤쪽 약 10m 위치로 전진 백업
                RoleTargetPosition = chaser.transform.position - toFielder * 10f;
                return true;
            }

            // 3루수/유격수/투수가 땅볼을 잡았을 때 -> 1루 송구가 거의 100% 발생하므로 1루 뒤쪽 15m로 질주 백업
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_PITCHER)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                Vector3 throwDir = (firstBasePos - chaser.transform.position).normalized;
                RoleTargetPosition = firstBasePos + throwDir * 15f;
                return true;
            }
        }

        // ============================================================
        // 3. 내야 뜬공 및 라인드라이브 (2루수/1루수 뒤쪽 전진 백업)
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.POP_FLY || ballType == BATTED_BALL_TYPE.LINE_DRIVE)
        {
            if (chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                RoleTargetPosition = chaser.transform.position + new Vector3(0, 0, 8f);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 기존 호환용 (BATTED_BALL_TYPE 없이 호출 시)
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        if (chaser == this) return false;

        bool isOutfieldHit = landPos.z > 25f;

        // 1. 내야 땅볼 시 1루 송구 백업
        if (!isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
            Vector3 throwDir = (firstBasePos - chaser.transform.position).normalized;
            RoleTargetPosition = firstBasePos + throwDir * 15f; 
            return true;
        }

        // 2. 외야 타구 시 중견수 백업
        if (isOutfieldHit && chaser.Type == PLAYER_TYPE.E_CENTER_FIELD && landPos.x > 0)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 8f;
            return true;
        }

        return false;
    }
}
