using UnityEngine;

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
    Vector3 _initScale;

    // ── 신규 : 야수 송구 / 공기역학 / 바운드 ─────────────────
    /// <summary>수비수가 던진 송구인가(수비수들이 타구처럼 쫓지 않게 하는 플래그).</summary>
    public bool IsFielderThrow { get; private set; }

    Vector3 _lastVelocity;
    bool _rolling;
    float _trajectoryStamp;

    /// <summary>수비 AI 가 참조하는 최신 예측 궤적.</summary>
    public BallTrajectory Trajectory { get { return _calc != null ? _calc.Trajectory : null; } }

    /// <summary>궤적이 계산된 뒤 흐른 시간(초).</summary>
    public float TrajectoryAge { get { return Time.time - _trajectoryStamp; } }

    private void Awake()
    {
        _initScale = transform.localScale;
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
        IsFielderThrow = false;
        _rolling = false;
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
        _isBattedBall = false;
        IsFielderThrow = false;
        _rolling = false;

        _calc.init();

        if (deadTime > 0f) Destroy(gameObject, deadTime);
    }

    private void FixedUpdate()
    {
        Move();
        ApplyAerodynamics();

        if (_playerThrows)
        {
            Target = null;
            LostBall();
            _playerThrows = false;
        }
        if (_aiThrows && _rig.linearVelocity != Vector3.zero)
        {
            predictBallPath();
            _aiThrows = false;
        }

        _lastVelocity = _rig.linearVelocity;
    }

    /// <summary>
    /// 타구 / 송구에 공기 저항과 백스핀 양력, 구름 마찰을 적용한다.
    /// BallTrajectory 예측기와 <b>완전히 같은 공식</b>을 쓰므로 예측이 실제와 어긋나지 않는다.
    /// (투구는 기존 BallType 의 무브먼트 로직을 그대로 두기 위해 제외한다.)
    /// </summary>
    void ApplyAerodynamics()
    {
        if (_rig == null || _rig.isKinematic) return;
        if (!_isBattedBall && !IsFielderThrow) return;

        Vector3 v = _rig.linearVelocity;

        if (_rolling)
        {
            Vector3 h = new Vector3(v.x, 0f, v.z);
            float sp = h.magnitude;
            if (sp > 0.01f)
            {
                float decel = BallAero.RollFriction * Mathf.Abs(Physics.gravity.y);
                float ns = Mathf.Max(0f, sp - decel * Time.fixedDeltaTime);
                Vector3 nh = (h / sp) * ns;
                _rig.linearVelocity = new Vector3(nh.x, v.y, nh.z);
            }
            return;
        }

        _rig.AddForce(BallAero.ExtraAccel(v), ForceMode.Acceleration);
    }

    void Move()
    {
        if (!_contact && Target != null && !type.Do_not_AddForce())
        {
            _rig.AddForce(type.GetSpeed(getDistance()), ForceMode.Acceleration);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsFoul && (collision.transform.CompareTag("Ground") || collision.transform.CompareTag("Untagged")))
        {
            IsFoul = false;
            CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_FOUL, null);
            if (GamePlayerProvider.provider != null) GamePlayerProvider.provider.ResetPlay();
            return;
        }

        // ── 결정론적 바운드 : 예측기와 동일한 반발/마찰 계수를 강제 적용 ──
        if ((_isBattedBall || IsFielderThrow) && IsGroundLike(collision))
        {
            bool rolling;
            Vector3 bounced = BallAero.Bounce(_lastVelocity, out rolling);
            _rig.linearVelocity = bounced;
            _rolling = rolling;
        }

        if (!_contact)
        {
            _contactName = collision.transform.tag;
            _contact = true;
            _aiThrows = false;
            _windUp = false;
            type.reset();

            if (collision.gameObject.tag.Equals("Glove") || collision.gameObject.tag.Equals("Ball"))
            {
                _rig.linearVelocity *= -0.0001f;
            }
        }
    }

    static bool IsGroundLike(Collision c)
    {
        if (c.transform.CompareTag("Ground")) return true;
        if (c.contactCount > 0)
        {
            return Vector3.Dot(c.GetContact(0).normal, Vector3.up) > 0.55f;
        }
        return false;
    }

    private void OnCollisionExit(Collision collision)
    {
        if (_contact)
        {
            if (collision.gameObject.tag.Contains("Ground"))
            {
                _contact = false;
                predictBallPath(false);
            }
            if (collision.gameObject.name.ToLower().Contains("bat"))
            {
                if (collision.gameObject.GetComponent<Bat>() == null)
                {
                    SetBattedBall(true);
                    LostBall();
                    if (GamePlayerProvider.provider != null)
                    {
                        GamePlayerProvider.provider.NotifyBatterHit();
                    }
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag.Equals("Finish") && _windUp)
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
        IsFoul = false;
        _isBattedBall = false;
        IsFielderThrow = false;
        _rolling = false;
        _contact = false;
    }

    public void LostBall()
    {
        changeState(false, _ballPool);
        transform.localScale = _initScale;
        _contact = false;
        if (!_aiThrows)
        {
            predictBallPath();
        }
    }

    public bool CanBeDirectOut() { return !_contact; }

    public void ThrowBallByPlayer()
    {
        _playerThrows = true;
    }

    /// <summary>
    /// 수비수의 송구. 지정한 비행 시간에 정확히 목표 지점에 도달하도록
    /// 초기 속도를 역산해서 넣는다(포물선 해). 덕분에 ThrowDecision 이 계산한
    /// "송구 도착 시간" 이 실제와 일치한다.
    /// </summary>
    public void ThrowByFielder(Vector3 origin, Vector3 aimPoint, float flightTime)
    {
        flightTime = Mathf.Clamp(flightTime, 0.12f, 4f);

        Target = null;
        type.reset();
        _isBattedBall = false;
        _isSwingMiss = false;
        IsFoul = false;
        _aiThrows = false;
        _rolling = false;
        _contact = false;
        IsFielderThrow = true;

        changeState(false, _ballPool);
        transform.localScale = _initScale;
        transform.position = origin;

        // p = p0 + v*t + 0.5*g*t^2  →  v = (p - p0 - 0.5*g*t^2) / t
        Vector3 v = (aimPoint - origin - 0.5f * Physics.gravity * flightTime * flightTime) / flightTime;

        _rig.linearVelocity = v;
        _rig.angularVelocity = Vector3.zero;
        _lastVelocity = v;
    }

    void changeState(bool hold, Transform parent)
    {
        _rig.isKinematic = hold;
        _collider.enabled = !hold;
        if (parent != null)
        {
            transform.SetParent(parent);
            if (!hold) transform.localScale = _initScale;
        }
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }

    public Vector3 GetPosition(int idx)
    {
        return Line.GetPosition(Mathf.Clamp(Line.positionCount - idx, 0, Line.positionCount - 1));
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
        if (Target == null) return -1f;
        if (Target.position.z - transform.position.z < 0f) return -1f;
        return Vector3.Distance(transform.position, Target.position);
    }

    public void ResetVelocity()
    {
        _rig.angularVelocity = Vector3.zero;
        _rig.linearVelocity = Vector3.zero;
        _lastVelocity = Vector3.zero;
    }

    public void AddListener(listener lis)
    {
        _listeners += lis;
    }

    public void ResetPosition(Vector3 pos)
    {
        ResetVelocity();
        if (_ballPool != null)
        {
            transform.SetParent(_ballPool);
        }
        transform.position = pos;
        transform.localScale = _initScale;
        _aiThrows = true;
        _isBattedBall = false;
        IsFielderThrow = false;
        _rolling = false;
        LostBall();
    }

    public bool isCatchable()
    {
        return !_rig.isKinematic;
    }

    void predictBallPath(bool decreaseFlag = true)
    {
        if (!_playerThrows && !_aiThrows && decreaseFlag && !_isBattedBall) _rig.linearVelocity *= 0.35f;

        // 낙구 마커는 타구일 때만 표시한다(투구/송구에는 띄우지 않음).
        _landPosition = _calc.Calculate(_rig.linearVelocity, _rig.mass, Line, _isBattedBall);
        _trajectoryStamp = Time.time;

        if (Line == null || Line.positionCount < 2) return;

        _flyDirection = (_landPosition - Line.GetPosition(0)).normalized;
        if (_listeners != null)
        {
            _listeners(Line.GetPosition(0), _landPosition, Line.GetPosition(1) - Line.GetPosition(0));
        }
    }

    public float GetDirection(Vector3 pos)
    {
        return Vector3.Dot(_flyDirection, (pos - transform.position).normalized);
    }

    public bool DontNeedToMove()
    {
        // AI 송구 · 수비수 송구 · 아직 배트에 맞지 않은 투구는 타구가 아니다.
        return _aiThrows || IsFielderThrow || !_isBattedBall;
    }

    public void SetBattedBall(bool isBatted)
    {
        // 타구가 막 발생한 순간 한 번만 구장 규격 보정을 적용한다.
        // (Bat.cs 가 만든 실제 MLB 초속을 이 구장 크기에 맞는 비거리로 압축)
        if (isBatted && !_isBattedBall && _rig != null && !_rig.isKinematic)
        {
            float k = BallAero.CarrySpeedScale;
            if (k < 0.999f) _rig.linearVelocity *= k;
            _lastVelocity = _rig.linearVelocity;
        }

        _isBattedBall = isBatted;
        if (isBatted)
        {
            IsFielderThrow = false;
            _rolling = false;
        }
    }

    public string GetContactName() { return _contactName; }
}
