using UnityEngine;

public class Attacker : PlayerRole
{
    float _swingSpeed;
    bool _counted;

    // 주루(Baserunning) 변수
    float _currentSpeed;
    bool _isOverrunning;
    Vector3 _runTarget;

    BASE_TYPE _finalTargetBase;
    bool _isRounding;
    float _startDelayTimer;

    /// <summary>출루 또는 주루 중인지 여부 플래그.</summary>
    public bool IsOnBaseOrRunning { get; private set; } = false;
    public BASE_TYPE TargetBase => _base;

    // ── 수비 AI 가 참조하는 주루 상태 (RunnerTracker 의 ETA 계산 입력) ──
    /// <summary>현재 주루 속도(씬 단위 m/s).</summary>
    public float RunSpeed => _currentSpeed;
    /// <summary>아직 스타트 딜레이 중인가.</summary>
    public bool IsStarting => _startDelayTimer > 0f;
    /// <summary>남은 스타트 딜레이(초).</summary>
    public float StartDelayRemaining => Mathf.Max(0f, _startDelayTimer);
    /// <summary>최종 목표 베이스(장타 시 2·3루까지).</summary>
    public BASE_TYPE FinalTargetBase => _finalTargetBase;

    /// <summary>
    /// 이 주자가 마지막으로 베이스에 발이 닿은 시각(Time.time). 아직 닿지 않았으면 0.
    /// 설계도 10.4 의 <c>runner.BaseTouchTimestamp</c> — 포스 아웃 정밀 판정의 입력이다.
    /// </summary>
    public float BaseTouchTimestamp { get; private set; }

    /// <summary>베이스 접촉 시각을 기록한다(같은 베이스에 대해 최초 1회).</summary>
    void StampBaseTouch()
    {
        BaseTouchTimestamp = Time.time;
        Debug.Log($"[BaseTouch] 주자({_type})가 {_base} 베이스를 터치함! (Time: {Time.time:F3})");
    }

    float RunMaxSpeed { get { return FieldMetrics.M(DefenseTuning.RUNNER_TOP_SPEED); } }
    float RunAccel { get { return FieldMetrics.M(DefenseTuning.RUNNER_ACCEL); } }

