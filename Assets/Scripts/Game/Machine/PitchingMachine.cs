using UnityEngine;

public class PitchingMachine : BaseballPlayer
{
    public Transform BallOffset;

    public bool isRightHand;

    public Transform Target;
    public Transform BallHint;

    public BallData BallSelector;
    
    //float _distPitcher2Target;
    public float speed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_PITCHER;
        base.init();
    }

    protected override void setRoleController(Transform trans=null)
    {
        base.setRoleController(BallOffset);
    }

    // Update is called once per frame
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
        _ball.ResetPosition(BallOffset.position);
        _role.GetController().ResetValue((int)BallSelector.getType());
        _role.GetController().Action(BallHint);
    }

    public override bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos)
    {
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

        return false;
    }
}
