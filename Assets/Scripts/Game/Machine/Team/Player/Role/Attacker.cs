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
                    _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
                }
                
                // 오버런 및 도착 판정
                float dist = Vector3.Distance(_my.position, _runTarget);
                if (dist < 0.5f)
                {
                    if (_isOverrunning)
                    {
                        // 오버런 지점 도달 시 정지
                        getMovement().SetMovementType(MOVEMENT_TYPE.E_STAY);
                        _ani.SetBool("IsRunning", false);
                        _currentSpeed = 0f;
                    }
                    else
                    {
                        // 베이스 도달 시 오버런 모드 돌입
                        _isOverrunning = true;
                        // 기존 런 타겟(베이스) 너머로 3미터 추가 진행
                        Vector3 overrunDir = (_runTarget - _my.position).normalized;
                        if (overrunDir == Vector3.zero) overrunDir = _my.forward;
                        _runTarget = _runTarget + overrunDir * 3f;
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
        // 1. 배트 숨기기 및 애니메이션 전환
        _item.gameObject.SetActive(false);
        _ani.SetBool("IsRunning", true);
        
        // 2. 베이스 타겟 설정
        if(_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetAttackerBaseState(_base, -1);
        _base = BasePositionProvider.provider.GetNextBase(_base);
        BasePositionProvider.provider.SetAttackerBaseState(_base, ((int)_type + 10));
        
        _runTarget = BasePositionProvider.provider.GetBasePosition(_base);
        setMovementTarget(_runTarget, _my.position, -1f, MOVEMENT_TYPE.E_RUN);
        
        // 3. 주루 가속도 초기화
        _currentSpeed = 0f;
        _isOverrunning = false;
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
