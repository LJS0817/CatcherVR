using UnityEngine;

public class SwingController : RoleController
{
    Vector3 _swingStartPos;
    bool _isSwing;
    Vector3 _prevPos;
    int _index;
    Ball _ball;
    bool _chooseNotTo;
    
    // 타격 타이밍 및 스크립트 기반 스윙 궤적을 위한 변수들
    bool _decisionMade;
    float _timingError;
    float _swingTimer;
    Vector3 _targetSwingPos;

    float _timeDifferenceAtDecision;
    bool _isStrikeAtDecision;
    bool _hitEvaluated;
    
    float _decisionDistance = 6.0f; // 동적 결정 거리 (6미터)
    float _dynamicHitTime; // 동적으로 계산된 타격 도달 시간
    
    PlayerRole _role;

    public SwingController(Transform offset, Transform point, Transform target, float speed = 0.135f, PlayerRole role = null)
    {
        base.Init(offset, point, target, speed);

        _role = role;

        _ball = target.GetComponent<Ball>();

        _isSwing = false;
        _prevPos = Vector3.zero;
        _isSwing = false;
        
        // 선구안 스탯 (예: 0.75 = 75% 확률로 정확한 판단, 25% 확률로 착각)
        // _chooseNotTo가 true가 되면, 스트라이크를 볼로 착각해 안 치거나, 볼을 스트라이크로 착각해 헛스윙(Chase)하게 됩니다.
        float plateDiscipline = 0.75f;
        _chooseNotTo = (Random.value > plateDiscipline); 
        
        _decisionMade = false;
        _timingError = Random.Range(-0.02f, 0.02f); 
        _swingTimer = -1f;
        _hitEvaluated = false;

        _itemPoint.position = getOffsetPosition(0);
        _swingStartPos = getOffsetPosition(2);
        if (speed > 1f) _defaultSpeed = 0.135f;
    }

    public override void Action(Transform my)
    {
        calculateSwingPosition(my);
    }

    public override void ResetValue(int i=0)
    {
        if (i == 0)
        {
            _prevPos = Vector3.zero;
            _isSwing = false;
            
            float plateDiscipline = 0.75f;
            _chooseNotTo = (Random.value > plateDiscipline); 
            
            _decisionMade = false;
            _timingError = Random.Range(-0.02f, 0.02f); 
            _swingTimer = -1f;
            _hitEvaluated = false;
        }
        _index = i;
    }

    Vector3 getOffsetPosition(int idx)
    {
        if (idx < 0 || idx >= _pool.childCount) return _pool.GetChild(_pool.childCount - 1).position;
        return _pool.GetChild(idx).position;
    }

    void calculateSwingPosition(Transform my)
    {
        if (!_isSwing && !_decisionMade)
        {
            float z = my.position.z - _ball.GetPosition().z;
            if (z < 2f) return;
            if (_prevPos != Vector3.zero && z < 10f)
            {
                // 공이 결정 구간(_decisionDistance)에 도달했을 때 판정
                if (z < _decisionDistance)
                {
                    float timeToPlate = z / _ball.GetVelocity().z;
                    
                    // 타자는 공이 도착할 시간에 맞춰 스윙을 계획하지만, _timingError 만큼 오차가 발생
                    _dynamicHitTime = timeToPlate + _timingError;
                    if (_dynamicHitTime < 0.05f) _dynamicHitTime = 0.05f; // 지나치게 빠른 스윙 방어

                    Vector3 dir = _ball.GetVelocity().normalized;
                    Vector3 pos = _ball.GetPosition() + dir * z;
                    pos.z = my.position.z;
                    
                    bool isStrike = CountsProvider.provider.ContainsStrikeZone(pos);
                    _decisionMade = true;
                    
                    _timeDifferenceAtDecision = _timingError; // 오차가 곧 실제 타격 타이밍 빗나감
                    _isStrikeAtDecision = isStrike;

                    if (isStrike)
                    {
                        if (!_chooseNotTo) swing(pos, my);
                    }
                    else
                    {
                        if (_chooseNotTo) swing(pos, my);
                    }
                }
            }
            if (z < 10f)
            {
                if (_prevPos == Vector3.zero) _chooseNotTo = Random.Range(0, 1f) < 0.2f; // 에러 확률 증가
                _prevPos = _ball.GetPosition();
            }
        }
        
        // 2. 스크립트 기반 스윙 보간(Lerp) 적용
        if (_isSwing && _swingTimer >= 0f)
        {
            _swingTimer += Time.fixedDeltaTime;
            
            // _dynamicHitTime 시점에 타격 판정 (기존 0.135f 대신)
            if (!_hitEvaluated && _swingTimer >= _dynamicHitTime)
            {
                _hitEvaluated = true;
                EvaluateHit(my);
            }
            
            UpdateScriptedSwing();
        }
        else if (!_isSwing)
        {
            // 스윙 전 준비자세 유지
            _itemPoint.position = Vector3.MoveTowards(_itemPoint.position, getOffsetPosition(0), 10f * Time.fixedDeltaTime);
        }
    }

