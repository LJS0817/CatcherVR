using UnityEngine;

public class PitchingMachine : BaseballPlayer
{
    public Transform BallOffset;

    public bool isRightHand;

    public Transform Target;
    public Transform BallHint;

    public BallData BallSelector;
    
    public float speed;

    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_PITCHER;
        base.init();
    }

    protected override void setRoleController(Transform trans=null)
    {
        base.setRoleController(BallOffset);
    }

    protected override void update()
    {
        base.update();
        if (Input.GetKeyDown(KeyCode.A))
        {
            ThrowBall();
        }
    }

    protected override void fixedUpdate()
    {
        base.fixedUpdate();
    }

    public void ThrowBall()
    {
        // 투구 시작 시 투수 위치를 마운드 원위치로 리셋 및 이동 정지
        transform.position = _initPos;
        DefRole = DEFENSIVE_ROLE.IDLE;
        RoleTargetPosition = _initPos;
        if (_role != null)
        {
            _role.movementInit();
        }

        _ball.ResetPosition(BallOffset.position);
        _role.GetController().ResetValue((int)BallSelector.getType());
        _role.GetController().Action(BallHint);
    }

    /// <summary>
    /// 투수 전용 수비 존 반환 (마운드 주변 좁은 반경)
    /// </summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreatePitcher();
    }

    /// <summary>
    /// 투수는 마운드 주변 타구(CHASER)일 때만 직접 수비하고,
    /// 타 수비수의 타구 시에는 마운드를 비우지 않고 마운드 정위치를 지킵니다.
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        // 자신이 체이서라면 마운드 앞 타구를 직접 처리
        if (chaser == this) return false;

        // 1루수나 2루수가 내야 우측 땅볼을 처리할 때, 투수는 1루 베이스 커버 (PFP)
        if (chaser.Type == PLAYER_TYPE.E_FIRST_BASE || chaser.Type == PLAYER_TYPE.E_SECOND_BASE)
        {
            if (landPos.x > 0 && landPos.z < 25f)
            {
                DefRole = DEFENSIVE_ROLE.BASE_COVER;
                RoleTargetPosition = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
                return true;
            }
        }
        
        // 장타 시 홈 송구 백업 (포수 뒤쪽)
        if (landPos.z > 25f) 
        {
            DefRole = DEFENSIVE_ROLE.BACKUP;
            Vector3 homePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE);
            Vector3 dirFromOutfield = (homePos - landPos).normalized;
            RoleTargetPosition = homePos + dirFromOutfield * 8f;
            return true;
        }

        // 그 외: 마운드를 지키며 대기
        DefRole = DEFENSIVE_ROLE.IDLE;
        RoleTargetPosition = _initPos;
        return true;
    }

    /// <summary>
    /// 기존 호환용
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        return AssignSpecialRole(chaser, landPos, BATTED_BALL_TYPE.GROUND_BALL);
    }
}
