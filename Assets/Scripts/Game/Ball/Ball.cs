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

    /// <summary>플레이어(포수)가 던진 송구인가. 빗나가면 루즈볼 수비가 발동한다.</summary>
    public bool IsPlayerThrow { get; private set; }

    /// <summary>
    /// 배트에 맞은 뒤 지면(또는 펜스)에 한 번이라도 닿았는가.
    /// 노바운드 포구(플라이 아웃) 판정의 유일한 근거다.
    /// </summary>
    public bool HasTouchedGround { get; private set; }

    /// <summary>지금 공을 쥐고 있는 트랜스폼(글러브). 아무도 쥐고 있지 않으면 null.</summary>
    public Transform Holder { get; private set; }

    /// <summary>
    /// 이 송구를 받기로 지정된 야수. AI → AI 송구에서 <b>엉뚱한 야수가 중간에 가로채는 것</b>을 막는다.
    /// (플레이어 송구나 루즈볼은 지정 수신자가 없으므로 누구나 처리할 수 있다.)
    /// </summary>
    public Transform ThrowReceiver { get; private set; }

    public void SetThrowReceiver(Transform t) { ThrowReceiver = t; }

    /// <summary>수비 AI 팀 소속 야수가 공을 쥐고 있는가(플레이어가 쥔 것과 구분).</summary>
    public bool HeldByFielder
    {
        get
        {
            if (Holder == null) return false;
            return Holder.GetComponentInParent<BaseballPlayer>() != null;
        }
    }

    /// <summary>공이 굴러가는 중인가.</summary>
    public bool IsRolling { get { return _rolling; } }

    /// <summary>
    /// 수비 AI 가 반응해야 하는 "살아있는 공" 인가.
    /// 타구 · 플레이어 송구 · 야수 송구는 모두 수비 대상이지만, 투구는 아니다.
    /// </summary>
    public bool IsLiveForDefense
    {
        get { return (_isBattedBall || IsPlayerThrow || IsFielderThrow) && !_rig.isKinematic; }
    }

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
        IsPlayerThrow = false;
        HasTouchedGround = false;
        Holder = null;
        ThrowReceiver = null;
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
        IsPlayerThrow = false;
        HasTouchedGround = false;
        Holder = null;
        ThrowReceiver = null;
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

        // ── 노바운드 여부 확정 ──
        // 글러브 · 배트 · 다른 공과의 접촉을 제외한 모든 충돌(지면 · 펜스 · 백스톱)은
        // "땅에 닿았다" 로 간주한다. 이 플래그만이 플라이 아웃 판정의 근거이며,
        // 예전처럼 _contact 를 쓰면 바운드 후 콜라이더에서 벗어나는 순간 다시 false 가 되어
        // <b>튄 공을 잡아도 플라이 아웃이 선언되는 치명적 오판정</b>이 발생했다.
        if (!IsCatchGear(collision))
        {
            HasTouchedGround = true;
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

    /// <summary>포구 장비(글러브 / 배트 / 다른 공)와의 접촉인가 — 낙구로 보지 않는다.</summary>
    static bool IsCatchGear(Collision c)
    {
        if (c == null || c.transform == null) return false;
        string tag = c.transform.tag;
        if (tag == "Glove" || tag == "Ball" || tag == "Bat" || tag == "Player") return true;

        string n = c.transform.name.ToLower();
        return n.Contains("glove") || n.Contains("bat") || n.Contains("hand");
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
        if (_calc != null) _calc.Clear(Line);
        changeState(true, parent);
        transform.localPosition = Vector3.zero;
        IsFoul = false;
        _isBattedBall = false;
        IsFielderThrow = false;
        IsPlayerThrow = false;
        HasTouchedGround = false;
        Holder = parent;
        ThrowReceiver = null;
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

    /// <summary>
    /// 노바운드 포구(플라이 아웃)로 인정할 수 있는 상태인가.
    /// 배트에 맞은 뒤 지면 · 펜스에 한 번도 닿지 않았을 때만 true.
    /// </summary>
    public bool CanBeDirectOut() { return !HasTouchedGround; }

    public void ThrowBallByPlayer()
    {
        _playerThrows = true;
        IsPlayerThrow = true;
        IsFielderThrow = false;
        _isBattedBall = false;
        HasTouchedGround = false;
        Holder = null;
        ThrowReceiver = null;
        _rolling = false;
        _contact = false;
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
        IsPlayerThrow = false;
        HasTouchedGround = false;
        Holder = null;
        ThrowReceiver = null;

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
        IsPlayerThrow = false;
        HasTouchedGround = false;
        Holder = null;
        ThrowReceiver = null;
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
            IsPlayerThrow = false;
            HasTouchedGround = false;   // 새 타구 — 노바운드 상태로 리셋
            Holder = null;
            ThrowReceiver = null;
            _rolling = false;
        }
    }

    /// <summary>
    /// 페일세이프 : 물리 이상이나 실책 연쇄로 플레이가 끝나지 않을 때
    /// 공을 즉시 투수의 글러브로 회수한다(설계도 12.4).
    /// </summary>
    public void ForceReturnTo(Transform holder)
    {
        if (holder == null) return;
        GrabBall(holder);
    }

    public string GetContactName() { return _contactName; }
}
