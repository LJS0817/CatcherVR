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

    protected override void setRoleController(Transform trans = null)
    {
        base.setRoleController(BallOffset);
    }

    protected override void update()
    {
        base.update();
        if (Input.GetKeyDown(KeyCode.A))
        {
            base.TriggerAReset();
            ThrowBall();
        }
    }

    public override void TriggerAReset()
    {
        base.TriggerAReset();

        // 투구 시작 전 위치 및 상태 초기화
        transform.position = DefaultPosition;
        DefRole = DEFENSIVE_ROLE.IDLE;
        RoleTargetPosition = DefaultPosition;
        if (_role != null)
        {
            _role.movementInit();
        }

        // 공이 글러브(기계 내부)로 들어가서 사라지는 것을 방지하기 위해 발사구로 이동
        if (_ball != null && BallOffset != null)
        {
            _ball.ResetPosition(BallOffset.position);
        }
    }

    protected override void fixedUpdate()
    {
        base.fixedUpdate();
    }

    public void ThrowBall()
    {
        if (_role != null)
        {
            _role.GetController().ResetValue((int)BallSelector.getType());
            _role.GetController().Action(BallHint);
        }
    }

    /// <summary>투수 전용 수비 존 (마운드 주변 좁은 반경).</summary>
    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreatePitcher();
    }

    // 투수의 커버/백업(PFP, 3루·홈 백업)은 FieldingCoordinator 가
    // 주자 상황과 송구 목표 베이스를 보고 매번 다시 결정한다.
}
