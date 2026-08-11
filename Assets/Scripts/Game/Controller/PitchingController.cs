using System.Data;
using UnityEngine;

public class PitchingController : RoleController
{
    BallValueProvider _provider;
    GameObject _ballObj;

    public PitchingController(Transform pool, Transform point, Transform target, float speed)
    {
        base.Init(pool, point, target, speed);

        _provider = new BallValueProvider(pool);
        _ballObj = target.gameObject;
    }

    public override void Action(Transform hint)
    {
        throwBall(hint);
    }

    public override void ResetValue(int i = 0)
    {
        _mode = (PITCHING_MODE)i;
    }

    void throwBall(Transform hint, float deadTime = -1f)
    {
        float speed = _provider.GetSpeed(_mode, _defaultSpeed);
        float dspd = _provider.GetSpeed(_mode, 2f);

        float movementTime = _provider.GetMomentTime(_mode);

        Vector3 angle = _provider.GetAngle(_mode);

        Ball ball = getBallObject(deadTime > 0f, _ballObj).GetComponent<Ball>();

        Vector3 wild = wildPitch(0.2f);
        setHintPosition(wild, _provider.GetOffsetAngle(_mode, angle), hint, movementTime);
        setReleasePoint(hint.position);

        float dist = Vector3.Distance(_ballObj.transform.position, hint.position);

        Vector3 pos = getTargetPosition(wild, hint, ball.GetPosition(), angle * (speed / dspd), speed, dist, movementTime);
        setReleasePoint(pos);

        ball.ResetPosition(_itemPoint.position);
        ball.init(_pool, hint, _itemPoint.forward * speed, deadTime, true);
        ball.type.init(Vector3.Distance(_ballObj.transform.position, hint.position) * movementTime, angle * (speed / dspd));
    }

    void setHintPosition(Vector3 wild, Vector3 missOffset, Transform hint, float movementTime)
    {
        if (hint != null && !hint.tag.Equals("BaseballPlayer")) hint.localPosition = wild + missOffset * movementTime;
    }

    Vector3 getTargetPosition(Vector3 wild, Transform hint, Vector3 ballPos, Vector3 addForce, float speed, float dist, float mT)
    {
        Vector3 vel = ((_itemPoint.forward * speed) / 0.05f);

        float t = dist / vel.magnitude;                     //미트까지 걸리는 시간
        float t2 = (dist * (1f - mT)) / vel.magnitude;      //공의 변화 전까지
        float t3 = t - t2;                                  //변화 후

        Vector3 point = ballPos + t * vel;                  //아무일도 없을 때 위치 계산

        point.y += (Physics.gravity.y * 0.5f * t * t);      //아무일도 없을 때 중력 계산

        Vector3 p3 = point;                                 //추가적인 힘 계산을 위함

        Vector3 vel3 = (addForce / 0.05f);                  //추가적인 힘 계산

        p3 += ((addForce) * 0.5f * t3 * t3);                //위치 계산

        //GameObject te = new GameObject();
        //te.name = "P1";
        //te.transform.position = point;

        //GameObject te3 = new GameObject();
        //te3.name = "P3";
        //te3.transform.position = p3;
        
        Vector3 p = p3 - hint.position;                     //오차 계산
        p *= -1;

        //te3.transform.position = hint.position + p + (Physics.gravity * 0.5f * t * t);

        return hint.position + p;                //오차까지 계산해서 더해주면 끝
    }

    void setReleasePoint(Vector3 target)
    {
        _itemPoint.LookAt(target);
    }

    Vector3 wildPitch(float max)
    {
        if (Random.Range(0, 1f) < 2f) max = 0;
        return new Vector3(Random.Range(-max, max), Random.Range(-max, max), 0);
    }

    GameObject getBallObject(bool needDead, GameObject ballObj)
    {
        if(needDead)
        {
            return GameObject.Instantiate(ballObj, _itemPoint.position, _itemPoint.rotation);
        } else
        {
            ballObj.transform.position = _itemPoint.position;
            ballObj.transform.rotation = _itemPoint.rotation;
            ballObj.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            return ballObj;
        }
    }
}
