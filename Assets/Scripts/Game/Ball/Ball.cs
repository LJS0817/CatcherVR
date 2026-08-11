using System.Collections;
using System.Diagnostics.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

public class Ball : MonoBehaviour
{
    [HideInInspector]
    public Transform Target;
    [HideInInspector]
    public BallType type;

    BallProjectileCalculator _calc;

    public delegate void listener(Vector3 startPos, Vector3 endPos, Vector3 inc);
    listener _listeners;

    Rigidbody _rig;
    Transform _ballPool;
    Collider _collider;

    bool _contact;
    bool _playerThrows;
    bool _aiThrows;
    bool _windUp;

    public LineRenderer Line;

    Vector3 _landPosition;
    Vector3 _flyDirection;

    string _contactName;

    private void Awake()
    {
        _rig = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
        _calc = GetComponent<BallProjectileCalculator>();
        type = new BallType();

        _contact = false;
        _playerThrows = false;
        _aiThrows = false;
        _windUp = false;
        _contactName = "";

        _landPosition = Vector3.zero;
        _flyDirection = Vector3.zero;
    }

    public void init(Transform pool, Transform target, Vector3 dir, float deadTime, bool rightHand)
    {
        ResetVelocity();
        _ballPool = pool;
        Target = target;
        _rig.AddForce(dir, ForceMode.Impulse);

        _landPosition = Vector3.zero;
        _flyDirection = Vector3.zero;
        _playerThrows = false;
        //_aiThrows = false;

        _calc.init();

        if (deadTime > 0f) Destroy(gameObject, deadTime);
    }

    private void FixedUpdate()
    {
        Move();
        if(_playerThrows)
        {
            Target = null;
            LostBall();
            _playerThrows = false;
        }
        if(_aiThrows && _rig.linearVelocity != Vector3.zero)
        {
            predictBallPath();
            _aiThrows = false;
        }
    }

    void Move()
    {
        if (!_contact && Target != null && !type.Do_not_AddForce())
        {
            _rig.AddForce(type.GetSpeed(getDistance()), ForceMode.Acceleration);
            //_rig.AddForce(type.GetSpeed(-1), ForceMode.Acceleration);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!_contact)
        {
            _contactName = collision.transform.tag;
            _contact = true;
            _aiThrows = false;
            _windUp = false;
            type.reset();
            //if (collision.gameObject.name.Contains("bat"))
            //{
            //    LostBall();
            //}
            if (collision.gameObject.tag.Equals("Glove") || collision.gameObject.tag.Equals("Ball"))
            {
                _rig.linearVelocity *= -0.0001f;
            }
        }
    }

    private void OnCollisionExit(Collision collision)
    {
        if(_contact)
        {
            if (collision.gameObject.tag.Contains("Ground"))
            {
                _contact = false;
                predictBallPath(false);
            }
            if (collision.gameObject.name.Contains("bat"))
            {
                //Debug.Log(GetVelocity());
                //Debug.Log(collision.transform.name);
                _rig.linearVelocity *= 3f;
                LostBall();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.tag.Equals("Finish") && CountsProvider.provider.ContainsStrikeZone(transform.position) && _windUp)
        {
            _windUp = false;
            CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_STRIKE, () => { GamePlayerProvider.provider.PlayerOut(PLAYER_TYPE.E_PITCHER); });
        }
    }

    public void WindUp()
    {
        _windUp = true;
    }

    public Vector3 GetLandPosition()
    {
        return _landPosition;
    }

    public void GrabBall(Transform parent)
    {
        ResetVelocity();
        changeState(true, parent);
        transform.localPosition = Vector3.zero;
    }

    public void LostBall()
    {
        changeState(false, _ballPool);
        _contact = false;
        if(!_aiThrows)
        {
            predictBallPath();
        }
    }

    public bool CanBeDirectOut() { return !_contact; }

    public void ThrowBallByPlayer()
    {
        _playerThrows = true;
    }

    void changeState(bool hold, Transform parent)
    {
        _rig.isKinematic = hold;
        _collider.enabled = !hold;
        transform.parent = parent;
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }

    public Vector3 GetPosition(int idx)
    {
        return Line.GetPosition(Line.positionCount - idx);
    }

    public Vector3 GetPositionWithYToZero()
    {
        return new Vector3(transform.position.x, 0, transform.position.z);
    }

    public Vector3 GetVelocity()
    {
        return _rig.linearVelocity;
    }

    float getDistance()
    {
        if (Target.position.z - transform.position.z < 0f) return -1f;
        return Vector3.Distance(transform.position, Target.position);
    }

    public void ResetVelocity()
    {
        _rig.angularVelocity = Vector3.zero;
        _rig.linearVelocity = Vector3.zero;
    }

    public void AddListener(listener lis)
    {
        _listeners += lis;
    }

    public void ResetPosition(Vector3 pos)
    {
        ResetVelocity();
        transform.position = pos;
        _aiThrows = true;
        LostBall();
    }

    public bool isCatchable()
    {
        return !_rig.isKinematic;
    }

    void predictBallPath(bool decreaseFlag=true)
    {
        if(!_playerThrows && !_aiThrows && decreaseFlag) _rig.linearVelocity *= 0.35f;
        _landPosition = _calc.Calculate(_rig.linearVelocity, _rig.mass, Line);

        _flyDirection = (_landPosition - Line.GetPosition(0)).normalized;
        _listeners(Line.GetPosition(0), _landPosition, Line.GetPosition(1) - Line.GetPosition(0));
    }

    public float GetDirection(Vector3 pos)
    {
        return Vector3.Dot(_flyDirection, (pos - transform.position).normalized);
    }

    public bool DontNeedToMove()
    {
        return _aiThrows;
    }

    public string GetContactName() { return _contactName; }
}
