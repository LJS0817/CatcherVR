using System;
using System.Collections;
using UnityEngine;

public class Defencer : PlayerRole
{
    float _allowAngle;

    float _readyToThrow;
    bool _hasBall;

    enum CATCH_TYPE { NONE, FOLLOW, JUMP, SLIDE, COVER, REACH }
    CATCH_TYPE _catchType = CATCH_TYPE.NONE;
    Vector3 _interceptPos;
    float _catchTimer;
    bool _hasJumped = false;
    Vector3 _startBodyPos;
    Quaternion _startBodyRot;

    // 글러브 Lerp 포구 시스템
    bool _isReachingForBall = false;
    float _gloveReachTimer = 0f;

    // 포지션별 포구 범위
    float _catchRange = 1.5f;

    bool IsOutfielder { get { return _type == PLAYER_TYPE.E_LEFT_FILED || _type == PLAYER_TYPE.E_CENTER_FIELD || _type == PLAYER_TYPE.E_RIGHT_FILED; } }
    bool IsInfielder { get { return _type == PLAYER_TYPE.E_FIRST_BASE || _type == PLAYER_TYPE.E_SECOND_BASE || _type == PLAYER_TYPE.E_SHORT_STOP || _type == PLAYER_TYPE.E_THIRD_BASE; } }

    public override void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        base.init(h, tool, t, ball, player, bT);
        _allowAngle = 0.95f;
        _movement = new CatchMovement();

        _item = _item.GetChild(0);
        _item.gameObject.SetActive(true);

        _range = new Vector3(h * 0.7f, 0.8f + h, 7.5f);

        _readyToThrow = 0f;
        _hasBall = false;
        _catchType = CATCH_TYPE.NONE;
        _isReachingForBall = false;

        // 포지션별 포구 범위 설정
        if (t == PLAYER_TYPE.E_LEFT_FILED || t == PLAYER_TYPE.E_CENTER_FIELD || t == PLAYER_TYPE.E_RIGHT_FILED)
            _catchRange = 2.2f; // 외야수: 넓은 범위
        else if (t == PLAYER_TYPE.E_FIRST_BASE || t == PLAYER_TYPE.E_SECOND_BASE || t == PLAYER_TYPE.E_SHORT_STOP || t == PLAYER_TYPE.E_THIRD_BASE)
            _catchRange = 1.5f; // 내야수: 보통
        else if (t == PLAYER_TYPE.E_PITCHER)
            _catchRange = 1.0f; // 투수: 좁게
        else
            _catchRange = 1.5f;

        if (_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetDefencerBaseState(_base, (int)t + 1);
    }

    public override void ResetRole()
    {
        base.ResetRole();
        _catchType = CATCH_TYPE.NONE;
        _hasBall = false;
        _hasJumped = false;
        _readyToThrow = 0f;
        _isReachingForBall = false;
        _gloveReachTimer = 0f;
    }

    public override void SetController(Transform pool, Transform point, Transform target, float speed=1.9f)
    {
        _controller = new PitchingController(pool, point, target, speed == 0f ? 1.9f : speed);
    }

