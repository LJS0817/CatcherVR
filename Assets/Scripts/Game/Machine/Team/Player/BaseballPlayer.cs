using UnityEngine;

public enum PLAYER_TYPE
{
    E_FIRST_BASE,
    E_SECOND_BASE,
    E_SHORT_STOP,
    E_THIRD_BASE,

    E_RIGHT_FILED,
    E_CENTER_FIELD,
    E_LEFT_FILED,

    E_PITCHER,

    /// <summary>9번째 야수(포수). VR 에서는 플레이어가 맡지만, AI 팀에서는 홈 커버를 담당한다.</summary>
    E_CATCHER,
}

public enum BASE_TYPE
{
    E_SELF,
    E_BALL,
    E_FIRST_BASE,
    E_SECOND_BASE,
    E_THIRD_BASE,
    E_HOME_BASE,
}

/// <summary>
/// 수비 역할. 설계도 2장의 <c>FielderRole</c> 열거형에 대응한다.
///
///   None              → IDLE
///   PrimaryFielder    → CHASER
///   AssistingFielder  → ASSIST
///   CutoffMan         → CUTOFF
///   RelayMan          → RELAY
///   BaseCover         → BASE_COVER
///   Backup            → BACKUP
///   DecoyOrObserver   → OBSERVER
/// </summary>
public enum DEFENSIVE_ROLE
{
    IDLE,        // None : 지시 없음(원위치 대기)
    CHASER,      // PrimaryFielder   : 타구 1차 처리자
    ASSIST,      // AssistingFielder : 바운드/충돌 대비 근접 보조
    BACKUP,      // Backup           : 송구/타구 후방 백업
    BASE_COVER,  // BaseCover        : 베이스 커버
    CUTOFF,      // CutoffMan        : 외야-홈 송구선을 자르는 중계
    RELAY,       // RelayMan         : 깊은 외야 장거리 중계
    OBSERVER     // DecoyOrObserver  : 제자리 주시 / 주자 견제 감시
}

public class BaseballPlayer : MonoBehaviour
{
    protected PLAYER_TYPE _type;
    public PLAYER_TYPE Type => _type;

    // ── 하위 호환용 (기존 코드가 참조) ─────────────────────
    public DEFENSIVE_ROLE DefRole = DEFENSIVE_ROLE.IDLE;
    public Vector3 RoleTargetPosition = Vector3.zero;

    // ── 새 수비 시스템 ───────────────────────────────────
    FieldAssignment _assignment;
    FielderAttributes _attributes;

    /// <summary>조율자가 내려준 현재 수비 지시.</summary>
    public FieldAssignment Assignment => _assignment;

    /// <summary>포지션별 수비 능력치.</summary>
    public FielderAttributes Attributes
    {
        get
        {
            if (_attributes == null || _attributes.Position != _type)
                _attributes = FielderAttributes.For(_type);
            return _attributes;
        }
    }

    /// <summary>수비 시작 위치(시프트/뎁스가 적용된 값).</summary>
    public Vector3 HomePosition { get; private set; }

    /// <summary>기본(시프트 적용 전) 수비 위치.</summary>
    public Vector3 DefaultPosition { get; private set; }

    /// <summary>지금 달리고 있는가(인터셉트 계산에서 반응 시간 생략 여부).</summary>
    public bool IsRunningNow
    {
        get
        {
            Defencer d = _role as Defencer;
            return d != null && d.IsRunning;
        }
    }

    public Team MyTeam { get; private set; }
    public Rigidbody Rig { get; private set; }

    public void SetAssignment(FieldAssignment a)
    {
        _assignment = a;
        // 하위 호환 필드도 함께 갱신
        DefRole = a.Role;
        RoleTargetPosition = a.Target;
    }

    /// <summary>수비 시프트/뎁스 적용. offset 은 필드 기준 월드 오프셋.</summary>
    public void SetFieldingPosition(Vector3 worldPos)
    {
        HomePosition = worldPos;
    }

    public void ResetFieldingPosition()
    {
        HomePosition = DefaultPosition;
    }

