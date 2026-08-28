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

    // 출루 또는 주루 중인지 여부 플래그 (다음 타자 교체 시 퇴장 방지)
    public bool IsOnBaseOrRunning { get; private set; } = false;
    public BASE_TYPE TargetBase => _base;

    /// <summary>
    /// 주자가 현재 베이스 위에 안전하게 안착해 있어 태그 아웃을 피할 수 있는지 여부
    /// </summary>
    public bool IsSafeOnBase()
    {
        if (!IsOnBaseOrRunning) return false;
        if (getMovement().isMoving() || _startDelayTimer > 0f) return false;

        // 1루 오버런 중 안전 지대
        if (_isOverrunning) return true;

        // 베이스 위에 완전히 멈춰있는지 확인
        Vector3 basePos = BasePositionProvider.provider.GetBasePosition(_base);
        float dist = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(basePos.x, 0, basePos.z));
        return dist < 0.65f;
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

    public override void SetController(Transform pool, Transform point, Transform target, float speed=0.135f)
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

    protected override void Move(float speed)
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            // 타석에 서 있는 타자만 카운트 리셋 (베이스에 출루한 주자는 리셋하지 않음)
            if (!IsOnBaseOrRunning)
            {
                _base = BASE_TYPE.E_SELF;
                _counted = false;
                GetController().ResetValue();
            }
        }

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

        if (getMovement().isMoving())
        {
            if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_BASE))
            {
                // 가속도 적용 (초기 0에서 최고 속도 5.5m/s까지)
                float runMaxSpeed = (speed > 0f) ? speed * 1.3f : 5.5f;
                _currentSpeed = Mathf.MoveTowards(_currentSpeed, runMaxSpeed, Time.deltaTime * 18f);
                
                // 이동 처리
                _my.position = getMovement().GetMovementPosition(_my.position, _runTarget, _currentSpeed, 20f);
                
                // 회전 처리 (달리는 방향으로 정렬)
                Vector3 dir = (_runTarget - _my.position).normalized;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.001f)
                {
                    _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 12f);
                }
                
                float dist = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(_runTarget.x, 0, _runTarget.z));
                
                // 라운딩(바나나 궤적) 중일 때는 1루를 정확히 안 밟고 스쳐 지나가며 2루 타겟으로 변경
                if (_isRounding && dist < 3.0f)
                {
                    _isRounding = false;
                    _base = BasePositionProvider.provider.GetNextBase(_base);
                    BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
                    _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
                    setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
                    return;
                }

                // 일반 베이스 도달 판정
                if (dist < 0.6f)
                {
                    if (_base == _finalTargetBase)
                    {
                        if (_base == BASE_TYPE.E_HOME_BASE)
                        {
                            // 홈에 도달하여 득점 완료 -> 주루 종료 및 선수 퇴장
                            IsOnBaseOrRunning = false;
                            getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                            if (_ani != null) _ani.SetBool("IsRunning", false);
                            _currentSpeed = 0f;
                            BasePositionProvider.provider.SetAttackerBaseState(BASE_TYPE.E_HOME_BASE, -1);

                            if (_bp != null)
                            {
                                _bp.ExitGame();
                            }
                        }
                        else if (_base == BASE_TYPE.E_FIRST_BASE)
                        {
                            // 1루 도달 (오버런 허용 후 1루 베이스로 복귀하여 안착)
                            if (_isOverrunning)
                            {
                                getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                                if (_ani != null) _ani.SetBool("IsRunning", false);
                                _currentSpeed = 0f;
                                _my.position = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
                                BasePositionProvider.provider.SetAttackerBaseState(BASE_TYPE.E_FIRST_BASE, (int)_type); // 1루 안착
                                IsOnBaseOrRunning = true; // 출루 유지
                            }
                            else
                            {
                                _isOverrunning = true;
                                Vector3 overrunDir = (_runTarget - _my.position).normalized;
                                if (overrunDir == Vector3.zero) overrunDir = _my.forward;
                                _runTarget = _runTarget + overrunDir * 3f;
                                setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
                            }
                        }
                        else
                        {
                            // 2루나 3루는 베이스 위에서 정지하여 출루 유지
                            getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                            if (_ani != null) _ani.SetBool("IsRunning", false);
                            _currentSpeed = 0f;
                            _my.position = _runTarget;
                            BasePositionProvider.provider.SetAttackerBaseState(_base, (int)_type); // 2루/3루 안착
                            IsOnBaseOrRunning = true; // 출루 유지
                        }
                    }
                    else
                    {
                        // 다음 베이스로 계속 주루 (1루 -> 2루, 2루 -> 3루, 3루 -> 홈)
                        _base = BasePositionProvider.provider.GetNextBase(_base);
                        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
                        _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
                        setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
                    }
                }
            }
        }
    }

    public override void fixedUpdate()
    {
        base.fixedUpdate();
    }

    protected override void PhysicsMoves()
    {
        // 타석에 서 있는 타자만 스윙 컨트롤러 물리 액션 실행
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
            if(!_ball.GetContactName().Equals("Bat") && _ball.GetVelocity().z > 0)
            {
                _ball._isSwingMiss = true;
            }
        }
    }

    public void OnHitBall()
    {
        // 1. 배트 숨기기
        if (_item != null) _item.gameObject.SetActive(false);
        
        // 2. 장타 판단 (Extra-base Hit Judgment)
        Vector3 landPos = _ball != null ? _ball.GetLandPosition() : Vector3.zero;
        _finalTargetBase = BASE_TYPE.E_FIRST_BASE;
        
        if (landPos.z > 35f) // 펜스 근처 깊은 타구
        {
            if (Mathf.Abs(landPos.x) > 10f) _finalTargetBase = BASE_TYPE.E_THIRD_BASE; // 좌/우중간 3루타 코스
            else _finalTargetBase = BASE_TYPE.E_SECOND_BASE; // 2루타 코스
        }
        else if (landPos.z > 20f && Mathf.Abs(landPos.x) > 12f) // 짧지만 라인선상 빈공간
        {
            _finalTargetBase = BASE_TYPE.E_SECOND_BASE;
        }

        // 3. 1루 베이스 목표 설정 (타자는 타격 직후 무조건 1루를 향해 질주!)
        if (_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        _base = BASE_TYPE.E_FIRST_BASE; 
        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
        
        _isRounding = (_finalTargetBase != BASE_TYPE.E_FIRST_BASE);
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        
        if (_isRounding)
        {
            _runTarget = firstBasePos + new Vector3(2f, 0, -2f);
        }
        else
        {
            _runTarget = firstBasePos;
        }
        
        // 4. 주루 초기화 및 즉시 1루 타겟 설정 (출루/주루 플래그 활성화)
        IsOnBaseOrRunning = true;
        _currentSpeed = 0f;
        _isOverrunning = false;
        _startDelayTimer = UnityEngine.Random.Range(0.15f, 0.25f); // 타격 직후 첫 발 딜레이
        
        setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN, _startDelayTimer);
        if (_ani != null) _ani.SetBool("IsRunning", true);

        // 5. 같은 팀의 기존 출루 주자들에게도 즉시 진루 명령 전파!
        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.NotifyRunnersToAdvance(landPos, _bp);
        }
    }

    /// <summary>
    /// 후속 타자가 타격했을 때, 이미 출루해 있던 주자가 전방 베이스로 진루
    /// </summary>
    public void AdvanceRunnerOnNextHit(Vector3 hitLandPos)
    {
        // 1. 현재 주자의 베이스 위치 확인 (물리적 위치 기반으로도 판별)
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        Vector3 secondBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
        Vector3 thirdBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_THIRD_BASE);

        float d1 = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(firstBasePos.x, 0, firstBasePos.z));
        float d2 = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(secondBasePos.x, 0, secondBasePos.z));
        float d3 = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(thirdBasePos.x, 0, thirdBasePos.z));

        BASE_TYPE currentOccupiedBase = BASE_TYPE.E_FIRST_BASE;
        if (d3 < 5.0f && d3 < d1 && d3 < d2) currentOccupiedBase = BASE_TYPE.E_THIRD_BASE;
        else if (d2 < 5.0f && d2 < d1) currentOccupiedBase = BASE_TYPE.E_SECOND_BASE;
        else currentOccupiedBase = BASE_TYPE.E_FIRST_BASE;

        _base = currentOccupiedBase;

        // 2. 타구 비거리에 따른 최종 목표 베이스 결정
        if (_base == BASE_TYPE.E_THIRD_BASE)
        {
            _finalTargetBase = BASE_TYPE.E_HOME_BASE; // 3루 주자는 홈으로 (득점)
        }
        else if (_base == BASE_TYPE.E_SECOND_BASE)
        {
            _finalTargetBase = (hitLandPos.z > 30f) ? BASE_TYPE.E_HOME_BASE : BASE_TYPE.E_THIRD_BASE;
        }
        else if (_base == BASE_TYPE.E_FIRST_BASE)
        {
            _finalTargetBase = (hitLandPos.z > 35f) ? BASE_TYPE.E_THIRD_BASE : BASE_TYPE.E_SECOND_BASE;
        }

        // 3. 기존 베이스 상태 비우고 다음 베이스로 진루 개시
        BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        _base = BasePositionProvider.provider.GetNextBase(_base);
        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));

        _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
        _isRounding = (_finalTargetBase != _base);
        _currentSpeed = 0f;
        _isOverrunning = false;
        IsOnBaseOrRunning = true;
        _startDelayTimer = 0.15f; // 타구 보고 즉시 스타트

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
        }
    }

    public override string ToString()
    {
        return "Att";
    }
}
