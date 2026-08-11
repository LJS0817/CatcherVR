using UnityEngine;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;

public class Attacker : PlayerRole
{
    float _swingSpeed;
    bool _counted;

    public override void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        base.init(h, tool, t, ball, player, bT);

        _item = _item.GetChild(1);
        _item.gameObject.SetActive(true);   

        _movement = new RunnerMovement();
        _counted = false;

        setSwingSpeed(1f);

        addEvent();
    }

    public override void SetController(Transform pool, Transform point, Transform target, float speed=0.135f)
    {
        _controller = new SwingController(_offsets.GetChild(2), point, _ball.transform, 0.135f);
        //base.SetController(_offsets.GetChild(2), point, _ball.transform, speed, data);
    }

    AnimationEvent createEvent(float time, int i)
    {
        AnimationEvent evt = new AnimationEvent();

        evt.time = time;
        evt.intParameter = i;
        evt.functionName = "Increase";

        return evt;
    }

    void addEvent()
    {
        AnimationClip clip = _ani.runtimeAnimatorController.animationClips[2];
        clip.AddEvent(createEvent(0.03f, 1));
        clip.AddEvent(createEvent(0.09f, 2));
        clip.AddEvent(createEvent(0.16f, 3));
        clip.AddEvent(createEvent(0.19f, 4));
    }

    void setSwingSpeed(float spd)
    {
        _swingSpeed = spd;
        _ani.SetFloat("SwingSpeed", _swingSpeed);
    }

    public override void update(float speed)
    {
        base.update(speed);
    }

    protected override void Move(float speed)
    {
        if(Input.GetKeyDown(KeyCode.A))
        {
            _base = BASE_TYPE.E_SELF;
            _counted = false;
            GetController().ResetValue();
        }

        if (getMovement().isMoving())
        {
            Debug.DrawLine(_my.position, getMovement().GetTarget(), Color.red, 0.1f);
            if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_BASE))
            {
                _my.position = getMovement().GetMovementPosition(_my.position, getMovement().GetTarget(), speed);
            }
        }
    }

    public override void fixedUpdate()
    {
        base.fixedUpdate();
    }

    protected override void PhysicsMoves()
    {
        GetController().Action(_my);
    }

    public override void Increase(int i)
    {
        GetController().ResetValue(i);
        if (i == 1 && _counted) _counted = false;
        if (!_counted && i == 3 && getMovement().CompareMovementType(MOVEMENT_TYPE.E_STAY))
        {
            _counted = true;
            Debug.Log(_ball.GetContactName());
            if(_ball.GetContactName().Equals("Bat"))
            {
                Debug.Log(_ball.GetVelocity().z);
                if(_ball.GetVelocity().z < 0)
                {
                    if(_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
                    _base = BasePositionProvider.provider.GetNextBase(_base);
                    BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
                    setMovementTarget(BasePositionProvider.provider.GetBasePosition(_base), _my.position, -1f, MOVEMENT_TYPE.E_RUN);
                } else
                {
                    CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_FOUL, () => { });
                }
            } else
            {
                CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_STRIKE, () => { GamePlayerProvider.provider.PlayerOut(PLAYER_TYPE.E_PITCHER); });
            }
        }
    }

    public override void OnTriggerEnter(Collider collider)
    {
        if (collider.transform.tag.Equals("Base"))
        {
            BasePositionProvider.provider.SetAttackerBaseState(_base, (int)_type);
        }
    }

    public override string ToString()
    {
        return "Att";
    }
}
