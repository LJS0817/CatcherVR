using UnityEngine;

public class SwingController : RoleController
{
    Vector3 _swingStartPos;
    bool _isSwing;
    Vector3 _prevPos;
    int _index;
    Ball _ball;
    bool _chooseNotTo;

    public SwingController(Transform offset, Transform point, Transform target, float speed = 0.135f)
    {
        base.Init(offset, point, target, speed);

        _ball = target.GetComponent<Ball>();

        _isSwing = false;
        _prevPos = Vector3.zero;
        _index = 0;
        _chooseNotTo = false;

        _itemPoint.position = getOffsetPosition(0);
        _swingStartPos = getOffsetPosition(2);
        if (speed > 1f) _defaultSpeed = 0.135f;
    }

    public override void Action(Transform my)
    {
        calculateSwingPosition(my);
    }

    public override void ResetValue(int i=0)
    {
        if (i == 0)
        {
            _prevPos = Vector3.zero;
            _isSwing = false;
            _chooseNotTo = false;
        }
        _index = i;
    }

    Vector3 getOffsetPosition(int idx)
    {
        return _pool.GetChild(idx).position;
    }

    void calculateSwingPosition(Transform my)
    {
        if (!_isSwing)
        {
            float z = my.position.z - _ball.GetPosition().z;
            if (z < 2f) return;
            if (_prevPos != Vector3.zero && z < 10f)
            {
                //if (!_ball.CanBeDirectOut()) return;
                float timing = z / _ball.GetVelocity().z;
                if (timing < _defaultSpeed)
                {
                    Vector3 dir = _ball.GetVelocity().normalized;
                    Vector3 pos = _ball.GetPosition() + dir * z;
                    pos.z = my.position.z;
                    Debug.Log(CountsProvider.provider.ContainsStrikeZone(pos));
                    swing(pos, my);
                }
            }
            if (z < 10f)
            {
                if (_prevPos == Vector3.zero) _chooseNotTo = Random.Range(0, 1f) < 0.1f;
                _prevPos = _ball.GetPosition();
            }
        }
        else if (_index > -1)
        {
            _itemPoint.position = Vector3.MoveTowards(_itemPoint.position, getOffsetPosition(_index), 10f * Time.fixedDeltaTime);
        }
    }


    void swing(Vector3 pos, Transform my)
    {
        _isSwing = true;

        my.GetComponent<Animator>().SetTrigger("Activate");

        pos = _swingStartPos;
        //pos.x = _swingStartPos.x - Mathf.Abs(pos.x - my.position.x);
        pos.y = _swingStartPos.y - 0.095f;

        _pool.GetChild(2).position = pos;

        _itemPoint.position = getOffsetPosition(_index++);
    }
}