    protected override void Move(float speed)
    {
        if (_catchType == CATCH_TYPE.JUMP)
        {
            _catchTimer += Time.deltaTime * 2.5f; 
            
            // 물리 엔진(Rigidbody)이 점프를 처리하므로 Y축 위치를 수동으로 변경하지 않음
            
            if (_catchTimer >= 0.4f && !_hasBall) 
            {
                if (Vector3.Distance(_ball.GetPosition(), _item.position) < 2.5f)
                {
                    catchBall();
                }
            }
            if (_catchTimer >= 1f)
            {
                _catchType = _hasBall ? CATCH_TYPE.NONE : CATCH_TYPE.COVER;
            }
        }
        else if (_catchType == CATCH_TYPE.SLIDE)
        {
            _catchTimer += Time.deltaTime * 2.5f;
            
            float slideProgress = Mathf.Sin(Mathf.Clamp01(_catchTimer) * Mathf.PI); 
            
            Vector3 slideDir = (_interceptPos - _startBodyPos);
            slideDir.y = 0;
            slideDir.Normalize();
            
            _my.position = Vector3.Lerp(_startBodyPos, _startBodyPos + slideDir * 2.5f, slideProgress);
            
            // Tilt body
            Quaternion targetRot = Quaternion.LookRotation(slideDir) * Quaternion.Euler(75f, 0, 0);
            _my.rotation = Quaternion.Slerp(_startBodyRot, targetRot, slideProgress);
            
            if (_catchTimer >= 0.4f && !_hasBall)
            {
                if (Vector3.Distance(_ball.GetPosition(), _item.position) < 2.5f)
                {
                    catchBall();
                }
            }
            if (_catchTimer >= 1f)
            {
                _my.position = _interceptPos;
                _my.rotation = _startBodyRot; // 슬라이딩 종료 후 원래 자세로
                _catchType = _hasBall ? CATCH_TYPE.NONE : CATCH_TYPE.COVER;
            }
        }
        else if (_catchType == CATCH_TYPE.REACH)
        {

            Vector3 lookDir = (_interceptPos - _startBodyPos);
            lookDir.y = 0;
            if (lookDir != Vector3.zero)
                _my.rotation = Quaternion.Slerp(_startBodyRot, Quaternion.LookRotation(lookDir), _catchTimer);

            if (_catchTimer >= 0.4f && !_hasBall)
            {
                if (Vector3.Distance(_ball.GetPosition(), _item.position) < 2.5f)
                {
                    catchBall();
                }
            }
            if (_catchTimer >= 1f)
            {
                _my.rotation = _startBodyRot;
                _catchType = _hasBall ? CATCH_TYPE.NONE : CATCH_TYPE.COVER;
            }
        }
        else
        {
            // Normal Movement (상황별 속도 차별화)
            if (getMovement().isMoving())
            {
                float moveSpeed = speed;
                MOVEMENT_TYPE mt = getMovement().GetMovementType();
                if (mt == MOVEMENT_TYPE.E_PREDICT_PATH || mt == MOVEMENT_TYPE.E_FOLLOW_BALL)
                    moveSpeed = speed * 1.3f; // 타구 추적 시 가속
                else if (mt == MOVEMENT_TYPE.E_BASE)
                    moveSpeed = speed * 0.85f; // 베이스 커버 시 약간 감속

                if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_FOLLOW_BALL))
                {
                    _my.position = getMovement().GetMovementPosition(_my.position, getMovement().GetTarget(), moveSpeed);
                }
                else
                {
                    _my.position = getMovement().GetMovementPosition(_my.position, _ball.GetPositionWithYToZero(), moveSpeed);
                }
                
                Vector3 dir = (getMovement().GetTarget() - _my.position).normalized;
                dir.y = 0;
                if (dir != Vector3.zero) _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
            }
        }

        // 글러브 Lerp 포구 시스템
        if (!_hasBall && _ball != null && _ball.isCatchable())
        {
            float distToBall = Vector3.Distance(_my.position, _ball.GetPosition());
            if (distToBall < _catchRange + 1.0f)
            {
                // 포구 범위 안에 들어오면 글러브를 부드럽게 공 쪽으로 이동
                _isReachingForBall = true;
                _gloveReachTimer += Time.deltaTime;
                float lerpSpeed = Mathf.Lerp(8f, 25f, _gloveReachTimer); // 점점 빨라지는 Lerp
                _item.position = Vector3.Lerp(_item.position, _ball.GetPosition(), Time.deltaTime * lerpSpeed);

                // 글러브가 충분히 공에 가까워지면 포구 실행
                if (Vector3.Distance(_item.position, _ball.GetPosition()) < 0.4f && !_hasBall)
                {
                    catchBall();
                    _isReachingForBall = false;
                    _gloveReachTimer = 0f;
                }
            }
            else
            {
                // 범위 밖이면 글러브를 기본 위치로 복귀
                _isReachingForBall = false;
                _gloveReachTimer = 0f;
                _item.localPosition = Vector3.Lerp(_item.localPosition, Vector3.zero, Time.deltaTime * 5f);
            }
        }

        throwBall();
    }

    protected override void PhysicsMoves()
    {
        if (_catchType == CATCH_TYPE.FOLLOW && _ball != null && _ball.isCatchable())
        {
            if (getMovement().CompareMovementType(MOVEMENT_TYPE.E_PREDICT_PATH))
            {
                float nowDistXZ = Vector2.Distance(new Vector2(_ball.GetPosition().x, _ball.GetPosition().z), new Vector2(_my.position.x, _my.position.z));

                if (nowDistXZ < _range.z)
                {
                    // 외야수는 공이 6m 이하로 낮아졌을 때 추적 전환 (기존 4m → 6m으로 더 일찍 반응)
                    if (!IsOutfielder || _ball.GetPosition().y < 6.0f)
                    {
                        getMovement().SetMovementType(MOVEMENT_TYPE.E_FOLLOW_BALL);
                    }
                }
            }
            // 포지션별 포구 범위 적용 (글러브 Lerp가 처리하므로 여기선 범위 체크만)
            if (getDistanceBall() < _catchRange || Vector3.Distance(_ball.GetPosition(), _item.position) < _catchRange)
            {
                // 글러브 Lerp가 Move()에서 처리하므로 여기서는 직접 catchBall 호출하지 않음
                // 단, Lerp가 아직 동작하지 않는 상황(매우 가까움)이면 즉시 포구
                if (Vector3.Distance(_ball.GetPosition(), _item.position) < 0.5f)
                {
                    catchBall();
                }
            }
        }
        else if (_catchType == CATCH_TYPE.JUMP)
        {
            if (!_hasJumped)
            {
                float nowDistXZ = Vector2.Distance(new Vector2(_ball.GetPosition().x, _ball.GetPosition().z), new Vector2(_my.position.x, _my.position.z));
                if (nowDistXZ < 1.5f && _ball.GetPosition().y > 1.8f)
                {
                    _hasJumped = true;
                    if (_bp != null && _bp.Rig != null)
                    {
                        _bp.Rig.linearVelocity = new Vector3(_bp.Rig.linearVelocity.x, 0, _bp.Rig.linearVelocity.z);
                        _bp.Rig.AddForce(Vector3.up * 5.5f, ForceMode.VelocityChange);
                    }
                }
            }

            if (getDistanceBall() < _catchRange || Vector3.Distance(_ball.GetPosition(), _item.position) < _catchRange)
            {
                if (Vector3.Distance(_ball.GetPosition(), _item.position) < 0.5f)
                {
                    catchBall();
                }
            }
        }
    }

    public override void BallEventListener(Vector3 playerPos, Vector3 startPos, Vector3 endPos, Vector3 inc)
    {
        if (_ball == null) return;
        
        // 투수가 던진 투구나 수비수가 던진 송구일 경우, 타구가 아니므로 쫓아가지 않음
        if (_ball.DontNeedToMove()) 
        {
            return;
        }

        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.AssignDefensiveRoles(endPos);
        }
        
        _catchType = CATCH_TYPE.COVER; 
        _hasJumped = false; // 점프 초기화
        
        bool canIntercept = false;
        LineRenderer line = _ball.Line;
        
        // 투수는 무리한 점프나 슬라이딩을 하지 않음
        bool isPitcher = (_type == PLAYER_TYPE.E_PITCHER);

        // 포지션별 인터셉트 탐지 반경
        float interceptRadius = IsOutfielder ? 6.0f : 5.0f;

        if (line != null && line.positionCount > 0 && !isPitcher)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 pos = line.GetPosition(i);
                float distXZ = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(playerPos.x, playerPos.z));
                
                // 포지션별 인터셉트 탐지 반경 적용
                if (distXZ < interceptRadius && pos.z > -5f) 
                {
                    if (pos.y > 1.8f && pos.y <= 4.0f)
                    {
                        _catchType = CATCH_TYPE.JUMP;
                        _interceptPos = pos;
                        canIntercept = true;
                        break;
                    }
                    else if (pos.y <= 1.5f && distXZ > 1.5f)
                    {
                        _catchType = CATCH_TYPE.SLIDE;
                        _interceptPos = pos;
                        canIntercept = true;
                        break;
                    }
                    else if (pos.y <= 1.5f && distXZ <= 1.5f)
                    {
                        _catchType = CATCH_TYPE.REACH;
                        _interceptPos = pos;
                        canIntercept = true;
                        break;
                    }
                }
            }
        }

        if (canIntercept)
        {
            setMovementTarget(_interceptPos, playerPos, -1f, MOVEMENT_TYPE.E_PREDICT_PATH); 
            _catchTimer = 0f;
            _startBodyPos = _my.position;
            _startBodyRot = _my.rotation;
            return;
        }

        if (_bp != null)
        {
            DEFENSIVE_ROLE role = _bp.DefRole;
            Vector3 roleTarget = _bp.RoleTargetPosition;

            if (role == DEFENSIVE_ROLE.CHASER)
            {
                _catchType = CATCH_TYPE.FOLLOW;
                Vector3 smartIntercept = roleTarget; // 기본값은 낙구 지점

                // 외야수와 내야수 모두 스마트 인터셉트 적용
                if (line != null && line.positionCount > 0)
                {
                    float playerSpeed = IsOutfielder ? 4.5f : 5.0f; // 내야수가 짧은 거리를 더 빠르게
                    for (int i = 0; i < line.positionCount; i++)
                    {
                        Vector3 pos = line.GetPosition(i);
                        float ballTime = i * 0.1f;
                        float myTime = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(pos.x, pos.z)) / playerSpeed;

                        // 내가 공보다 먼저(또는 동시에) 도착할 수 있으면서, 글러브가 닿을 수 있는 높이 이하
                        float maxCatchHeight = IsOutfielder ? 3.0f : 2.5f;
                        if (myTime <= ballTime && pos.y <= maxCatchHeight)
                        {
                            smartIntercept = new Vector3(pos.x, 0, pos.z);
                            break;
                        }
                    }
                }

                setMovementTarget(smartIntercept, playerPos, -1f, MOVEMENT_TYPE.E_PREDICT_PATH);
            }
            else if (role == DEFENSIVE_ROLE.CUTOFF || role == DEFENSIVE_ROLE.BACKUP || role == DEFENSIVE_ROLE.BASE_COVER)
            {
                _catchType = CATCH_TYPE.COVER;
                setMovementTarget(roleTarget, playerPos, -1f, MOVEMENT_TYPE.E_BASE);
            }
            else
            {
                baseCover(); // IDLE인 경우 본래 베이스로 복귀
            }
        }
        else
        {
            baseCover();
        }
    }

    void baseCover()
    {
        if (!getMovement().CompareMovementType(MOVEMENT_TYPE.E_BASE) && _base != BASE_TYPE.E_SELF)
            setMovementTarget(BasePositionProvider.provider.GetBasePosition(_base), _my.position, -1f, MOVEMENT_TYPE.E_BASE);
    }

    BASE_TYPE GetClosestBase()
    {
        for (int i = 0; i < BasePositionProvider.provider.Bases.Count; i++)
        {
            if (Vector3.Distance(_my.position, BasePositionProvider.provider.Bases[i].position) < 2.0f)
            {
                return (BASE_TYPE)(i + 2); // E_FIRST_BASE is 2
            }
        }
        return BASE_TYPE.E_SELF;
    }

    void catchBall()
    {
        _item.position = _ball.GetPosition();
        _ball.GrabBall(_item);
        _hasBall = true;

        _readyToThrow = 0f;
        
        BASE_TYPE currentBase = GetClosestBase();

        // 1. 노바운드로 잡으면 아웃 처리 (플라이 아웃)
        if (_ball.CanBeDirectOut())
        {
            PlayerOut(BASE_TYPE.E_FIRST_BASE, BasePositionProvider.provider.GetAttckerBaseState(BASE_TYPE.E_FIRST_BASE));
        }

        // 공을 잡은 후 다음 행동 결정 (병살 연계 / 컷오프)
        if (_catchType == CATCH_TYPE.COVER) 
        {
            Transform nextTarget = BasePositionProvider.provider.GetThrowTarget(_my, _type);
            if (nextTarget != null && nextTarget != _my) 
            {
                _catchType = CATCH_TYPE.NONE; // 다시 송구 모드로 전환 (Relay Throw)
            }
        }
        else if (_catchType == CATCH_TYPE.FOLLOW)
        {
            _catchType = CATCH_TYPE.NONE; 
        }
        // JUMP, SLIDE, REACH는 완료될 때까지 상태를 유지하여 원래 자세로 복귀함
    }

    void PlayerOut(BASE_TYPE baseType, char playerState)
    {
        CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, () => {
            GamePlayerProvider.provider.PlayerOut(BasePositionProvider.provider.GetAttackerPlayerType(playerState)); 
        });
        BasePositionProvider.provider.SetAttackerBaseState(baseType, -1);
        
        // 타자가 아웃되면 처음 위치로 복귀 및 공을 투수에게 전달
        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.ResetDefense();
            
            if (_ball != null)
            {
                Transform pitcherHand = _bp.MyTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
                if (pitcherHand != null)
                {
                    _ball.GrabBall(pitcherHand);
                }
            }
        }
    }

    void throwBall()
    {
        if(_hasBall)
        {
            Transform targetTransform = BasePositionProvider.provider.GetThrowTarget(_my, _type);
            
            // 외야수 중계 플레이 로직 (Cutoff)
            if (_type == PLAYER_TYPE.E_LEFT_FILED || _type == PLAYER_TYPE.E_CENTER_FIELD || _type == PLAYER_TYPE.E_RIGHT_FILED)
            {
                if (targetTransform != null && Vector3.Distance(_my.position, targetTransform.position) > 25f) // 송구 거리가 너무 멀면 컷오프 맨에게
                {
                    PLAYER_TYPE cutoffType = (_type == PLAYER_TYPE.E_RIGHT_FILED) ? PLAYER_TYPE.E_SECOND_BASE : PLAYER_TYPE.E_SHORT_STOP;
                    if (_bp != null && _bp.MyTeam != null)
                    {
                        targetTransform = _bp.MyTeam.GetPlayerItem(cutoffType);
                    }
                }
            }

            // 타겟이 베이스인 경우(Base 태그를 가짐), 해당 베이스를 커버하는 실제 야수 찾기
            bool usingDummy = false;
            if (targetTransform != null && targetTransform.CompareTag("Base"))
            {
                if (_bp != null && _bp.MyTeam != null)
                {
                    BaseballPlayer fielder = _bp.MyTeam.GetFielderCoveringBase(targetTransform);
                    if (fielder != null)
                    {
                        if (Vector3.Distance(fielder.transform.position, targetTransform.position) > 1.5f)
                        {
                            // 야수가 아직 베이스에 도착하지 않았다면, 도착할 위치(베이스 위 가슴 높이)로 더미를 만들어 던짐
                            GameObject throwTargetDummy = new GameObject("ThrowTargetDummy");
                            throwTargetDummy.tag = "BaseballPlayer";
                            throwTargetDummy.transform.position = targetTransform.position + Vector3.up * 1.5f;
                            targetTransform = throwTargetDummy.transform;
                            usingDummy = true;
                        }
                        else
                        {
                            // 이미 베이스에 도착했다면 야수의 글러브로 직접 던짐
                            targetTransform = fielder.GetItem();
                        }
                    }
                }
            }

            if (targetTransform != null && targetTransform != _my)
            {
                // 타겟 방향으로 부드럽게 몸을 돌림 (최단 경로 회전)
                Vector3 lookDir = targetTransform.position - _my.position;
                lookDir.y = 0;
                
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    // RotateTowards 대신 Slerp를 사용하여 항상 최단 경로로 회전 (360도 회전 버그 방지)
                    float rotSpeed = IsInfielder ? 15f : 12f;
                    _my.rotation = Quaternion.Slerp(_my.rotation, targetRot, Time.deltaTime * rotSpeed);
                }

                _readyToThrow += Time.deltaTime;
                
                // 내야수는 빠르게(0.3초), 외야수는 여유있게(0.5초) 송구 준비
                float minThrowDelay = IsInfielder ? 0.25f : 0.4f;
                float maxThrowDelay = IsInfielder ? 0.6f : 0.8f;
                float angleThreshold = 10.0f;
                
                if (_readyToThrow >= maxThrowDelay || (_readyToThrow >= minThrowDelay && Vector3.Angle(_my.forward, lookDir.normalized) < angleThreshold))
                {
                    _readyToThrow = 0f;
                    _hasBall = false;
                    
                    // 송구 직전 몸을 타겟으로 완전 정렬
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        _my.rotation = Quaternion.LookRotation(lookDir);
                    }
                    
                    // 정확하게 타겟을 향해 공이 날아가도록 손(item)의 방향을 타겟으로 강제 정렬
                    if (_item != null)
                    {
                        _item.rotation = Quaternion.LookRotation(targetTransform.position - _item.position);
                    }

                    GetController().Action(targetTransform);
                    if (usingDummy)
                    {
                        GameObject.Destroy(targetTransform.gameObject, 3.0f);
                    }
                }
            }
            else
            {
                _hasBall = false;
            }
        }
    }

    public override void OnTriggerEnter(Collider collider)
    {
        if(collider.transform.tag.Equals("Base"))
        {
            BASE_TYPE hitBase = GetClosestBase();
            if (hitBase == BASE_TYPE.E_SELF) return;

            BasePositionProvider.provider.SetDefencerBaseState(hitBase, (int)_type + 1);
            char attacker = BasePositionProvider.provider.GetAttckerBaseState(hitBase);

            if (_hasBall && attacker > '9' && attacker != '_')
            {
                PlayerOut(hitBase, attacker);
            }
        }
    }

    public override string ToString()
    {
        return "Def";
    }
}
