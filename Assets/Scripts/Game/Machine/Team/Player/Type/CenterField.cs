using UnityEngine;

public class CenterField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_CENTER_FIELD;
        base.init();
        _role.SetRange(30f); // 중견수는 가장 넓은 수비 반경
    }

    protected override void update()
    {
        base.update();
    }

    /// <summary>
    /// 중견수 전용 수비 존 반환 (중앙 외야 전체 커버, 외야의 사령탑)
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateCenterField();
    }

    /// <summary>
    /// 타구 유형을 고려한 중견수 특수 역할 분배
    /// 
    /// 실제 야구에서 중견수(Center Fielder)의 핵심 역할:
    /// 1. 좌익수 및 우익수 타구 시 깊은 2선 백업 (8~10m 뒤)
    /// 2. 내야 땅볼 시 2루 베이스 악송구 백업 (2루 뒤쪽 15m 지점)
    /// 3. 유격수/2루수 사이를 빠져나가는 중전 안타/알까기 대비 전진 백업
    /// 4. 2루 주자 견제 및 2루 포스/태그 아웃 시 2루 백업
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (chaser == this) return false;

        bool isOutfieldHit = landPos.z > 25f;
        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);

        // ============================================================
        // 1. 외야 타구 시 좌익수 / 우익수 백업
        // ============================================================
        if (isOutfieldHit)
        {
            if (chaser.Type == PLAYER_TYPE.E_LEFT_FILED || chaser.Type == PLAYER_TYPE.E_RIGHT_FILED)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                // 타구 처리 외야수 뒤쪽(홈플레이트 반대 방향) 8m 지점으로 신속하게 백업
                Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
                RoleTargetPosition = chaser.transform.position + backupDir * 8f;
                return true;
            }
        }

        // ============================================================
        // 2. 내야 땅볼 및 번트 (2루 베이스 송구 백업 및 센터 관통 타구 백업)
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.GROUND_BALL || ballType == BATTED_BALL_TYPE.BUNT)
        {
            // 유격수나 2루수가 땅볼을 처리할 때 -> 2루수/유격수 뒤쪽으로 전진하여 알까기 대비 및 2루 송구 백업
            if (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_SECOND_BASE)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                // 2루 베이스 연장선 뒤쪽 15m 지점으로 이동 (2루 악송구 방지)
                Vector3 throwDir = (secondBasePos - chaser.transform.position).normalized;
                RoleTargetPosition = secondBasePos + throwDir * 15f;
                return true;
            }

            // 3루수, 1루수, 투수가 땅볼을 잡을 때도 2루 송구 가능성이 높으므로 2루 백업
            if (chaser.Type == PLAYER_TYPE.E_THIRD_BASE || chaser.Type == PLAYER_TYPE.E_FIRST_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER)
            {
                DefRole = DEFENSIVE_ROLE.BACKUP;
                Vector3 throwDir = (secondBasePos - chaser.transform.position).normalized;
                RoleTargetPosition = secondBasePos + throwDir * 15f;
                return true;
            }
        }

        // ============================================================
        // 3. 내야 뜬공 및 라인드라이브 (키를 넘기는 텍사스성 안타 대비 전진 백업)
        // ============================================================
        if (ballType == BATTED_BALL_TYPE.POP_FLY || ballType == BATTED_BALL_TYPE.LINE_DRIVE)
        {
            if (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_SECOND_BASE)
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

        // 2루 베이스 송구 백업
        if (!isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_SHORT_STOP || chaser.Type == PLAYER_TYPE.E_SECOND_BASE || chaser.Type == PLAYER_TYPE.E_PITCHER))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
            Vector3 throwDir = (secondBasePos - chaser.transform.position).normalized;
            RoleTargetPosition = secondBasePos + throwDir * 15f;
            return true;
        }

        // 좌익수/우익수 백업
        if (isOutfieldHit && (chaser.Type == PLAYER_TYPE.E_LEFT_FILED || chaser.Type == PLAYER_TYPE.E_RIGHT_FILED))
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
            RoleTargetPosition = chaser.transform.position + backupDir * 8f;
            return true;
        }

        return false;
    }
}
