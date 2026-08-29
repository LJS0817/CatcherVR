using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

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

    // ====== DOTween 송구 모션 변수 ======
    Transform _gloveHand;
    Transform _throwHand;
    Vector3 _initGloveHandLocalPos;
    Quaternion _initGloveHandLocalRot;
    Vector3 _initThrowHandLocalPos;
    Quaternion _initThrowHandLocalRot;
    bool _isThrowingMotion = false;
    Sequence _throwSequence;

    // ====== 수비 존 시스템 ======
    DefenseZone _defenseZone;
    BATTED_BALL_TYPE _currentBallType = BATTED_BALL_TYPE.GROUND_BALL;
    Vector3 _homePos = Vector3.zero; // 홈플레이트 위치 캐싱

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

        // 수비 존 초기화
        _defenseZone = DefenseZone.CreateForPosition(t);

        // 홈플레이트 위치 캐싱
        if (BasePositionProvider.provider != null)
        {
            _homePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE);
        }

        // 글러브 손(RightHand) 및 던지는 맨손(LeftHand) 캐싱
        Transform playerObj = _my.childCount > 0 ? _my.GetChild(0) : null;
        Transform shoulder = (playerObj != null && playerObj.childCount > 1) ? playerObj.GetChild(1) : null;
        if (shoulder != null)
        {
            if (shoulder.childCount > 0) _gloveHand = shoulder.GetChild(0);
            if (shoulder.childCount > 1) _throwHand = shoulder.GetChild(1);
        }

        if (_gloveHand == null || _throwHand == null)
        {
            foreach (var tr in _my.GetComponentsInChildren<Transform>(true))
            {
                if (tr.name.Contains("RightHand") && _gloveHand == null) _gloveHand = tr;
                if (tr.name.Contains("LeftHand") && _throwHand == null) _throwHand = tr;
            }
        }

        if (_gloveHand != null)
        {
            _initGloveHandLocalPos = _gloveHand.localPosition;
            _initGloveHandLocalRot = _gloveHand.localRotation;
        }
        if (_throwHand != null)
        {
            _initThrowHandLocalPos = _throwHand.localPosition;
            _initThrowHandLocalRot = _throwHand.localRotation;
        }

        if (_base != BASE_TYPE.E_SELF) BasePositionProvider.provider.SetDefencerBaseState(_base, (int)t + 1);
    }

    public override void ResetRole()
    {
        base.ResetRole();
        _throwSequence?.Kill();
        _isThrowingMotion = false;
        if (_gloveHand != null)
        {
            _gloveHand.localPosition = _initGloveHandLocalPos;
            _gloveHand.localRotation = _initGloveHandLocalRot;
        }
        if (_throwHand != null)
        {
            _throwHand.localPosition = _initThrowHandLocalPos;
            _throwHand.localRotation = _initThrowHandLocalRot;
        }
        if (_controller != null && _item != null)
        {
            _controller.SetItemPoint(_item);
        }
        _catchType = CATCH_TYPE.NONE;
        _hasBall = false;
        _hasJumped = false;
        _readyToThrow = 0f;
        _isReachingForBall = false;
        _gloveReachTimer = 0f;
        _currentBallType = BATTED_BALL_TYPE.GROUND_BALL;
    }

    public override void SetController(Transform pool, Transform point, Transform target, float speed=1.9f)
    {
        _controller = new PitchingController(pool, point, target, speed == 0f ? 1.9f : speed);
    }

    /// <summary>
    /// 외부에서 수비 존을 설정할 수 있도록 허용 (포지션별 커스텀 존)
    /// </summary>
    public void SetDefenseZone(DefenseZone zone)
    {
        if (zone != null) _defenseZone = zone;
    }

    /// <summary>현재 판별된 타구 유형</summary>
    public BATTED_BALL_TYPE CurrentBallType => _currentBallType;

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
                float acceleration = 15f;
                
                MOVEMENT_TYPE mt = getMovement().GetMovementType();
                if (mt == MOVEMENT_TYPE.E_PREDICT_PATH || mt == MOVEMENT_TYPE.E_FOLLOW_BALL)
                {
                    moveSpeed = speed * (IsOutfielder ? 1.4f : 1.25f);
                    acceleration = IsOutfielder ? 12f : 18f; // 외야수는 관성이 커서 묵직함
                }
                else if (mt == MOVEMENT_TYPE.E_BASE)
                {
                    moveSpeed = speed * 0.9f; 
                    acceleration = 20f;
                }

                Vector3 targetPos = getMovement().GetTarget();
                if (getMovement().CompareMovementType(MOVEMENT_TYPE.E_FOLLOW_BALL))
                {
                    targetPos = _ball.GetPositionWithYToZero();
                }

                _my.position = getMovement().GetMovementPosition(_my.position, targetPos, moveSpeed, acceleration);
                
                Vector3 dir = (targetPos - _my.position).normalized;
                dir.y = 0;
                if (dir.sqrMagnitude > 0.001f) 
                {
                    _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(dir), Time.deltaTime * (IsOutfielder ? 8f : 12f));
                }
            }
        }

        // 글러브 Lerp 포구 시스템: 배트에 맞은 타구(_isBattedBall)이거나 송구 수구 대기 중(COVER)일 때만 동작
        if (!_hasBall && _ball != null && _ball.isCatchable() && (_ball._isBattedBall || _catchType == CATCH_TYPE.COVER))
        {
            float distToBall = Vector3.Distance(_my.position, _ball.GetPosition());
            float heightToBall = _ball.GetPosition().y;

            if (distToBall < _catchRange + 1.5f && heightToBall < 4.0f)
            {
                _isReachingForBall = true;
                _gloveReachTimer += Time.deltaTime;
                
                // 공의 속도와 거리에 비례하여 글러브 속도 조절 (자연스럽게 마중 나가는 느낌)
                float ballSpeedXZ = new Vector2(_ball.GetVelocity().x, _ball.GetVelocity().z).magnitude;
                float lerpSpeed = Mathf.Clamp(10f + (ballSpeedXZ * 0.5f), 15f, 35f);
                
                _item.position = Vector3.Lerp(_item.position, _ball.GetPosition(), Time.deltaTime * lerpSpeed);

                if (Vector3.Distance(_item.position, _ball.GetPosition()) < 0.35f && !_hasBall)
                {
                    catchBall();
                    _isReachingForBall = false;
                    _gloveReachTimer = 0f;
                }
            }
            else
            {
                _isReachingForBall = false;
                _gloveReachTimer = 0f;
                _item.localPosition = Vector3.Lerp(_item.localPosition, Vector3.zero, Time.deltaTime * 8f);
            }
        }
        else if (!_hasBall)
        {
            _isReachingForBall = false;
            _gloveReachTimer = 0f;
            _item.localPosition = Vector3.Lerp(_item.localPosition, Vector3.zero, Time.deltaTime * 8f);
        }

        throwBall();
    }

    protected override void PhysicsMoves()
    {
        if (_catchType == CATCH_TYPE.FOLLOW && _ball != null && _ball.isCatchable() && _ball._isBattedBall)
        {
            if (getMovement().CompareMovementType(MOVEMENT_TYPE.E_PREDICT_PATH))
            {
                // 지속적으로 최적의 낙구 지점(인터셉트)을 갱신 (커브 경로 생성 효과)
                LineRenderer line = _ball.Line;
                if (line != null && line.positionCount > 0)
                {
                    float playerSpeed = IsOutfielder ? 5.6f : 5.0f; // 예상 최고 속도
                    Vector3 bestIntercept = _ball.GetLandPosition(); // 기본 낙구 지점
                    
                    for (int i = 0; i < line.positionCount; i++)
                    {
                        Vector3 pos = line.GetPosition(i);
                        float ballTime = i * 0.1f;
                        float myTime = Vector2.Distance(new Vector2(_my.position.x, _my.position.z), new Vector2(pos.x, pos.z)) / playerSpeed;

                        float maxCatchHeight = BattedBallClassifier.GetMaxCatchHeight(_currentBallType, IsOutfielder);
                        // 현재 내 위치에서 달렸을 때, 공이 도달하기 전이나 동시에 도착할 수 있는 가장 빠른 지점을 찾음
                        if (myTime <= ballTime && pos.y <= maxCatchHeight)
                        {
                            bestIntercept = new Vector3(pos.x, 0, pos.z);
                            break;
                        }
                    }
                    setMovementTarget(bestIntercept, _my.position, -1f, MOVEMENT_TYPE.E_PREDICT_PATH);
                }

                float nowDistXZ = Vector2.Distance(new Vector2(_ball.GetPosition().x, _ball.GetPosition().z), new Vector2(_my.position.x, _my.position.z));
                if (nowDistXZ < _range.z)
                {
                    // 외야수는 공이 6m 이하로 낮아졌을 때 추적 전환
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

    // ============================================================
    //  존 기반 타구 판단 시스템
    // ============================================================

    public override void BallEventListener(Vector3 playerPos, Vector3 startPos, Vector3 endPos, Vector3 inc)
    {
        if (_ball == null) return;
        
        // 투수가 던진 투구나 수비수가 던진 송구일 경우, 타구가 아니므로 쫓아가지 않음
        if (_ball.DontNeedToMove()) 
        {
            return;
        }

        // ① 타구 유형 분류
        LineRenderer line = _ball.Line;
        _currentBallType = BattedBallClassifier.ClassifyFromTrajectory(line, endPos, _homePos);

        // ② 팀 역할 분배 (Team에서 존 기반으로 CHASER 결정)
        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.AssignDefensiveRoles(endPos, _currentBallType);
        }
        
        _catchType = CATCH_TYPE.COVER; 
        _hasJumped = false; // 점프 초기화
        
        // ③ 내 수비 존 안으로 궤적이 지나가는지 확인 (인터셉트 가능성)
        bool canIntercept = false;
        
        bool isPitcher = (_type == PLAYER_TYPE.E_PITCHER);

        // 존 기반 인터셉트 검사: 궤적이 내 존을 통과하는 포인트 찾기
        float maxCatchHeight = BattedBallClassifier.GetMaxCatchHeight(_currentBallType, IsOutfielder);

        if (line != null && line.positionCount > 0)
        {
            // 존 + 근접 거리 혼합 판단
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 pos = line.GetPosition(i);
                float distXZ = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(playerPos.x, playerPos.z));
                
                // 존 안의 궤적이거나, 내 근처(5m 이내)를 지나가는 궤적
                bool inMyZone = _defenseZone != null && _defenseZone.IsInZone(pos, _homePos);
                bool nearMe = distXZ < 5.0f;

                if ((inMyZone || nearMe) && pos.z > -8f) 
                {
                    // 일반 야수: 점프 및 슬라이딩 인터셉트
                    if (!isPitcher && pos.y > 1.8f && pos.y <= 4.0f && nearMe)
                    {
                        _catchType = CATCH_TYPE.JUMP;
                        _interceptPos = pos;
                        canIntercept = true;
                        break;
                    }
                    else if (!isPitcher && pos.y <= 1.5f && distXZ > 1.5f && nearMe)
                    {
                        _catchType = CATCH_TYPE.SLIDE;
                        _interceptPos = pos;
                        canIntercept = true;
                        break;
                    }
                    // 일반 야수 및 투수: 정면/초근접 강습 타구에 대한 반사적 손 뻗기 (REACH)
                    else if (pos.y <= 2.2f && distXZ <= (isPitcher ? 2.0f : 1.5f))
                    {
                        _catchType = CATCH_TYPE.REACH;
                        _interceptPos = pos;
                        canIntercept = true;
                        break;
                    }
                }
            }
        }

        // 반응 딜레이 계산
        float reactionDelay;
        if (isPitcher)
        {
            // 투수: 정면 강습 인터셉트는 즉각 반사 반응(0.08초), 이동 추적/수비 전환은 투구 팔로우스루 후(0.35~0.45초)
            reactionDelay = canIntercept ? 0.08f : UnityEngine.Random.Range(0.35f, 0.45f);
        }
        else
        {
            // 야수: 타구 유형별 반응 딜레이
            BattedBallClassifier.GetReactionDelay(_currentBallType, out float minDelay, out float maxDelay);
            reactionDelay = canIntercept ? minDelay * 0.5f : UnityEngine.Random.Range(minDelay, maxDelay);
        }

        if (canIntercept)
        {
            setMovementTarget(_interceptPos, playerPos, -1f, MOVEMENT_TYPE.E_PREDICT_PATH, reactionDelay); 
            _catchTimer = 0f;
            _startBodyPos = _my.position;
            _startBodyRot = _my.rotation;
            return;
        }

        // ④ 팀에서 할당된 역할에 따라 행동 결정
        if (_bp != null)
        {
            DEFENSIVE_ROLE role = _bp.DefRole;
            Vector3 roleTarget = _bp.RoleTargetPosition;

            if (role == DEFENSIVE_ROLE.CHASER)
            {
                _catchType = CATCH_TYPE.FOLLOW;
                Vector3 smartIntercept = roleTarget; // 기본값은 낙구 지점

                // 스마트 인터셉트 적용 (타구 유형별 최적 포구 높이 반영)
                if (line != null && line.positionCount > 0)
                {
                    float playerSpeed = IsOutfielder ? 4.5f : (isPitcher ? 4.2f : 5.0f);
                    for (int i = 0; i < line.positionCount; i++)
                    {
                        Vector3 pos = line.GetPosition(i);
                        float ballTime = i * 0.1f;
                        float myTime = Vector2.Distance(new Vector2(playerPos.x, playerPos.z), new Vector2(pos.x, pos.z)) / playerSpeed;

                        // 내가 공보다 먼저(또는 동시에) 도착할 수 있으면서, 포구 가능한 높이 이하
                        if (myTime <= ballTime && pos.y <= maxCatchHeight)
                        {
                            smartIntercept = new Vector3(pos.x, 0, pos.z);
                            break;
                        }
                    }
                }

                setMovementTarget(smartIntercept, playerPos, -1f, MOVEMENT_TYPE.E_PREDICT_PATH, reactionDelay);
            }
            else if (role == DEFENSIVE_ROLE.CUTOFF || role == DEFENSIVE_ROLE.BACKUP || role == DEFENSIVE_ROLE.BASE_COVER)
            {
                _catchType = CATCH_TYPE.COVER;
                setMovementTarget(roleTarget, playerPos, -1f, MOVEMENT_TYPE.E_BASE, reactionDelay);
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

        // 수비수가 공을 쥐고 있을 때 실시간 포스 아웃 및 태그 아웃 검사
        if (_hasBall)
        {
            // 1. 베이스 근접(0.8m 이내) 포스 아웃
            BASE_TYPE nearBase = GetClosestBase();
            if (nearBase != BASE_TYPE.E_SELF)
            {
                Vector3 basePos = BasePositionProvider.provider.GetBasePosition(nearBase);
                float dist = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(basePos.x, 0, basePos.z));
                if (dist < 0.8f)
                {
                    CheckBaseForceOut(nearBase);
                }
            }

            // 2. 주자 근접(1.2m 이내) 태그 아웃
            Team attackTeam = GamePlayerProvider.provider != null ? GamePlayerProvider.provider.AttackTeam : null;
            if (attackTeam != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    BaseballPlayer runner = attackTeam.GetPlayer((PLAYER_TYPE)i);
                    if (runner != null && runner.gameObject.activeSelf && runner.GetRole() is Attacker att && att.IsOnBaseOrRunning)
                    {
                        float distToRunner = Vector3.Distance(new Vector3(_my.position.x, 0, _my.position.z), new Vector3(runner.transform.position.x, 0, runner.transform.position.z));
                        if (distToRunner <= 1.2f)
                        {
                            CheckTagOut(runner);
                        }
                    }
                }
            }
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
        _throwSequence?.Kill();
        _isThrowingMotion = false;
        if (_gloveHand != null)
        {
            _gloveHand.localPosition = _initGloveHandLocalPos;
            _gloveHand.localRotation = _initGloveHandLocalRot;
        }
        if (_throwHand != null)
        {
            _throwHand.localPosition = _initThrowHandLocalPos;
            _throwHand.localRotation = _initThrowHandLocalRot;
        }
        if (_controller != null && _item != null)
        {
            _controller.SetItemPoint(_item);
        }

        _item.position = _ball.GetPosition();
        _ball.GrabBall(_item);
        _hasBall = true;

        _readyToThrow = 0f;
        
        // 1. 노바운드로 타구(배트에 맞은 공)를 직접 잡았을 때만 플라이 아웃 처리
        // (송구를 받거나 땅볼 포구 시에는 플라이 아웃을 발동하지 않음!)
        if (_ball._isBattedBall && _ball.CanBeDirectOut())
        {
            _ball.SetBattedBall(false); // 타구 플래그 소비 (후속 송구 수구 시 중복 아웃 방지)
            CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, () => {
                GamePlayerProvider.provider.PlayerOut(PLAYER_TYPE.E_PITCHER); // 타자 플라이 아웃
            });
        }
        else
        {
            // 타구가 땅에 닿았거나 송구를 받은 상태
            _ball.SetBattedBall(false);
            
            // 투수가 공을 잡았을 때: 선행 주자를 처리할 수 있으면 그 베이스에 던지고, 아니면 1루에 던져서 아웃
            if (_type == PLAYER_TYPE.E_PITCHER)
            {
                Transform throwTarget = BasePositionProvider.provider.GetThrowTarget(_my, _type);
                
                // 던질 곳이 있다면(주자가 달리고 있는 베이스가 있으면) 송구 모드로 전환
                if (throwTarget != null && throwTarget != _my)
                {
                    _catchType = CATCH_TYPE.NONE; // throwBall()이 다음 프레임에서 처리
                }
                else
                {
                    // 아무 주자도 없거나 더 이상 던질 곳이 없으면 플레이 종료 -> 다음 타자 교체
                    if (GamePlayerProvider.provider != null)
                    {
                        Team attackTeam = GamePlayerProvider.provider.AttackTeam;
                        if (attackTeam != null)
                        {
                            attackTeam.ChangeAttacker();
                        }
                    }
                }
            }
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
        _throwSequence?.Kill();
        _isThrowingMotion = false;

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
        if(_hasBall && !_isThrowingMotion)
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

            // 1루수 언어시스트 아웃: 공을 잡은 1루수가 1루 베이스 근처에 있다면 직접 밟으러 감
            if (_type == PLAYER_TYPE.E_FIRST_BASE && _currentBallType == BATTED_BALL_TYPE.GROUND_BALL)
            {
                Vector3 firstBasePos = BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
                float distToBase = Vector3.Distance(_my.position, firstBasePos);
                
                // 베이스에서 5m 이내이면 송구 대신 직접 밟으러 감
                if (distToBase < 5.0f)
                {
                    setMovementTarget(firstBasePos, _my.position, -1f, MOVEMENT_TYPE.E_BASE);
                    
                    // 방향 전환
                    Vector3 lookDir = firstBasePos - _my.position;
                    lookDir.y = 0;
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        _my.rotation = Quaternion.Slerp(_my.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 15f);
                    }
                    return; // 송구하지 않고 직접 밟으러 감
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
                // 타겟 방향으로 몸 정렬
                Vector3 lookDir = targetTransform.position - _my.position;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    _my.rotation = Quaternion.LookRotation(lookDir);
                }

                StartThrowSequence(targetTransform, usingDummy);
            }
            else
            {
                // 던질 곳이 없을 때 (투수가 공을 들고 있으나 주자가 없거나)
                if (_type == PLAYER_TYPE.E_PITCHER)
                {
                    // 투수가 공을 들고 있으나 던질 타겟이 없으면 플레이 종료
                    _hasBall = false;
                    if (GamePlayerProvider.provider != null)
                    {
                        Team attackTeam = GamePlayerProvider.provider.AttackTeam;
                        if (attackTeam != null)
                        {
                            attackTeam.ChangeAttacker();
                        }
                    }
                }
                else
                {
                    _hasBall = false;
                }
            }
        }
    }

    /// <summary>
    /// DOTween을 활용한 사실적인 야구 송구 모션 (글러브에서 볼 빼기 -> 테이크백 -> 릴리스 -> 팔로우스루)
    /// </summary>
    void StartThrowSequence(Transform targetTransform, bool usingDummy)
    {
        if (_isThrowingMotion || targetTransform == null) return;
        _isThrowingMotion = true;
        _throwSequence?.Kill();

        if (_gloveHand == null || _throwHand == null)
        {
            // 손 Transform이 없는 경우의 안전한 즉시 송구 폴백
            ExecuteDirectThrow(targetTransform, usingDummy);
            _isThrowingMotion = false;
            return;
        }

        // 포지션별 송구 모션 시간 (내야수 퀵 스로우 0.38초, 외야수 크로우 홉 0.65초, 투수 0.45초)
        float baseDuration = IsInfielder ? 0.38f : (IsOutfielder ? 0.65f : 0.45f);
        float tTransfer = baseDuration * 0.32f; // 글러브에서 공 꺼내기 (볼 빼기)
        float tCock = baseDuration * 0.35f;     // 테이크백 (뒤로 젖히기)
        float tRelease = baseDuration * 0.18f;  // 전방 릴리스 스윙
        float tFollow = baseDuration * 0.25f;   // 팔로우스루 및 복귀

        _throwSequence = DOTween.Sequence();

        // -------------------------------------------------------------
        // Phase 1: 글러브로 던지는 맨손(LeftHand)이 다가가서 공을 잡음 (Transfer)
        // -------------------------------------------------------------
        Vector3 chestGlovePos = new Vector3(0.4f, -0.7f, 0.7f);
        _throwSequence.Append(_gloveHand.DOLocalMove(chestGlovePos, tTransfer).SetEase(Ease.OutQuad));
        _throwSequence.Join(_gloveHand.DOLocalRotate(new Vector3(-20f, 35f, 0f), tTransfer).SetEase(Ease.OutQuad));

        Vector3 chestThrowPos = new Vector3(0.35f, -0.7f, 0.7f);
        _throwSequence.Join(_throwHand.DOLocalMove(chestThrowPos, tTransfer).SetEase(Ease.OutQuad));
        _throwSequence.Join(_throwHand.DOLocalRotate(new Vector3(40f, -40f, 0f), tTransfer).SetEase(Ease.OutQuad));

        // 공이 글러브에서 맨손으로 이동 (Transfer Point)
        _throwSequence.AppendCallback(() =>
        {
            if (_ball != null && _hasBall && _throwHand != null)
            {
                _ball.GrabBall(_throwHand);
            }
        });

        // -------------------------------------------------------------
        // Phase 2: 테이크백 & 와인드업 (Cocking)
        // -------------------------------------------------------------
        Vector3 cockPos = new Vector3(-1.6f, 0.5f, -0.7f);
        _throwSequence.Append(_throwHand.DOLocalMove(cockPos, tCock).SetEase(Ease.InOutQuad));
        _throwSequence.Join(_throwHand.DOLocalRotate(new Vector3(-65f, 15f, 25f), tCock).SetEase(Ease.InOutQuad));

        Vector3 aimGlovePos = new Vector3(0.8f, -0.3f, 1.2f);
        _throwSequence.Join(_gloveHand.DOLocalMove(aimGlovePos, tCock).SetEase(Ease.InOutQuad));
        _throwSequence.Join(_gloveHand.DOLocalRotate(new Vector3(10f, -15f, 0f), tCock).SetEase(Ease.InOutQuad));

        // -------------------------------------------------------------
        // Phase 3: 전방 스윙 및 공 릴리스 (Release)
        // -------------------------------------------------------------
        Vector3 releasePos = new Vector3(-0.3f, 0.4f, 1.4f);
        _throwSequence.Append(_throwHand.DOLocalMove(releasePos, tRelease).SetEase(Ease.InQuad));
        _throwSequence.Join(_throwHand.DOLocalRotate(new Vector3(55f, 0f, 0f), tRelease).SetEase(Ease.InQuad));

        _throwSequence.AppendCallback(() =>
        {
            if (_hasBall && targetTransform != null)
            {
                _hasBall = false;

                Vector3 finalLookDir = targetTransform.position - _my.position;
                finalLookDir.y = 0;
                if (finalLookDir.sqrMagnitude > 0.001f)
                {
                    _my.rotation = Quaternion.LookRotation(finalLookDir);
                }

                Transform throwOrigin = _throwHand != null ? _throwHand : _item;
                Vector3 throwDir = targetTransform.position - throwOrigin.position;
                float dist = throwDir.magnitude;

                if (dist > 15f)
                {
                    throwDir.y += (dist - 15f) * 0.15f;
                }

                throwOrigin.rotation = Quaternion.LookRotation(throwDir);

                if (_controller != null)
                {
                    _controller.SetItemPoint(throwOrigin);
                    _controller.Action(targetTransform);
                    _controller.SetItemPoint(_item);
                }

                if (usingDummy && targetTransform != null)
                {
                    GameObject.Destroy(targetTransform.gameObject, 3.0f);
                }
            }
        });

        // -------------------------------------------------------------
        // Phase 4: 팔로우스루 및 수비 기본 자세 복귀 (Follow-Through & Return)
        // -------------------------------------------------------------
        Vector3 followPos = new Vector3(0.8f, -1.3f, 0.4f);
        _throwSequence.Append(_throwHand.DOLocalMove(followPos, tFollow * 0.4f).SetEase(Ease.OutQuad));

        _throwSequence.Append(_throwHand.DOLocalMove(_initThrowHandLocalPos, tFollow * 0.6f).SetEase(Ease.OutQuad));
        _throwSequence.Join(_throwHand.DOLocalRotate(_initThrowHandLocalRot.eulerAngles, tFollow * 0.6f).SetEase(Ease.OutQuad));
        _throwSequence.Join(_gloveHand.DOLocalMove(_initGloveHandLocalPos, tFollow * 0.6f).SetEase(Ease.OutQuad));
        _throwSequence.Join(_gloveHand.DOLocalRotate(_initGloveHandLocalRot.eulerAngles, tFollow * 0.6f).SetEase(Ease.OutQuad));

        _throwSequence.OnComplete(() =>
        {
            _isThrowingMotion = false;
        });

        _throwSequence.OnKill(() =>
        {
            _isThrowingMotion = false;
        });
    }

    void ExecuteDirectThrow(Transform targetTransform, bool usingDummy)
    {
        _hasBall = false;
        if (_item != null && targetTransform != null)
        {
            Vector3 throwDir = targetTransform.position - _item.position;
            float dist = throwDir.magnitude;
            if (dist > 15f) throwDir.y += (dist - 15f) * 0.15f;
            _item.rotation = Quaternion.LookRotation(throwDir);
        }
        if (_controller != null)
        {
            _controller.Action(targetTransform);
        }
        if (usingDummy && targetTransform != null)
        {
            GameObject.Destroy(targetTransform.gameObject, 3.0f);
        }
    }

    public override void OnTriggerEnter(Collider collider)
    {
        // 1. 베이스 충돌 시 포스 아웃 검사
        if(collider.transform.tag.Equals("Base") || collider.transform.name.ToLower().Contains("base"))
        {
            BASE_TYPE hitBase = GetClosestBase();
            if (hitBase != BASE_TYPE.E_SELF)
            {
                BasePositionProvider.provider.SetDefencerBaseState(hitBase, (int)_type + 1);
                CheckBaseForceOut(hitBase);
            }
        }

        // 2. 공격팀 주자 직접 충돌 시 태그 아웃 검사
        if (_hasBall)
        {
            BaseballPlayer touchedPlayer = collider.GetComponentInParent<BaseballPlayer>();
            if (touchedPlayer != null && touchedPlayer.GetRole() is Attacker)
            {
                CheckTagOut(touchedPlayer);
            }
        }
    }

    /// <summary>
    /// 수비수가 공을 쥐고 베이스를 터치했을 때, 포스 아웃(Force Out) 판정 및 해당 주자 퇴장(ExitGame)
    /// </summary>
    public void CheckBaseForceOut(BASE_TYPE hitBase)
    {
        if (!_hasBall || hitBase == BASE_TYPE.E_SELF) return;

        // 1. 해당 베이스가 포스 아웃 가능한 상태인지 확인
        if (!BasePositionProvider.provider.IsForcePlayBase(hitBase)) return;

        // 2. 공격팀 가져오기
        Team attackTeam = GamePlayerProvider.provider != null ? GamePlayerProvider.provider.AttackTeam : null;
        if (attackTeam == null) return;

        // 3. 해당 베이스로 달려오고 있는 주자 검색
        BaseballPlayer incomingRunner = attackTeam.GetRunnerHeadingToBase(hitBase);
        if (incomingRunner != null && incomingRunner.gameObject.activeSelf && incomingRunner.GetRole() is Attacker runnerAtt)
        {
            Vector3 basePos = BasePositionProvider.provider.GetBasePosition(hitBase);
            float runnerDist = Vector3.Distance(new Vector3(incomingRunner.transform.position.x, 0, incomingRunner.transform.position.z), new Vector3(basePos.x, 0, basePos.z));

            // 주자가 베이스에 아직 닿지 않았다면 포스 아웃!
            if (runnerDist > 0.4f)
            {
                CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, () => {
                    runnerAtt.OnOut(); // ★ 해당 주자 아웃 및 즉시 비활성화(ExitGame)
                });

                BasePositionProvider.provider.SetAttackerBaseState(hitBase, -1);

                // 4. 후속 플레이: 병살(Double Play)을 위해 다른 주자가 있는 베이스로 송구 시도
                Transform nextTarget = BasePositionProvider.provider.GetThrowTarget(_my, _type);
                if (nextTarget != null && nextTarget != _my)
                {
                    _readyToThrow = 0f;
                    _catchType = CATCH_TYPE.COVER; // 다음 베이스로 즉시 릴레이 송구
                }
                else
                {
                    // 더 이상 송구할 곳이 없으면 투수에게 공 반환
                    if (_bp != null && _bp.MyTeam != null && _ball != null)
                    {
                        Transform pitcherHand = _bp.MyTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
                        if (pitcherHand != null)
                        {
                            _ball.GrabBall(pitcherHand);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 공을 쥔 수비수가 베이스에서 떨어져 있는 주자를 직접 태그(Tag)했을 때 태그 아웃 판정 및 퇴장
    /// </summary>
    public void CheckTagOut(BaseballPlayer runnerPlayer)
    {
        if (!_hasBall || runnerPlayer == null || !runnerPlayer.gameObject.activeSelf) return;

        if (runnerPlayer.GetRole() is Attacker runnerAtt)
        {
            // 주자가 베이스 위에 완전히 정지해 있지 않고 베이스 사이를 뛰고 있거나 오버런 이탈한 경우 -> 태그 아웃!
            if (runnerAtt.IsOnBaseOrRunning && !runnerAtt.IsSafeOnBase())
            {
                CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, () => {
                    runnerAtt.OnOut(); // ★ 태그 아웃된 주자 퇴장(ExitGame)
                });

                // 후속 플레이: 다음 주자가 있다면 다음 베이스로 송구, 없으면 투수에게 반환
                Transform nextTarget = BasePositionProvider.provider.GetThrowTarget(_my, _type);
                if (nextTarget != null && nextTarget != _my)
                {
                    _readyToThrow = 0f;
                    _catchType = CATCH_TYPE.COVER;
                }
                else
                {
                    if (_bp != null && _bp.MyTeam != null && _ball != null)
                    {
                        Transform pitcherHand = _bp.MyTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
                        if (pitcherHand != null)
                        {
                            _ball.GrabBall(pitcherHand);
                        }
                    }
                }
            }
        }
    }

    public override string ToString()
    {
        return "Def";
    }
}
