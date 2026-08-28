using UnityEngine;

public class LeftField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_LEFT_FILED;
        base.init();
        _role.SetRange(25f);
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 좌익수 전용 수비 존 반환 (좌측 파울라인 ~ 좌중간 외야)
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateLeftField();
    }

    /// <summary>
    /// 타구 유형을 고려한 좌익수 특수 역할 분배
    /// 
    /// 실제 야구에서 좌익수(Left Fielder)의 핵심 역할:
    /// 1. 3루 송구 백업 (3루 악송구 대비 파울라인 뒤쪽 12~15m)
    /// 2. 유격수/3루수 뒤쪽 땅볼 백업 (내야 관통 타구 및 알까기 대비)
    /// 3. 중견수(Center Field) 좌중간 타구 백업 (타구 오버런/펜스 반사 대비 8~10m 뒤)
    /// 4. 2루 송구 시 2루 뒤쪽 2선 백업
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (chaser == this) return false;

        bool isOutfieldHit = landPos.z > 25f;
        Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);

        // ============================================================
        // 1. 외야 타구 (중견수 타구 백업 등)
        // ============================================================
        if (isOutfieldHit)
        {
            // 중견수가 좌중간 또는 중앙 타구를 처리할 때
            if (chaser.Type == PLAYER_TYPE.E_CENTER_FIELD)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                // 중견수 뒤쪽(홈플레이트 반대 방향) 8m 지점으로 백업
                Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
                RoleTargetPosition = chaser.transform.position + backupDir * 8f;
                return true;
            }

            // 우익수 쪽 깊은 타구인 경우 -> 3루 베이스 쪽으로 접근하여 3루 악송구 백업 준비
            if (chaser.Type == PLAYER_TYPE.E_RIGHT_FILED)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                // 3루 베이스 뒤쪽 15m 지점으로 이동
                Vector3 throwDir = (thirdBasePos - chaser.transform.position).normalized;
                RoleTargetPosition = thirdBasePos + throwDir * 15f;
                return true;
            }
        }

        // ============================================================
        // 2. 내야 땅볼 및 번트 (3루수/유격수 뒤쪽 백업 및 3루 송구 백업)
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL || ballType == BATTED_BALL_TYPE.BUNT)
        {
            // 3루수 또는 유격수 쪽 땅볼일 때 -> 내야수 뒤쪽으로 전진하여 알까기/빠지는 공 1차 백업
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_SHORT_STOP)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                Vector3 toFielder = (chaser.transform.position - transform.position).normalized;
                // 해당 내야수 뒤쪽 약 10m 위치로 신속하게 전진 백업
                RoleTargetPosition = chaser.transform.position - toFielder * 10f;
                return true;
            }

            // 1루수/2루수/투수 쪽 땅볼일 때 -> 3루 송구 악송구 백업 (3루 뒤쪽 15m)
            if (chaser.Type == PLAYER_TYPE.E_FIRST_BASE || chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                Vector3 throwDir = (thirdBasePos - chaser.transform.position).normalized;
                RoleTargetPosition = thirdBasePos + throwDir * 15f;
                return true;
            }
        }

        // ============================================================
        // 3. 내야 뜬공 및 라인드라이브
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.POP_FLY || ballType == BATTED_BALL_TYPE.LINE_DRIVE)
        {
            // 3루수/유격수가 내야 뜬공을 처리할 때 뒤쪽에서 콜플레이 대비 백업
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_SHORT_STOP)
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

        // 3루 송구 백업
        if (!isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);
            Vector3 throwDir = (thirdBasePos - chaser.transform.position).normalized;
            RoleTargetPosition = thirdBasePos + throwDir * 15f; 
            return true;
        }

        // 중견수 백업
        if (isOutfieldHit && chaser.Type == PLAYER_TYPE.E_CENTER_FIELD && landPos.x < 0)
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 8f;
            return true;
        }

        return false;
    }
}
