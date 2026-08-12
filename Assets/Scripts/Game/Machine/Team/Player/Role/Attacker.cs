using UnityEngine;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;

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

    public override void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        base.init(h, tool, t, ball, player, bT);

        _item = _item.GetChild(1);
        _item.gameObject.SetActive(true);   

        _movement = new RunnerMovement();
        _counted = false;

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
        if(Input.GetKeyDown(KeyCode.A))
        {
            _base = BASE_TYPE.E_SELF;
            _counted = false;
            GetController().ResetValue();
        }

        if (_startDelayTimer > 0f)
        {
            _startDelayTimer -= Time.deltaTime;
            if (_startDelayTimer <= 0f)
            {
                _ani.SetBool("IsRunning", true);
                setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
            }
            return;
        }

        if (getMovement().isMoving())
        {
            Debug.DrawLine(_my.position, getMovement().GetTarget(), Color.red, 0.1f);
            if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_BASE))
            {
                // 가속도 적용 (초기 0에서 최고 속도 speed까지)
                _currentSpeed = Mathf.Lerp(_currentSpeed, speed, Time.deltaTime * 2f);
                
                // 이동 처리
                _my.position = getMovement().GetMovementPosition(_my.position, _runTarget, _currentSpeed);
                
                // 회전 처리 (Slerp)
                Vector3 dir = (_runTarget - _my.position).normalized;
                dir.y = 0;
                if (dir != Vector3.zero)
                {
                    _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
                }
                
                float dist = Vector3.Distance(_my.position, _runTarget);
                
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
                if (dist < 0.5f)
                {
                    if (_base == _finalTargetBase)
                    {
                        if (_base == BASE_TYPE.E_FIRST_BASE || _base == BASE_TYPE.E_HOME_BASE)
                        {
                            // 1루나 홈은 오버런 허용
                            if (_isOverrunning)
                            {
                                getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                                _ani.SetBool("IsRunning", false);
                                _currentSpeed = 0f;
                            }
                            else
                            {
                                _isOverrunning = true;
                                Vector3 overrunDir = (_runTarget - _my.position).normalized;
                                if (overrunDir == Vector3.zero) overrunDir = _my.forward;
                                _runTarget = _runTarget + overrunDir * 3f;
                            }
                        }
                        else
                        {
                            // 2루나 3루는 지나치지 않고 베이스 위에서 정확히 정지 (슬라이딩 대용)
                            getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                            _ani.SetBool("IsRunning", false);
                            _currentSpeed = 0f;
                            _my.position = _runTarget; // 베이스 위 안착
                        }
                    }
                    else
                    {
                        // 아직 최종 목적지가 아니라면 다음 베이스로 계속 뜀
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
        GetController().Action(_my);
    }

    public override void Increase(int i)
    {
        GetController().ResetValue(i);
        if (i == 1 && _counted) _counted = false;
        if (!_counted && i == 3 && getMovement().CompareMovementType(MOVEMENT_TYPE.E_STAY))
        {
            _counted = true;
            // 타격 정타(Hit)는 SwingController가 OnHitBall()을 통해 스크립트로 처리합니다.
            // 여기서는 물리 타격이 아닌 스트라이크/헛스윙만 처리합니다.
            if(!_ball.GetContactName().Equals("Bat") && _ball.GetVelocity().z > 0)
            {
                _ball._isSwingMiss = true;
            }
        }
    }

    public void OnHitBall()
    {
        // 1. 배트 숨기기 및 애니메이션 전환 (달리기 시작은 딜레이 후)
        _item.gameObject.SetActive(false);
        
        // 장타 판단 (Extra-base Hit Judgment)
        Vector3 landPos = _ball.GetLandPosition();
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

        // 2. 초기 베이스 타겟 설정 (우선 1루를 향해)
        if(_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        _base = BasePositionProvider.provider.GetNextBase(BASE_TYPE.E_HOME_BASE); // 타자는 무조건 홈에서 1루로 출발
        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
        
        // 라운딩(바나나 궤적) 설정
        _isRounding = (_finalTargetBase != BASE_TYPE.E_FIRST_BASE);
        Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(_base);
        
        if (_isRounding)
        {
            // 1루 도달 전 우측 파울라인 쪽으로 살짝 부풀려서(바나나 궤적) 뜀
            _runTarget = firstBasePos + new Vector3(3f, 0, -3f);
        }
        else
        {
            _runTarget = firstBasePos;
        }
        
        // 이동 타입 설정 (아직 안 뛰고 대기)
        getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
        
        // 3. 주루 초기화 및 딜레이 
        _currentSpeed = 0f;
        _isOverrunning = false;
        _startDelayTimer = UnityEngine.Random.Range(0.2f, 0.35f);
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
