using System;
using System.Collections;
using UnityEngine;

public class Defencer : PlayerRole
{
    float _allowAngle;
    bool _getReady;

    float _readyToThrow;
    bool _hasBall;

    public override void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        base.init(h, tool, t, ball, player, bT);
        _allowAngle = 0.95f;
        _movement = new CatchMovement();
        _getReady = false;

        _item = _item.GetChild(0);
        _item.gameObject.SetActive(true);

        _range = new Vector3(h * 0.7f, 0.8f + h, 7.5f);

        _readyToThrow = 0f;
        _hasBall = false;

        if (_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetDefencerBaseState(_base, (int)t + 1);
    }

    public override void SetController(Transform pool, Transform point, Transform target, float speed=1.9f)
    {
        _controller = new PitchingController(pool, point, target, speed == 0f ? 1.9f : speed);
    }

    protected override void Move(float speed)
    {
        if (getMovement().isMoving())
        {
            Debug.DrawLine(_my.position, getMovement().GetTarget(), Color.red, 1f);
            if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_FOLLOW_BALL))
            {
                _my.position = getMovement().GetMovementPosition(_my.position, getMovement().GetTarget(), speed);
            }
            else
            {
                _my.position = getMovement().GetMovementPosition(_my.position, _ball.GetPositionWithYToZero(), speed);
            }
        }
        throwBall();
    }

    protected override void PhysicsMoves()
    {
        if (readyToCatch())
        {
            if (getMovement().CompareMovementType(MOVEMENT_TYPE.E_PREDICT_PATH))
            {
                float nowDist = _ball.GetPosition().z - _my.position.z;
                if (nowDist < _range.z)
                {
                    getMovement().SetMovementType(MOVEMENT_TYPE.E_FOLLOW_BALL);
                }
            }
            if (getDistanceBall() < _range.x)
            {
                catchBall();
            }
        }
    }

    protected override bool setMovementTarget(Vector3 pos, Vector3 playerPos, float range, MOVEMENT_TYPE mt)
    {
        return base.setMovementTarget(pos, playerPos,range, mt) || getMovement().CompareMovementType(MOVEMENT_TYPE.E_FOLLOW_BALL);
    }

    public override void BallEventListener(Vector3 playerPos, Vector3 startPos, Vector3 endPos, Vector3 inc)
    {
        if (getReady(playerPos))
        {
            if (_ball.DontNeedToMove()) return;
            if (!setMovementTarget(endPos, playerPos, _range.z, MOVEMENT_TYPE.E_END_POINT))
            {
                float zDir = endPos.z - playerPos.z;
                //Debug.Log(zDir);
                if (zDir < 0)
                {
                    Vector3 dir = endPos - playerPos;
                    //Debug.Log(dir.x / inc.x);
                    Vector3 pos = _ball.GetPosition((int)(zDir / inc.z) - 1);
                    if (pos.y < _range.y)
                    {
                        pos.y = 0;
                        setMovementTarget(pos, playerPos, 10f, MOVEMENT_TYPE.E_JUMP);
                    }
                    jumpToCatch();
                    baseCover();
                }
                else
                {
                    //Debug.Log(transform.name + "    " + inc);
                    inc.y = 0;
                    setMovementTarget(endPos + inc * zDir, playerPos, 30f, MOVEMENT_TYPE.E_PREDICT_PATH);
                }
            }
        } else {
            baseCover();
        }
    }

    bool getReady(Vector3 pos)
    {
        _getReady = isMyDirection(_ball.GetDirection(pos), _allowAngle);
        return _getReady;
    }

    bool readyToCatch()
    {
        return _getReady && _ball != null && _ball.isCatchable();
    }


    void baseCover()
    {
        if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_BASE) && _base != BASE_TYPE.E_SELF)
            setMovementTarget(BasePositionProvider.provider.GetBasePosition(_base), _my.position, -1f, MOVEMENT_TYPE.E_BASE);
    }

    void jumpToCatch()
    {

    }

    void catchBall()
    {
        _item.position = _ball.GetPosition();
        _ball.GrabBall(_item);
        _getReady = false;
        _hasBall = true;

        _readyToThrow = 0f;
        //Debug.Log(_type + "     " + _base);
        if (_base == BASE_TYPE.E_SELF && _ball.CanBeDirectOut())
        {
            PlayerOut(BASE_TYPE.E_FIRST_BASE, BasePositionProvider.provider.GetAttckerBaseState(BASE_TYPE.E_FIRST_BASE));
        } else if(_base != BASE_TYPE.E_SELF) {
            char attacker = BasePositionProvider.provider.GetAttckerBaseState(_base);

            if (attacker > '9' && !BasePositionProvider.provider.GetDefencerBaseState(_base).Equals('_'))
            {
                PlayerOut(_base, attacker);
            }
        }
            
    }

    void PlayerOut(BASE_TYPE baseType, char playerState)
    {
        CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, () => {
            Debug.Log("123");
            GamePlayerProvider.provider.PlayerOut((PLAYER_TYPE)(playerState - ('0' + 10))); 
        });
        BasePositionProvider.provider.SetAttackerBaseState(baseType, -1);
    }

    void throwBall()
    {
        if(_hasBall)
        {
            _readyToThrow += Time.deltaTime;
            if (_readyToThrow >= 0.2f)
            {
                _readyToThrow = 0f;
                _hasBall = false;
                PLAYER_TYPE target = BasePositionProvider.provider.GetThrowTarget();
                if(target == _type) 
                GetController().Action(_my.parent.GetComponent<Team>().GetPlayerItem(target));
            }
        }
    }

    public override void OnTriggerEnter(Collider collider)
    {
        if(collider.transform.tag.Equals("Base"))
        {
            BasePositionProvider.provider.SetDefencerBaseState(_base, (int)_type + 1);
            char attacker = BasePositionProvider.provider.GetAttckerBaseState(_base);

            if (_hasBall && attacker > '9')
            {
                PlayerOut(_base, attacker);
            }
        }
    }

    public override string ToString()
    {
        return "Def";
    }
}