    /// <summary>이 포지션의 수비 존. 서브클래스에서 오버라이드 가능.</summary>
    public virtual DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateForPosition(_type);
    }

    // 포지션별 특수 역할은 FieldingCoordinator 로 이관되었다.
    // 커스텀이 필요하면 여기를 오버라이드해서 조율자 결과를 덮어쓸 수 있다.
    public virtual bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos) { return false; }
    public virtual bool AssignSpecialRole(BaseballPlayer chaser, Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        return AssignSpecialRole(chaser, landPos);
    }

    public void SetPlayerType(PLAYER_TYPE type)
    {
        _type = type;
        _attributes = FielderAttributes.For(type);
    }

    protected Ball _ball;

    protected float _moveSpeed;
    protected float _height = 1.8f;

    protected Vector3 _initPos;

    /// <summary>Defencer class | Attacker class</summary>
    protected PlayerRole _role;
    protected Animator _ani;

    Transform _ballPool;

    void Start()
    {
    }

    void Update()
    {
        update();
    }

    private void FixedUpdate()
    {
        fixedUpdate();
    }

    void LateUpdate()
    {
        // 절차적 모션은 Animator 가 본을 쓴 뒤에 적용해야 덮어써지지 않는다.
        if (_role != null) _role.lateUpdate();
    }

    protected virtual void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _initPos = transform.position;
        DefaultPosition = _initPos;
        HomePosition = _initPos;
        _moveSpeed = 4f;
        _ball = _ball != null ? _ball : null;

        if (transform.parent != null)
        {
            MyTeam = transform.parent.GetComponent<Team>();
        }
        Rig = GetComponent<Rigidbody>();

        _assignment = FieldAssignment.Idle(_initPos);

        _role.init(_height, transform.GetChild(0).GetChild(1).GetChild(0), _type, _ball, transform, bT);
        setRoleController();
    }

    protected virtual void setRoleController(Transform trans = null)
    {
        _role.SetController(_ballPool, trans == null ? _role.GetItemTransform() : trans, _ball.transform);
    }

    public bool IsActiveRunner => _role is Attacker att && att.IsOnBaseOrRunning;

    public virtual void TriggerAReset()
    {
        if (!IsActiveRunner)
        {
            transform.position = HomePosition;
            _role.movementInit();
            _role.ResetRole();
        }

        if (_role is Attacker att)
        {
            att.TriggerAReset();
        }
    }

    protected virtual void update()
    {
        _role.update(_moveSpeed);
    }

    protected virtual void fixedUpdate()
    {
        _role.fixedUpdate();
    }

    public Transform GetItem()
    {
        return _role.GetItemTransform();
    }

    public PlayerRole GetRole()
    {
        return _role;
    }

    public void EnterTheGame(Transform pool, Ball ball, bool isAttacker)
    {
        _ani = GetComponent<Animator>();
        _ballPool = pool;
        focusBall(ball, isAttacker ? (PlayerRole)new Attacker() : (PlayerRole)new Defencer());
        setSide(isAttacker);

        init();
    }

    public void JoinGame()
    {
        gameObject.SetActive(true);
        transform.position = BasePositionProvider.provider.HitterBox.position;
        transform.rotation = BasePositionProvider.provider.HitterBox.rotation;
        _initPos = transform.position;
        HomePosition = transform.position; // 타석 위치를 리셋 기준점으로 삼도록 추가
        setSide(true);
    }

    public void ExitGame()
    {
        gameObject.SetActive(false);
    }

    public void ResetPosition()
    {
        DefRole = DEFENSIVE_ROLE.IDLE;
        RoleTargetPosition = Vector3.zero;
        _assignment = FieldAssignment.Idle(HomePosition);
        transform.position = HomePosition;

        if (Rig != null)
        {
            Rig.linearVelocity = Vector3.zero;
        }

        if (_role != null)
        {
            _role.ResetRole();
        }
    }

    void focusBall(Ball ball, PlayerRole role)
    {
        _ball = ball;
        _role = role;
        _ball.AddListener((Vector3 startPos, Vector3 endPos, Vector3 inc) =>
        {
            _role.BallEventListener(transform.position, startPos, endPos, inc);
        });
    }

    void setSide(bool isAttackSide)
    {
        if (_ani != null) _ani.SetBool("isAttacker", isAttackSide);
    }

    public void Increase(int i)
    {
        _role.Increase(i);
    }

    private void OnCollisionEnter(Collision collision)
    {
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_role != null) _role.OnTriggerEnter(other);
    }
}
