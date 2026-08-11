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
    public bool _isBattedBall; // 배트에 맞은 타구인지 여부
    public bool _isSwingMiss;
    public bool IsFoul;

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
        _isBattedBall = false;
        _isSwingMiss = false;
        IsFoul = false;
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
        _isSwingMiss = false;
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
        if (IsFoul && (collision.transform.CompareTag("Ground") || collision.transform.CompareTag("Untagged")))
        {
            // 파울볼이 땅에 닿으면 카운트 증가 및 데드볼 처리 -> 플레이 리셋
            IsFoul = false;
            CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_FOUL, null);
            if (GamePlayerProvider.provider != null) GamePlayerProvider.provider.ResetPlay();
            return;
        }

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
                _isBattedBall = true;
                _rig.linearVelocity *= 3f;
                LostBall();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.tag.Equals("Finish") && _windUp)
        {
            _windUp = false;
            
            bool isStrikeZone = CountsProvider.provider.ContainsStrikeZone(transform.position);
            
            if (_isSwingMiss || isStrikeZone)
            {
                CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_STRIKE, null);
            }
            else
            {
                CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_BALL, null);
            }
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
        IsFoul = false; // 잡힌 공은 파울(땅에 닿는 데드볼) 처리를 취소함
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
        // 타구(배트에 맞은 공)는 속도를 줄이지 않음. 그래야 외야 낙구 지점이 정확하게 예측됨
        if(!_playerThrows && !_aiThrows && decreaseFlag && !_isBattedBall) _rig.linearVelocity *= 0.35f;
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
        // AI가 던진 송구이거나, 아직 배트에 맞지 않은 투구라면 수비수가 타구처럼 쫓아가지 않음
        return _aiThrows || !_isBattedBall;
    }

    public void SetBattedBall(bool isBatted)
    {
        _isBattedBall = isBatted;
    }

    public string GetContactName() { return _contactName; }
}
