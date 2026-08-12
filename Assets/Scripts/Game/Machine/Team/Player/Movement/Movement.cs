using UnityEngine;

public enum MOVEMENT_TYPE
{
    E_STAY,

    E_END_POINT,
    E_JUMP,
    E_PREDICT_PATH,
    E_FOLLOW_BALL,

    E_BASE,
    E_PRE_RUN,
    E_RUN,
}

public class Movement
{
    protected Vector3 _target;
    protected MOVEMENT_TYPE _type;
    
    // 관성 및 물리 기반 이동 변수
    protected Vector3 _currentVelocity;
    protected float _delayTimer;
    protected bool _isDelaying;

    public virtual void init()
    {
        _target = Vector3.zero;
        _type = MOVEMENT_TYPE.E_STAY;
        _currentVelocity = Vector3.zero;
        _delayTimer = 0f;
        _isDelaying = false;
    }

    public void SetMovementType(MOVEMENT_TYPE mT) { _type = mT; }
    public bool CompareMovementType(MOVEMENT_TYPE t) { return _type == t; }
    public MOVEMENT_TYPE GetMovementType() { return _type; }

    public Vector3 GetTarget() { return _target; }

    public virtual bool SetTarget(Vector3 target, Vector3 myPos, float range, MOVEMENT_TYPE mT) 
    { 
        return false; 
    }

    protected void setTarget(Vector3 vec) 
    { 
        _target = vec; 
    }

    // 새로운 목표 설정 시 딜레이(첫 발 멈칫)를 줄지 여부 설정
    public void SetTargetWithDelay(Vector3 vec, float delayTime)
    {
        _target = vec;
        if (delayTime > 0f)
        {
            _isDelaying = true;
            _delayTimer = delayTime;
        }
    }

    public bool isMoving() { return _target != Vector3.zero && !CompareMovementType(MOVEMENT_TYPE.E_STAY); }

    // 관성을 적용한 부드러운 이동 계산
    public virtual Vector3 GetMovementPosition(Vector3 myPos, Vector3 target, float targetSpeed, float acceleration = 15f) 
    {
        if (_isDelaying)
        {
            _delayTimer -= Time.deltaTime;
            if (_delayTimer <= 0f)
            {
                _isDelaying = false;
            }
            else
            {
                // 딜레이 중에는 속도가 0으로 수렴
                _currentVelocity = Vector3.MoveTowards(_currentVelocity, Vector3.zero, acceleration * Time.deltaTime);
                return myPos + _currentVelocity * Time.deltaTime;
            }
        }

        Vector3 desiredDirection = (target - myPos);
        float distance = desiredDirection.magnitude;
        
        if (distance > 0.01f)
        {
            desiredDirection.Normalize();
            // 목표지점에 가까워지면 감속 (도착 부드럽게)
            float speedModifier = Mathf.Clamp01(distance / 1.5f);
            Vector3 desiredVelocity = desiredDirection * (targetSpeed * speedModifier);
            
            // 현재 속도에서 목표 속도로 가속(Acceleration)
            _currentVelocity = Vector3.MoveTowards(_currentVelocity, desiredVelocity, acceleration * Time.deltaTime);
        }
        else
        {
            _currentVelocity = Vector3.MoveTowards(_currentVelocity, Vector3.zero, acceleration * Time.deltaTime);
        }

        return myPos + _currentVelocity * Time.deltaTime;
    }
}