    /// <summary>주자가 베이스 위에 안전하게 안착해 있어 태그 아웃을 피할 수 있는지.</summary>
    public bool IsSafeOnBase()
    {
        if (!IsOnBaseOrRunning) return false;
        if (getMovement().isMoving() || _startDelayTimer > 0f) return false;

        if (_isOverrunning) return true;

        Vector3 basePos = BasePositionProvider.provider.GetBasePosition(_base);
        float dist = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(basePos.x, 0, basePos.z));
        return dist < FieldMetrics.M(0.9f);
    }

    public override void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        base.init(h, tool, t, ball, player, bT);

        _item = _item.GetChild(1);
        _item.gameObject.SetActive(true);

        _movement = new RunnerMovement();
        _counted = false;
        IsOnBaseOrRunning = false;

        setSwingSpeed(1f);

        addEvent();
    }

    public override void SetController(Transform pool, Transform point, Transform target, float speed = 0.135f)
    {
        _controller = new SwingController(_offsets.GetChild(2), point, _ball.transform, 0.135f, this);
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

    public void TriggerAReset()
    {
        if (!IsOnBaseOrRunning)
        {
            _base = BASE_TYPE.E_SELF;
            _counted = false;
            _currentSpeed = 0f;
            _isOverrunning = false;
            _isRounding = false;
            _startDelayTimer = 0f;
            BaseTouchTimestamp = 0f;
            if (_item != null) _item.gameObject.SetActive(true);
            GetController().ResetValue();
        }
    }

    protected override void Move(float speed)
    {
        if (_startDelayTimer > 0f)
        {
            _startDelayTimer -= Time.deltaTime;
            if (_startDelayTimer <= 0f)
            {
                if (_ani != null) _ani.SetBool("IsRunning", true);
                setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
            }
            return;
        }

        if (!getMovement().isMoving()) return;
        if (getMovement().CompareMovementType(MOVEMENT_TYPE.E_BASE)) return;

        // 가속 → 최고 속도. RunnerTracker 의 ETA 계산과 동일한 모델을 쓴다.
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, RunMaxSpeed, RunAccel * Time.deltaTime);

        Vector3 toTarget = _runTarget - _my.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist > 0.01f)
        {
            Vector3 dir = toTarget / dist;
            Vector3 step = dir * Mathf.Min(_currentSpeed * Time.deltaTime, dist);
            _my.position += step;

            _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 12f);
        }

        dist = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(_runTarget.x, 0, _runTarget.z));

        // 라운딩(바나나 궤적) : 1루를 스쳐 지나가며 다음 베이스로
        if (_isRounding && dist < FieldMetrics.M(3.5f))
        {
            _isRounding = false;
            _base = BasePositionProvider.provider.GetNextBase(_base);
            BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
            _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
            setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
            return;
        }

        if (dist >= FieldMetrics.M(0.6f)) return;

        if (_base == _finalTargetBase)
        {
            if (_base == BASE_TYPE.E_HOME_BASE)
            {
                IsOnBaseOrRunning = false;
                getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                if (_ani != null) _ani.SetBool("IsRunning", false);
                _currentSpeed = 0f;
                BasePositionProvider.provider.SetAttackerBaseState(BASE_TYPE.E_HOME_BASE, -1);

                if (_bp != null) _bp.ExitGame();
            }
            else if (_base == BASE_TYPE.E_FIRST_BASE)
            {
                if (_isOverrunning)
                {
                    getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                    if (_ani != null) _ani.SetBool("IsRunning", false);
                    _currentSpeed = 0f;
                    _my.position = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
                    BasePositionProvider.provider.SetAttackerBaseState(BASE_TYPE.E_FIRST_BASE, (int)_type);
                    IsOnBaseOrRunning = true;
                    StampBaseTouch();
                }
                else
                {
                    _isOverrunning = true;
                    StampBaseTouch();       // 1루를 밟고 지나가는 순간이 터치 시점이다
                    Vector3 overrunDir = (_runTarget - _my.position).normalized;
                    if (overrunDir == Vector3.zero) overrunDir = _my.forward;
                    _runTarget = _runTarget + overrunDir * FieldMetrics.M(3.5f);
                    setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
                }
            }
            else
            {
                getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                if (_ani != null) _ani.SetBool("IsRunning", false);
                _currentSpeed = 0f;
                _my.position = _runTarget;
                BasePositionProvider.provider.SetAttackerBaseState(_base, (int)_type);
                IsOnBaseOrRunning = true;
                StampBaseTouch();
            }
        }
        else
        {
            _base = BasePositionProvider.provider.GetNextBase(_base);
            BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
            _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
            setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
        }
    }

    public override void fixedUpdate()
    {
        base.fixedUpdate();
    }

    protected override void PhysicsMoves()
    {
        if (!IsOnBaseOrRunning && _my != null)
        {
            GetController().Action(_my);
        }
    }

    public override void Increase(int i)
    {
        GetController().ResetValue(i);
        if (i == 1 && _counted) _counted = false;
        if (!_counted && i == 3 && getMovement().CompareMovementType(MOVEMENT_TYPE.E_STAY))
        {
            _counted = true;
            if (!_ball.GetContactName().Equals("Bat") && _ball.GetVelocity().z > 0)
            {
                _ball._isSwingMiss = true;
            }
        }
    }

    public void OnHitBall()
    {
        if (_item != null) _item.gameObject.SetActive(false);

        Vector3 landPos = _ball != null ? _ball.GetLandPosition() : Vector3.zero;
        _finalTargetBase = BASE_TYPE.E_FIRST_BASE;

        float carry = FieldMetrics.DistFromHome(landPos);
        float lateral = Mathf.Abs(FieldMetrics.Lateral(landPos));

        if (carry > FieldMetrics.M(62f))
        {
            _finalTargetBase = (lateral > FieldMetrics.M(18f)) ? BASE_TYPE.E_THIRD_BASE : BASE_TYPE.E_SECOND_BASE;
        }
        else if (carry > FieldMetrics.M(45f))
        {
            _finalTargetBase = BASE_TYPE.E_SECOND_BASE;
        }

        if (_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        _base = BASE_TYPE.E_FIRST_BASE;
        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));

        _isRounding = (_finalTargetBase != BASE_TYPE.E_FIRST_BASE);
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);

        if (_isRounding)
        {
            Vector3 toSecond = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE) - firstBasePos;
            toSecond.y = 0f;
            Vector3 outward = toSecond.sqrMagnitude > 0.01f ? toSecond.normalized : FieldMetrics.Forward;
            _runTarget = firstBasePos + outward * FieldMetrics.M(2.5f);
        }
        else
        {
            _runTarget = firstBasePos;
        }

        IsOnBaseOrRunning = true;
        _currentSpeed = 0f;
        _isOverrunning = false;
        BaseTouchTimestamp = 0f;
        _startDelayTimer = UnityEngine.Random.Range(0.15f, 0.25f);

        setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN, _startDelayTimer);
        if (_ani != null) _ani.SetBool("IsRunning", true);

        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.NotifyRunnersToAdvance(landPos, _bp);
        }

        // 주자가 출발했으니 수비 역할(송구 목표 베이스·커버)을 즉시 다시 계산한다.
        GamePlayerProvider gp = GamePlayerProvider.provider;
        if (gp != null && gp.DefenseTeam != null) gp.DefenseTeam.ForceReevaluate();
    }

    /// <summary>후속 타자가 타격했을 때 기존 주자가 진루.</summary>
    public void AdvanceRunnerOnNextHit(Vector3 hitLandPos)
    {
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
        Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);

        Vector3 flat = new Vector3(_my.position.x, 0, _my.position.z);
        float d1 = Vector3.Distance(flat, new Vector3(firstBasePos.x, 0, firstBasePos.z));
        float d2 = Vector3.Distance(flat, new Vector3(secondBasePos.x, 0, secondBasePos.z));
        float d3 = Vector3.Distance(flat, new Vector3(thirdBasePos.x, 0, thirdBasePos.z));

        float near = FieldMetrics.M(7f);
        BASE_TYPE currentOccupiedBase;
        if (d3 < near && d3 < d1 && d3 < d2) currentOccupiedBase = BASE_TYPE.E_THIRD_BASE;
        else if (d2 < near && d2 < d1) currentOccupiedBase = BASE_TYPE.E_SECOND_BASE;
        else currentOccupiedBase = BASE_TYPE.E_FIRST_BASE;

        _base = currentOccupiedBase;

        float carry = FieldMetrics.DistFromHome(hitLandPos);

        if (_base == BASE_TYPE.E_THIRD_BASE)
        {
            _finalTargetBase = BASE_TYPE.E_HOME_BASE;
        }
        else if (_base == BASE_TYPE.E_SECOND_BASE)
        {
            _finalTargetBase = (carry > FieldMetrics.M(55f)) ? BASE_TYPE.E_HOME_BASE : BASE_TYPE.E_THIRD_BASE;
        }
        else
        {
            _finalTargetBase = (carry > FieldMetrics.M(62f)) ? BASE_TYPE.E_THIRD_BASE : BASE_TYPE.E_SECOND_BASE;
        }

        BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        _base = BasePositionProvider.provider.GetNextBase(_base);
        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));

        _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
        _isRounding = (_finalTargetBase != _base);
        _currentSpeed = 0f;
        _isOverrunning = false;
        IsOnBaseOrRunning = true;
        _startDelayTimer = 0.15f;

        setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN, _startDelayTimer);
        if (_ani != null) _ani.SetBool("IsRunning", true);
    }

    public void OnOut()
    {
        IsOnBaseOrRunning = false;
        if (_base != BASE_TYPE.E_SELF)
        {
            BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        }
        getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
        _currentSpeed = 0f;
        if (_ani != null) _ani.SetBool("IsRunning", false);
        if (_bp != null)
        {
            _bp.ExitGame();
        }
    }

    public override void OnTriggerEnter(Collider collider)
    {
        if (collider.transform.tag.Equals("Base"))
        {
            BasePositionProvider.provider.SetAttackerBaseState(_base, (int)_type);
            StampBaseTouch();
        }
    }

    public override string ToString()
    {
        return "Att";
    }
}