    void UpdateScriptedSwing()
    {
        // 기존 0.135f에 타격점에 도달하던 스윙을 동적 시간(_dynamicHitTime)에 맞게 스케일링
        float scale = _dynamicHitTime / 0.135f;
        float[] t = { 0f, 0.05f * scale, 0.135f * scale, 0.17f * scale, 0.22f * scale };
        
        if (_swingTimer <= t[1])
        {
            float ratio = (_swingTimer - t[0]) / (t[1] - t[0]);
            _itemPoint.position = Vector3.Lerp(getOffsetPosition(0), getOffsetPosition(1), Smooth(ratio));
        }
        else if (_swingTimer <= t[2])
        {
            float ratio = (_swingTimer - t[1]) / (t[2] - t[1]);
            _itemPoint.position = Vector3.Lerp(getOffsetPosition(1), getOffsetPosition(2), Smooth(ratio));
        }
        else if (_swingTimer <= t[3])
        {
            float ratio = (_swingTimer - t[2]) / (t[3] - t[2]);
            _itemPoint.position = Vector3.Lerp(getOffsetPosition(2), getOffsetPosition(3), Smooth(ratio));
        }
        else if (_swingTimer <= t[4])
        {
            float ratio = (_swingTimer - t[3]) / (t[4] - t[3]);
            _itemPoint.position = Vector3.Lerp(getOffsetPosition(3), getOffsetPosition(4), Smooth(ratio));
        }
        else
        {
            // 팔로스루 종료
            _itemPoint.position = getOffsetPosition(4);
            _swingTimer = -1f; 
        }
    }

    float Smooth(float ratio)
    {
        return ratio * ratio * (3f - 2f * ratio);
    }

    void EvaluateHit(Transform my)
    {
        float absDiff = Mathf.Abs(_timeDifferenceAtDecision);
        Vector3 currentVel = _ball.GetVelocity();
        
        if (absDiff < 0.015f) // 정타 (Fair Hit)
        {
            _ball.SetBattedBall(true);
            float speedMultiplier = Random.Range(1.2f, 2.5f);
            Vector3 hitVel = new Vector3(
                Random.Range(-10f, 10f), 
                Random.Range(5f, 25f), 
                -Mathf.Abs(currentVel.z) * speedMultiplier
            );
            _ball.GetComponent<Rigidbody>().linearVelocity = hitVel;
            _ball.LostBall();
            
            // 타자에게 주루 시작 알림
            if (_role is Attacker attacker)
            {
                attacker.OnHitBall();
            }
        }
        else if (absDiff < 0.03f) // 파울 (Foul)
        {
            _ball.SetBattedBall(true);
            _ball.IsFoul = true; // 파울볼 플래그 설정
            Vector3 foulVel = new Vector3(
                (Random.value > 0.5f ? 15f : -15f), 
                Random.Range(10f, 20f), 
                Mathf.Abs(currentVel.z) * 0.5f
            );
            _ball.GetComponent<Rigidbody>().linearVelocity = foulVel;
            _ball.LostBall();
        }
        else
        {
            // 헛스윙 (Miss) - 공을 건드리지 않음
            if (_isStrikeAtDecision)
            {
                // 스트라이크 판정은 기존에 OnTriggerEnter 등에서 되거나 여기서 처리
            }
        }
    }

    void swing(Vector3 pos, Transform my)
    {
        _isSwing = true;
        _swingTimer = 0f;

        // 애니메이션 스피드를 스크립트 스윙 시간에 맞게 동적 조절 (기본 0.135초 기준)
        float animSpeed = 0.135f / _dynamicHitTime;
        my.GetComponent<Animator>().SetFloat("SwingSpeed", animSpeed);
        my.GetComponent<Animator>().SetTrigger("Activate");

        // 실제 공의 궤적으로 타격점 세팅
        _targetSwingPos = pos;
        _pool.GetChild(2).position = pos;
    }
}
