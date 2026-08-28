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

        // 자신이 체이서가 아니라면 마운드를 지키며 대기 (마운드 이탈 방지)
        DefRole = DEFENSIVE_ROLE.IDLE;
        RoleTargetPosition = _initPos;
        return true;
    }

    /// <summary>
    /// 기존 호환용
    /// </summary>
    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
        if (chaser == this) return false;

        DefRole = DEFENSIVE_ROLE.IDLE;
        RoleTargetPosition = _initPos;
        return true;
    }
}
