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
}
