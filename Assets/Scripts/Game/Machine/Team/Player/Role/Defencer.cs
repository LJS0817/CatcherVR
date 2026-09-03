using System.Collections.Generic;
using UnityEngine;

/// <summary>수비수 FSM 상태.</summary>
public enum FIELDER_STATE
{
    PRE_PITCH,   // 투구 전 준비 자세
    READ,        // 타구를 읽는 중 (반응 시간)
    PURSUE,      // 타구 추격 / 루트 러닝
    FIELD,       // 포구 시도 (글러브 리치 + 다이빙/점프/슬라이딩)
    SECURE,      // 포구 완료 — 볼 빼기 / 발 세팅 / 크로우 홉
    THROW,       // 송구 동작
    COVER,       // 베이스 커버 · 중계 · 백업 위치에서 대기
    RECEIVE,     // 날아오는 송구 수구 (필요 시 스트레치)
    TAG,         // 태그 시도 / 런다운
    RETURN       // 원위치 복귀
}

/// <summary>
/// 수비수 AI.
///
/// 구성
///   BallTrajectory     : 바운드·구름까지 포함한 궤적 예측 (실제 물리와 동일 공식)
///   InterceptSolver    : 반응 · 가속 · 후진 페널티 · 글러브 리치를 반영한 도달 판정
///   FieldingCoordinator: 콜오프 우선순위 기반 역할 배정(1차/트레일/중계/커버/백업)
///   ThrowDecision      : 사용자 지휘 &gt; 송구 도착 시간 vs 주자 ETA 비교
///   CatchPostureTable  : 포구 자세별 기상 · 볼 빼기 딜레이(설계도 8장)
///   RundownPlay        : 런다운 협력 수비(설계도 9장)
///   FielderMotion      : 애니메이션 클립 없이 스크립트로 만드는 절차적 몸동작
///   이 클래스           : 위를 엮는 상태 기계
/// </summary>
public class Defencer : PlayerRole
{
    // ── 구성 요소 ─────────────────────────────────────────
    FielderAttributes _attr;
    FielderMotion _motion;

    Transform _body;
    Transform _shoulder;
    Transform _gloveHand;
    Transform _throwHand;

    // ── 상태 ─────────────────────────────────────────────
    FIELDER_STATE _state = FIELDER_STATE.PRE_PITCH;
    float _stateTime;
    Vector3 _velocity;
    float _baseY;

    bool _hasBall;
    float _reactionTimer;
    float _secureTimer;
    bool _crowHopped;

    ThrowPlan _plan;
    BATTED_BALL_TYPE _ballType = BATTED_BALL_TYPE.GROUND_BALL;

    bool _actionCommitted;      // 이번 포구에서 다이빙/점프 등을 이미 발동했는가
    int _lastTrajVersion = -1;

    DefenseZone _defenseZone;

    // ── 포구 자세 / 송구 보정 (설계도 8장) ────────────────
    CATCH_POSTURE _posture = CATCH_POSTURE.ROUTINE_STANDING;
    float _throwPowerScale = 1f;

    // ── 자기 송구 재포구 방지 ─────────────────────────────
    float _selfThrowLockout;

    // ── 투수 반환 / 런다운 ────────────────────────────────
    int _returnAttempts;
    int _rundownThrows;
    float _tagLingerTimer;
    bool _pumpFaked;

    // ── 정밀 아웃 판정용 타임스탬프 (설계도 10.4) ─────────
    /// <summary>이 야수가 공을 완전히 확보한 시각(Time.time).</summary>
    float _ballSecuredTime;

    /// <summary>공을 쥔 채 베이스를 밟기 시작한 시각과 그 베이스.</summary>
    BASE_TYPE _controlBase = BASE_TYPE.E_SELF;
    float _baseControlTime;

    /// <summary>직전 송구가 병살의 첫 단계였는가(피벗 스텝 판단용).</summary>
    bool _turningTwo;

    // ── 외부 조회용 ───────────────────────────────────────
    public FIELDER_STATE State { get { return _state; } }
    public bool HasBall { get { return _hasBall; } }
    public BATTED_BALL_TYPE CurrentBallType { get { return _ballType; } }
    public FielderAttributes Attributes { get { return _attr; } }
    public bool IsRunning { get { return _velocity.sqrMagnitude > 0.6f; } }
    public DefenseZone Zone { get { return _defenseZone; } }
    public CATCH_POSTURE LastCatchPosture { get { return _posture; } }
    /// <summary>공을 확보한 시각. 설계도 10.4 의 <c>BallCatchTimestamp</c>.</summary>
    public float BallCatchTimestamp { get { return _ballSecuredTime; } }
    /// <summary>지금 병살 피벗(선행 아웃 → 1루 연결) 중인가.</summary>
    public bool IsTurningTwo { get { return _turningTwo; } }
    public Vector3 GloveWorldPosition { get { return _motion != null ? _motion.GloveWorldPosition : _my.position; } }

    FieldAssignment Assignment
    {
        get { return _bp != null ? _bp.Assignment : FieldAssignment.Idle(_my.position); }
    }

    // ============================================================
    //  초기화
    // ============================================================

    public override void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        base.init(h, tool, t, ball, player, bT);

        _movement = new CatchMovement();   // 하위 호환(외부에서 movementInit 호출)
        _range = new Vector3(h * 0.7f, 0.8f + h, 7.5f);

        // 글러브 오브젝트 활성화
        _item = _item.GetChild(0);
        _item.gameObject.SetActive(true);

        _attr = FielderAttributes.For(t);
        _defenseZone = DefenseZone.CreateForPosition(t);

        CacheRig();
        _motion = new FielderMotion(_my, _body, _shoulder, _gloveHand, _throwHand, _item, _attr);

        _baseY = _my.position.y;
        _hasBall = false;
        _state = FIELDER_STATE.PRE_PITCH;
        _velocity = Vector3.zero;

        FieldMetrics.Refresh(true);

        if (_base != BASE_TYPE.E_SELF && BasePositionProvider.provider != null)
        {
            BasePositionProvider.provider.SetDefencerBaseState(_base, DefencerCode(t));
        }
    }

    void CacheRig()
    {
        _body = _my.childCount > 0 ? _my.GetChild(0) : null;
        _shoulder = (_body != null && _body.childCount > 1) ? _body.GetChild(1) : null;

        if (_shoulder != null)
        {
            if (_shoulder.childCount > 0) _gloveHand = _shoulder.GetChild(0);
            if (_shoulder.childCount > 1) _throwHand = _shoulder.GetChild(1);
        }

        if (_gloveHand == null || _throwHand == null)
        {
            Transform[] all = _my.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (_gloveHand == null && all[i].name.Contains("RightHand")) _gloveHand = all[i];
                if (_throwHand == null && all[i].name.Contains("LeftHand")) _throwHand = all[i];
            }
        }
    }

    /// <summary>BasePositionProvider 의 수비 코드(0=포수, 1=1루수 … 8=투수).</summary>
    static int DefencerCode(PLAYER_TYPE t)
    {
        if (t == PLAYER_TYPE.E_CATCHER) return 0;
        return (int)t + 1;
    }

    public override void SetController(Transform pool, Transform point, Transform target, float speed = 1.9f)
    {
        _controller = new PitchingController(pool, point, target, speed == 0f ? 1.9f : speed);
    }

    public void SetDefenseZone(DefenseZone zone)
    {
        if (zone != null) _defenseZone = zone;
    }

    public override void ResetRole()
    {
        base.ResetRole();
        if (_motion != null) _motion.ResetPose();
        _hasBall = false;
        _velocity = Vector3.zero;
        _state = FIELDER_STATE.PRE_PITCH;
        _stateTime = 0f;
        _reactionTimer = 0f;
        _secureTimer = 0f;
        _crowHopped = false;
        _actionCommitted = false;
        _plan = ThrowPlan.Hold();
        _ballType = BATTED_BALL_TYPE.GROUND_BALL;
        _lastTrajVersion = -1;
        _posture = CATCH_POSTURE.ROUTINE_STANDING;
        _throwPowerScale = 1f;
        _selfThrowLockout = 0f;
        _returnAttempts = 0;
        _rundownThrows = 0;
        _tagLingerTimer = 0f;
        _pumpFaked = false;
        _ballSecuredTime = 0f;
        _controlBase = BASE_TYPE.E_SELF;
        _baseControlTime = 0f;
        _turningTwo = false;
        if (_controller != null && _item != null) _controller.SetItemPoint(_item);
        if (_my != null) _baseY = _my.position.y;
    }

    // ============================================================
    //  타구 이벤트 (Ball → 모든 수비수)
    // ============================================================

    public override void BallEventListener(Vector3 playerPos, Vector3 startPos, Vector3 endPos, Vector3 inc)
    {
        if (_ball == null) return;

        // 타구 + 플레이어(포수)의 송구가 수비 반응 대상이다.
        // 플레이어 송구를 포함시키는 이유 : 설계도 11.3 「송구 미스 시 타격과 동일한
        // 유동적 수비 전환」을 같은 경로로 처리하기 위해서다.
        bool batted = _ball._isBattedBall;
        bool playerThrow = _ball.IsPlayerThrow;
        if (!batted && !playerThrow) return;

        BallTrajectory traj = _ball.Trajectory;
        if (traj == null || !traj.IsValid) return;
        if (traj.Version == _lastTrajVersion) return;
        _lastTrajVersion = traj.Version;

        _ballType = batted ? BattedBallClassifier.ClassifyFromTrajectory(traj) : BATTED_BALL_TYPE.GROUND_BALL;

        // 팀 조율자에게 알림 (팀 쪽에서 Version 으로 중복 호출을 막는다)
        if (_bp != null && _bp.MyTeam != null)
        {
            if (batted) _bp.MyTeam.OnBattedBall(traj, _ballType);
            else _bp.MyTeam.OnLooseBall(traj);
        }

        if (_hasBall) return;

        _actionCommitted = false;
        _crowHopped = false;
        _reactionTimer = _attr.ReactionTime * BattedBallClassifier.ReactionMultiplier(_ballType);

        // 정면 강습 타구는 반응이 거의 즉각적이다
        float distToPath = FieldMetrics.FlatDistance(_my.position, traj.LandPosition);
        if (distToPath < FieldMetrics.M(6f)) _reactionTimer *= 0.45f;

        SetState(FIELDER_STATE.READ);
    }

    // ============================================================
    //  메인 루프
    // ============================================================

    protected override void Move(float speed)
    {
        if (_my == null || _ball == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        _stateTime += dt;
        if (_selfThrowLockout > 0f) _selfThrowLockout -= dt;
        if (_tagLingerTimer > 0f) _tagLingerTimer -= dt;

        FieldMetrics.Refresh();

        // ★ 설계도 11.1 : 캐치 반경 안이면 어떤 상태에서든 100% 포구한다.
        //   베이스 커버 중이든, 백업으로 달리는 중이든, 원위치 복귀 중이든 예외가 없다.
        if (TryGuaranteedCatch()) return;

        switch (_state)
        {
            case FIELDER_STATE.PRE_PITCH: TickPrePitch(dt); break;
            case FIELDER_STATE.READ: TickRead(dt); break;
            case FIELDER_STATE.PURSUE: TickPursue(dt); break;
            case FIELDER_STATE.FIELD: TickField(dt); break;
            case FIELDER_STATE.SECURE: TickSecure(dt); break;
            case FIELDER_STATE.THROW: TickThrow(dt); break;
            case FIELDER_STATE.COVER: TickCover(dt); break;
            case FIELDER_STATE.RECEIVE: TickReceive(dt); break;
            case FIELDER_STATE.TAG: TickTag(dt); break;
            case FIELDER_STATE.RETURN: TickReturn(dt); break;
        }

        // 공을 쥐고 있는 동안은 항상 아웃 판정을 돌린다
        if (_hasBall) CheckOutsWhileHoldingBall();

#if UNITY_EDITOR
        DrawDebugTarget();
#endif
    }

    /// <summary>절차적 모션 적용. Animator 뒤에 실행되도록 LateUpdate 에서 호출된다.</summary>
    public override void lateUpdate()
    {
        if (_motion != null) _motion.Tick(Time.deltaTime, _velocity);
    }

    protected override void PhysicsMoves()
    {
        // 물리 스텝에서는 별도 처리 없음. 모든 판단은 Move() 에서 수행한다.
    }

    void SetState(FIELDER_STATE s)
    {
        if (_state == s) return;
        _state = s;
        _stateTime = 0f;
    }

#if UNITY_EDITOR
    void DrawDebugTarget()
    {
        if (Application.isPlaying && _bp != null)
        {
            Vector3 target = Assignment.Target;
            Color debugColor = Assignment.IsPrimary ? Color.red : Color.green;
            Debug.DrawLine(_my.position, target, debugColor);

            // 원 그리기
            int segments = 16;
            float angle = 0f;
            float step = 360f / segments;
            float radius = 0.5f;
            Vector3 prev = target + new Vector3(Mathf.Sin(0) * radius, 0.1f, Mathf.Cos(0) * radius);
            for (int i = 1; i <= segments; i++)
            {
                angle += step;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 next = target + new Vector3(Mathf.Sin(rad) * radius, 0.1f, Mathf.Cos(rad) * radius);
                Debug.DrawLine(prev, next, debugColor);
                prev = next;
            }
        }
    }
#endif

    // ============================================================
    //  캐치 반경 100% 포구 보장 (설계도 11.1)
    // ============================================================

    /// <summary>
    /// 공과의 3D 거리가 캐치 반경 안이면 무조건 포구한다.
    /// 다이빙 · 슬라이딩 · 스트레치 중에는 몸이 늘어난 만큼 반경이 확장된다.
    /// </summary>
    bool TryGuaranteedCatch()
    {
        if (_hasBall) return false;
        if (_ball == null || !_ball.IsLiveForDefense) return false;
        if (_selfThrowLockout > 0f) return false;          // 내가 방금 던진 공을 다시 잡지 않는다
        if (_motion != null && _motion.IsThrowing) return false;

        // 중계맨이 "통과(Let it go)" 지시를 받았으면 일부러 잡지 않는다(설계도 5.3)
        if (ShouldLetBallGo()) return false;

        // AI → AI 송구는 지정된 수구자만 받는다.
        // (이 가드가 없으면 송구 라인 근처의 엉뚱한 야수가 병살 송구를 가로챈다.)
        if (!IsDesignatedReceiverOrLoose()) return false;

        float radius = (_motion != null && _motion.IsExtended)
            ? _attr.ExtendedCatchRadius
            : _attr.CatchRadius;

        Vector3 ballPos = _ball.GetPosition();

        // 글러브 기준과 몸통(가슴) 기준 중 가까운 쪽을 쓴다.
        Vector3 chest = new Vector3(_my.position.x,
                                    FieldMetrics.GroundY + _attr.VerticalReach * 0.45f,
                                    _my.position.z);
        float d = Mathf.Min(Vector3.Distance(GloveWorldPosition, ballPos),
                            Vector3.Distance(chest, ballPos));

        if (d > radius) return false;

        CatchBall();
        return true;
    }

    /// <summary>
    /// 이 공을 내가 잡아도 되는가.
    ///   · 타구 · 플레이어 송구 · 이미 땅에 닿은(빗나간) 공 → 누구나 처리 가능(루즈볼)
    ///   · AI 송구  → 지정된 수구자, 또는 「컷」 지시를 받은 중계맨만
    /// </summary>
    bool IsDesignatedReceiverOrLoose()
    {
        if (!_ball.IsFielderThrow) return true;                 // 타구 · 플레이어 송구 · 루즈볼
        if (_ball.HasTouchedGround) return true;                // 빗나가 바운드된 송구 = 루즈볼

        Transform target = _ball.ThrowReceiver;
        if (target == null) return true;                        // 수구자 미지정 송구

        if (target == _my || target.IsChildOf(_my)) return true;

        FieldAssignment a = Assignment;

        // 커버 요원이 도중에 교체되었더라도, 내가 지키는 베이스로 날아온 송구는 받는다.
        if (a.Role == DEFENSIVE_ROLE.BASE_COVER && a.CoverBase != BASE_TYPE.E_SELF)
        {
            Vector3 basePos = FieldMetrics.BasePos(a.CoverBase);
            if (FieldMetrics.FlatDistance(_ball.GetPosition(), basePos) < FieldMetrics.M(3f)) return true;
        }

        // 플레이어가 「컷」을 콜했다면 중계맨은 가로챌 권한을 얻는다(설계도 5.3)
        if (DefenseCommand.CutRequested &&
            (a.Role == DEFENSIVE_ROLE.CUTOFF || a.Role == DEFENSIVE_ROLE.RELAY)) return true;

        return false;
    }

    /// <summary>플레이어의 "통과" 콜을 받은 중계맨인가.</summary>
    bool ShouldLetBallGo()
    {
        if (!DefenseCommand.LetGoRequested) return false;
        DEFENSIVE_ROLE role = Assignment.Role;
        if (role != DEFENSIVE_ROLE.CUTOFF && role != DEFENSIVE_ROLE.RELAY) return false;
        // 홈 승부를 위해 흘려보내는 상황에서만 유효하다
        return true;
    }

    // ============================================================
    //  상태별 처리
    // ============================================================

    void TickPrePitch(float dt)
    {
        Vector3 home = _bp != null ? _bp.HomePosition : _my.position;
        if (FieldMetrics.FlatDistance(_my.position, home) > FieldMetrics.M(0.6f))
        {
            Steer(home, dt, 0.7f);
        }
        else
        {
            Decelerate(dt);
            FaceToward(FieldMetrics.Home, dt);
            if (_stateTime > 1.4f)
            {
                _stateTime = 0f;
                if (_motion != null) _motion.PlayReady();
            }
        }

        // 굴러온 공이나 날아온 송구가 근처에 있으면 반응
        if (!_hasBall && ShouldReactToLooseBall())
        {
            _actionCommitted = false;
            SetState(FIELDER_STATE.FIELD);
        }
    }

    void TickRead(float dt)
    {
        Decelerate(dt);
        FaceToward(_ball.GetPosition(), dt, 14f);

        _reactionTimer -= dt;
        if (_reactionTimer > 0f) return;

        DEFENSIVE_ROLE role = Assignment.Role;
        if (role == DEFENSIVE_ROLE.CHASER) SetState(FIELDER_STATE.PURSUE);
        else if (role == DEFENSIVE_ROLE.IDLE) SetState(FIELDER_STATE.RETURN);
        else SetState(FIELDER_STATE.COVER);
    }

    void TickPursue(float dt)
    {
        FieldAssignment a = Assignment;

        if (a.Role != DEFENSIVE_ROLE.CHASER)
        {
            // 다른 야수가 콜했다 — 커버/백업으로 전환
            SetState(a.Role == DEFENSIVE_ROLE.IDLE ? FIELDER_STATE.RETURN : FIELDER_STATE.COVER);
            return;
        }

        Vector3 target = a.Target;
        target.y = _baseY;

        float distToTarget = FieldMetrics.FlatDistance(_my.position, target);
        bool campAndWait = a.Intercept.CanReach && a.Intercept.IsAirCatch &&
                           a.Intercept.Slack > DefenseTuning.CAMP_UNDER_BALL_SLACK &&
                           distToTarget < FieldMetrics.M(DefenseTuning.ARRIVE_DISTANCE * 2f);

        if (campAndWait)
        {
            // 낙구 지점에 미리 자리를 잡고 공을 올려다본다
            Decelerate(dt);
            FaceToward(_ball.GetPosition(), dt, 8f);
        }
        else
        {
            Steer(target, dt, 1f);
        }

        // 공이 사정권에 들어오면 포구 단계로
        float ballDist = FieldMetrics.FlatDistance(_my.position, _ball.GetPosition());
        float ballHeight = _ball.GetPosition().y - FieldMetrics.GroundY;

        bool readyToField = ballDist < FieldMetrics.M(DefenseTuning.FIELD_ACTION_RANGE);
        
        // 플라이볼을 기다리는 중이라면 공이 머리 위 가까이(수직 도달 범위 근처) 내려올 때까지 포구 단계로 넘어가지 않는다.
        // 미리 넘어갈 경우, 2D 거리는 가깝지만 고도가 높은 공의 '그림자'를 따라 이동하며 낙구 지점을 이탈하는 버그가 발생한다.
        if (a.Intercept.IsAirCatch && ballHeight > _attr.VerticalReach + _attr.JumpReach + FieldMetrics.M(1.5f))
        {
            readyToField = false;
        }

        if (_ball.isCatchable() && readyToField)
        {
            _actionCommitted = false;
            SetState(FIELDER_STATE.FIELD);
        }
    }

    void TickField(float dt)
    {
        if (!_ball.isCatchable())
        {
            SetState(FIELDER_STATE.COVER);
            return;
        }

        Vector3 ballPos = _ball.GetPosition();
        float ballDist = FieldMetrics.FlatDistance(_my.position, ballPos);
        float ballHeight = ballPos.y - FieldMetrics.GroundY;

        // ── 1. 포구 동작 결정 (한 번만) ──
        if (!_actionCommitted && !_motion.IsLocked)
        {
            DecideCatchAction(ballPos, ballDist, ballHeight);
        }

        // ── 2. 몸 이동 : 액션 중이 아니면 공의 예상 위치로 미세 조정 ──
        if (!_motion.IsLocked)
        {
            Vector3 leadPoint = PredictShortTermBallSpot();
            Steer(leadPoint, dt, 1f);
            FaceToward(ballPos, dt, 12f);
        }

        // ── 3. 글러브를 공으로 ──
        if (ballDist < FieldMetrics.M(DefenseTuning.GLOVE_REACH_RANGE) &&
            ballHeight < _attr.VerticalReach + _attr.JumpReach)
        {
            _motion.SetGloveTarget(ballPos);
        }
        else
        {
            _motion.ClearGloveTarget();
        }

        // ── 4. 포구 판정 ──
        float snap = FieldMetrics.M(DefenseTuning.CATCH_SNAP_DISTANCE) * (_motion.IsExtended ? 1.6f : 1f);
        if (Vector3.Distance(_motion.GloveWorldPosition, ballPos) < snap)
        {
            CatchBall();
            return;
        }

        // ── 5. 놓친 경우 : 공이 나를 지나쳐 갔으면 다시 추격 ──
        if (_stateTime > 0.4f && !_motion.IsLocked)
        {
            Vector3 ballVel = _ball.GetVelocity();
            Vector3 toMe = _my.position - ballPos;
            toMe.y = 0f;
            bool goingAway = Vector3.Dot(ballVel, toMe) < 0f;
            if (goingAway && ballDist > FieldMetrics.M(DefenseTuning.FIELD_ACTION_RANGE * 1.3f))
            {
                _actionCommitted = false;
                SetState(Assignment.Role == DEFENSIVE_ROLE.CHASER ? FIELDER_STATE.PURSUE : FIELDER_STATE.COVER);
            }
        }
    }

    /// <summary>다이빙 / 점프 / 슬라이딩 / 정면 포구 중 무엇을 할지 결정한다.</summary>
    void DecideCatchAction(Vector3 ballPos, float ballDist, float ballHeight)
    {
        FieldAssignment a = Assignment;
        CATCH_EFFORT effort = a.Intercept.CanReach ? a.Intercept.Effort : CATCH_EFFORT.HUSTLE;

        Vector3 dir = ballPos - _my.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = _my.forward;
        dir.Normalize();

        bool isPitcher = _type == PLAYER_TYPE.E_PITCHER;
        float reach = _attr.GloveReach;

        // 점프 : 머리 위로 넘어가는 공
        if (!isPitcher && ballHeight > _attr.VerticalReach * 0.88f &&
            ballHeight <= _attr.VerticalReach + _attr.JumpReach &&
            ballDist < reach * 1.6f)
        {
            _motion.PlayJump(dir, _attr.JumpReach * 0.9f);
            _actionCommitted = true;
            _velocity = Vector3.zero;
            return;
        }

        // 다이빙 : 서서는 닿지 않지만 몸을 날리면 닿는 거리
        if (!isPitcher && ballDist > reach * 1.05f && ballDist < reach + _attr.DiveExtension &&
            ballHeight < _attr.VerticalReach * 0.85f &&
            (effort == CATCH_EFFORT.DIVE || effort == CATCH_EFFORT.HUSTLE || a.Intercept.Slack < 0.1f))
        {
            _motion.PlayDive(dir, _attr.DiveExtension * 0.85f);
            _actionCommitted = true;
            _velocity = Vector3.zero;
            return;
        }

        // 슬라이딩 : 낮게 굴러오는 공을 옆으로 미끄러지며 차단
        if (!isPitcher && ballHeight < FieldMetrics.M(0.8f) &&
            ballDist > reach * 1.0f && ballDist < reach + _attr.DiveExtension * 0.8f &&
            IsRunning)
        {
            _motion.PlaySlide(dir, _attr.DiveExtension * 0.7f);
            _actionCommitted = true;
            _velocity = Vector3.zero;
            return;
        }

        // 평범한 땅볼 : 무릎을 굽히고 정면에서 잡는다
        if (ballHeight < FieldMetrics.M(1.0f))
        {
            _motion.PlayFieldGrounder();
            _actionCommitted = true;
        }
    }

    /// <summary>다음 몇 프레임 뒤 공이 있을 지면 위치(짧은 예측).</summary>
    Vector3 PredictShortTermBallSpot()
    {
        Vector3 p = _ball.GetPosition();
        Vector3 v = _ball.GetVelocity();
        float lead = 0.12f;
        Vector3 spot = p + v * lead;
        spot.y = _baseY;
        return spot;
    }

    void TickSecure(float dt)
    {
        Decelerate(dt);
        _secureTimer -= dt;

        // 외야수는 송구 전에 크로우 홉으로 도움닫기
        if (!_crowHopped && _attr.IsOutfielder &&
            (_plan.Kind == THROW_KIND.TO_BASE || _plan.Kind == THROW_KIND.RELAY))
        {
            _crowHopped = true;
            Vector3 d = _plan.AimPoint - _my.position;
            d.y = 0f;
            _motion.PlayCrowHop(d);
        }

        if (_plan.Kind == THROW_KIND.TO_BASE || _plan.Kind == THROW_KIND.RELAY ||
            _plan.Kind == THROW_KIND.TO_PITCHER)
        {
            FaceToward(_plan.AimPoint, dt, 16f);
        }

        if (_secureTimer > 0f || _motion.IsLocked) return;

        switch (_plan.Kind)
        {
            case THROW_KIND.TO_BASE:
            case THROW_KIND.RELAY:
                BeginThrow();
                break;

            case THROW_KIND.TO_PITCHER:
                // 반환 계획이 이미 세워진 송구면 그대로 던지고,
                // 아직 계획이 없으면 반환 시퀀스를 구성한다(외야 → 내야 → 투수).
                if (_plan.IsReturnThrow) BeginThrow();
                else FinishPlayWithBall();
                break;

            case THROW_KIND.RUN_TO_BASE:
                SetState(FIELDER_STATE.COVER);
                break;

            case THROW_KIND.TAG_RUNNER:
                _rundownThrows = 0;
                SetState(FIELDER_STATE.TAG);
                break;

            default:
                // 던질 곳이 없음 — 플레이 종료 절차
                FinishPlayWithBall();
                break;
        }
    }

    void BeginThrow()
    {
        Vector3 aim = _plan.AimPoint;

        if (_plan.Kind == THROW_KIND.TO_PITCHER)
        {
            Transform pitcherHand = (_bp != null && _bp.MyTeam != null)
                ? _bp.MyTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER) : null;
            if (pitcherHand == null || _type == PLAYER_TYPE.E_PITCHER)
            {
                HardFinishWithBall();
                return;
            }
            aim = pitcherHand.position;
        }

        float dist = Vector3.Distance(_motion.GloveWorldPosition, aim);

        // 중계가 필요한 거리라면 중계맨으로 목표 변경
        if (dist > _attr.MaxThrowRange * 0.95f)
        {
            BaseballPlayer relay = FindRelayMan(aim);
            if (relay != null)
            {
                Transform g = relay.GetItem();
                if (g != null) { aim = g.position; dist = Vector3.Distance(_motion.GloveWorldPosition, aim); }
            }
        }

        float crowHop = _attr.IsOutfielder ? DefenseTuning.CROW_HOP_DURATION : 0f;
        float duration = Mathf.Clamp((_attr.ReleaseTime - crowHop) / 0.70f, 0.30f, 1.0f);
        bool sidearm = _attr.IsInfielder && dist < FieldMetrics.M(24f);

        // 설계도 8장 : 포구 자세에 따라 송구 파워가 떨어지면 도착도 그만큼 늦어진다.
        float flight;
        if (_plan.SpeedOverrideRaw > 0f)
        {
            flight = _attr.ThrowFlightTimeAt(dist, _plan.SpeedOverrideRaw);
        }
        else
        {
            flight = _attr.ThrowFlightTime(dist) / Mathf.Clamp(_throwPowerScale, 0.5f, 1f);
        }

        Vector3 finalAim = aim;
        _plan.AimPoint = finalAim;
        _plan.FlightTime = flight;

        FaceToward(finalAim, 1f, 999f);
        _motion.PlayThrow(finalAim, duration, sidearm, delegate ()
        {
            ReleaseBall(finalAim, flight);
        });

        SetState(FIELDER_STATE.THROW);
    }

    void ReleaseBall(Vector3 aim, float flightTime)
    {
        if (!_hasBall || _ball == null) return;
        _hasBall = false;
        _controlBase = BASE_TYPE.E_SELF;
        _selfThrowLockout = 0.45f;    // 내가 던진 공을 그 자리에서 다시 잡지 않도록

        Transform origin = _throwHand != null ? _throwHand : _item;
        _ball.ThrowByFielder(origin.position, aim, flightTime);

        // 지정 수구자를 명시해 중간 가로채기를 막는다(설계도 11.2 : AI→AI 송구는 무오차).
        Transform receiverTr = null;
        if (_plan.Receiver != null) receiverTr = _plan.Receiver.transform;
        else if (_plan.Kind == THROW_KIND.TO_PITCHER && _bp != null && _bp.MyTeam != null)
        {
            BaseballPlayer p = _bp.MyTeam.GetPlayer(PLAYER_TYPE.E_PITCHER);
            if (p != null) receiverTr = p.transform;
        }
        _ball.SetThrowReceiver(receiverTr);

        // 사용자 지정 송구는 한 번 이행하면 소비된다(다음 송구는 다시 AI 판단).
        if (_plan.IsUserCommanded) DefenseCommand.ConsumeThrowCommand();

        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.OnThrowReleased(_bp, _plan.TargetBase, aim);
        }
    }

    void TickThrow(float dt)
    {
        Decelerate(dt);
        if (_motion.IsThrowing) return;

        if (_hasBall)
        {
            // 모션이 중단되어 릴리스가 안 된 경우의 안전 장치
            FinishPlayWithBall();
            return;
        }

        SetState(FIELDER_STATE.COVER);
    }

    void TickCover(float dt)
    {
        FieldAssignment a = Assignment;

        // ── 공을 쥐고 있는 경우가 최우선 ──
        if (_hasBall)
        {
            if (_plan.Kind == THROW_KIND.RUN_TO_BASE)
            {
                Vector3 basePos = FieldMetrics.BasePos(_plan.TargetBase);
                basePos.y = _baseY;
                Steer(basePos, dt, 1f);
                if (FieldMetrics.FlatDistance(_my.position, basePos) < FieldMetrics.M(DefenseTuning.BASE_TOUCH_RANGE))
                {
                    CheckBaseForceOut(_plan.TargetBase);
                    // 아웃이 성립하지 않았다면(주자가 먼저 도착) 잠시 뒤 다음 플레이를 다시 판단
                    if (_hasBall && _plan.Kind == THROW_KIND.RUN_TO_BASE && _stateTime > 0.35f)
                    {
                        ReplanThrow(0.05f);
                    }
                }
            }
            else
            {
                Decelerate(dt);
                if (_stateTime > 0.15f) ReplanThrow(0f);
            }
            return;
        }

        if (a.Role == DEFENSIVE_ROLE.CHASER)
        {
            SetState(FIELDER_STATE.PURSUE);
            return;
        }

        if (!a.Live)
        {
            SetState(FIELDER_STATE.RETURN);
            return;
        }

        Vector3 target = a.Target;
        target.y = _baseY;

        // 「통과」 지시를 받은 중계맨은 송구 라인에서 한 발 비켜선다(설계도 5.3)
        if ((a.Role == DEFENSIVE_ROLE.CUTOFF || a.Role == DEFENSIVE_ROLE.RELAY) && DefenseCommand.LetGoRequested)
        {
            Vector3 side = Vector3.Cross(Vector3.up, (a.FaceTarget - target).normalized);
            target += side * FieldMetrics.M(2.0f);
        }

        float d = FieldMetrics.FlatDistance(_my.position, target);
        if (d > FieldMetrics.M(DefenseTuning.ARRIVE_DISTANCE))
        {
            Steer(target, dt, a.Role == DEFENSIVE_ROLE.BASE_COVER ? 1f : 0.92f);
        }
        else
        {
            Decelerate(dt);
            FaceToward(a.FaceTarget, dt, 9f);
        }

        // 나에게 오는 송구 / 튀어나온 공에 반응
        // (백업 야수가 수구자의 공을 가로채지 않도록 역할과 거리로 제한한다)
        bool isRelayRole = a.Role == DEFENSIVE_ROLE.CUTOFF || a.Role == DEFENSIVE_ROLE.RELAY;
        bool canReceive = a.Role == DEFENSIVE_ROLE.BASE_COVER || isRelayRole;

        // 플레이어가 「컷」을 콜했으면 중계맨은 적극적으로 가로채러 나간다
        if (isRelayRole && DefenseCommand.CutRequested) canReceive = true;
        if (isRelayRole && DefenseCommand.LetGoRequested) canReceive = false;

        if (!canReceive)
        {
            canReceive = FieldMetrics.FlatDistance(_my.position, _ball.GetPosition()) < FieldMetrics.M(2.5f);
        }
        if (!canReceive && a.Role != DEFENSIVE_ROLE.BASE_COVER)
        {
            // 멈춰 있는 공은 커버가 아닌 야수가 회수한다(플레이 교착 방지)
            canReceive = _ball.isCatchable() &&
                         _ball.GetVelocity().sqrMagnitude < FieldMetrics.M(1.2f) * FieldMetrics.M(1.2f);
        }
        if (canReceive && ShouldReactToLooseBall()) SetState(FIELDER_STATE.RECEIVE);
    }

    void TickReceive(float dt)
    {
        if (!_ball.isCatchable() || _hasBall)
        {
            SetState(FIELDER_STATE.COVER);
            return;
        }

        Vector3 ballPos = _ball.GetPosition();
        float dist = Vector3.Distance(_motion.GloveWorldPosition, ballPos);
        float flat = FieldMetrics.FlatDistance(_my.position, ballPos);

        FieldAssignment a = Assignment;
        bool onBase = a.Role == DEFENSIVE_ROLE.BASE_COVER;

        if (onBase)
        {
            // ── 설계도 10.1 : 베이스 중앙(주자 슬라이딩 주로)을 비우고 안쪽 모서리에 발을 대고
            //    송구 방향으로 몸을 최대한 뻗는다(Max Stretch). ──
            Vector3 spot = a.Target;      // 조율자가 계산해 준 모서리/스트래들 위치
            spot.y = _baseY;
            if (FieldMetrics.FlatDistance(_my.position, spot) > FieldMetrics.M(0.5f))
            {
                Steer(spot, dt, 1f);
            }
            else
            {
                Decelerate(dt);
                if (flat < FieldMetrics.M(6f) && _motion.CurrentAction != FIELDER_ACTION.STRETCH)
                {
                    _motion.PlayStretch(ballPos);
                }
            }
        }
        else
        {
            // 중계맨/백업은 공 쪽으로 마중 나간다
            Vector3 spot = PredictShortTermBallSpot();
            if (flat > FieldMetrics.M(1.2f)) Steer(spot, dt, 1f);
            else Decelerate(dt);
        }

        FaceToward(ballPos, dt, 16f);
        _motion.SetGloveTarget(ballPos);

        float snap = FieldMetrics.M(DefenseTuning.CATCH_SNAP_DISTANCE) * 1.5f;
        if (dist < snap)
        {
            CatchBall();
            return;
        }

        // 공이 지나가 버렸다면 다시 커버 위치로
        if (_stateTime > 3.5f) SetState(FIELDER_STATE.COVER);
    }

    // ============================================================
    //  태그 / 런다운 (설계도 9장 · 10.2)
    // ============================================================

    void TickTag(float dt)
    {
        RunnerInfo r = _plan.TargetRunner;
        if (!r.IsValid || !r.Player.gameObject.activeSelf || !_hasBall)
        {
            SetState(FIELDER_STATE.COVER);
            return;
        }

        // 주자 정보를 매 프레임 갱신(위치/베이스가 바뀌므로)
        r = RefreshRunner(r);
        if (!r.IsValid || r.IsSafe)
        {
            ReplanThrow(0.05f);
            return;
        }

        Vector3 rp = r.Player.transform.position;
        Team myTeam = _bp != null ? _bp.MyTeam : null;

        RundownOrder order = RundownPlay.Solve(_bp, r, myTeam, _rundownThrows);

        if (order.Valid)
        {
            // ── 원칙 ③ : 주자가 역방향으로 돌면 즉시 플립 토스 ──
            if (order.ShouldFlip && order.FlipReceiver != null && _rundownThrows < RundownPlay.MAX_THROWS)
            {
                _rundownThrows++;
                _plan.Kind = THROW_KIND.TO_BASE;
                _plan.TargetBase = order.FlipBase;
                _plan.Receiver = order.FlipReceiver;
                _plan.AimPoint = order.FlipAim;
                _plan.SpeedOverrideRaw = DefenseTuning.RETURN_GENTLE_SPEED;  // 짧은 플립 토스
                _secureTimer = 0.08f;
                _crowHopped = true;                                          // 크로우 홉 생략
                SetState(FIELDER_STATE.SECURE);
                return;
            }

            // ── 원칙 ③ : 거리가 압축되면 펌프 페이크 1회로 주자의 감속을 유도한다 ──
            if (order.PumpFake && !_pumpFaked)
            {
                _pumpFaked = true;
                _motion.PlayPumpFake(FieldMetrics.BasePos(order.DriveToBase));
            }

            // ── 원칙 ① ② : 하위 베이스 쪽으로 몰면서 4~5m 까지 압축 ──
            Steer(order.ChasePoint, dt, 1f);
            FaceToward(rp, dt, 18f);
        }
        else
        {
            Steer(rp, dt, 1f);
            FaceToward(rp, dt, 18f);
        }

        // ── 설계도 10.2 : 다운 앤 스위프 태그 ──
        if (FieldMetrics.FlatDistance(_my.position, rp) < FieldMetrics.M(DefenseTuning.TAG_RANGE))
        {
            if (_motion.CurrentAction != FIELDER_ACTION.SWEEP_TAG)
            {
                _motion.PlaySweepTag(rp);
                _tagLingerTimer = DefenseTuning.TAG_LINGER_TIME;   // 미트를 붙여두는 링거 태그
            }
            CheckTagOut(r.Player);
            if (_hasBall && _tagLingerTimer <= 0f) ReplanThrow(0.1f);
        }

        if (_stateTime > 3.0f && _hasBall) ReplanThrow(0f);
    }

    /// <summary>주자 스냅샷을 최신 상태로 다시 읽는다.</summary>
    RunnerInfo RefreshRunner(RunnerInfo old)
    {
        List<RunnerInfo> list = RunnerTracker.Snapshot(true);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Player == old.Player) return list[i];
        }
        return new RunnerInfo();
    }

    void TickReturn(float dt)
    {
        Vector3 home = _bp != null ? _bp.HomePosition : _my.position;
        home.y = _baseY;

        // 설계도 12.3 : 상황 종료 후에는 전력 질주가 아니라 자연스러운 걸음/조깅으로 복귀한다.
        float walkScale = Mathf.Clamp01(FieldMetrics.M(DefenseTuning.RESET_WALK_SPEED) / Mathf.Max(0.01f, _attr.TopSpeed));

        if (FieldMetrics.FlatDistance(_my.position, home) > FieldMetrics.M(DefenseTuning.READY_ARRIVE_RADIUS))
        {
            Steer(home, dt, walkScale);
        }
        else
        {
            Decelerate(dt);
            FaceToward(FieldMetrics.Home, dt);
            SetState(FIELDER_STATE.PRE_PITCH);      // READY
        }

        FieldAssignment a = Assignment;
        if (a.Live && a.Role != DEFENSIVE_ROLE.IDLE)
        {
            SetState(a.Role == DEFENSIVE_ROLE.CHASER ? FIELDER_STATE.PURSUE : FIELDER_STATE.COVER);
        }
    }

    // ============================================================
    //  이동 / 회전
    // ============================================================

    void Steer(Vector3 target, float dt, float speedScale)
    {
        if (_motion != null && _motion.IsLocked) { return; }   // 액션이 루트를 소유

        Vector3 to = target - _my.position;
        to.y = 0f;
        float dist = to.magnitude;

        float topSpeed = _attr.TopSpeed * Mathf.Clamp(speedScale, 0.2f, 1.3f);
        float accel = _attr.Acceleration * 1.8f;

        if (dist < 0.02f)
        {
            Decelerate(dt);
            return;
        }

        Vector3 dir = to / dist;

        // 도착 감속 : v = sqrt(2*a*d)
        float arriveSpeed = Mathf.Sqrt(2f * accel * 0.55f * dist);
        float desiredSpeed = Mathf.Min(topSpeed, arriveSpeed);

        // 방향 전환 효율(뒤로 물러날 때 느려짐)
        float dirFactor = _attr.DirectionFactor(_my.forward, dir);
        desiredSpeed *= Mathf.Lerp(dirFactor, 1f, 0.35f);

        Vector3 desiredVel = dir * desiredSpeed;
        _velocity = Vector3.MoveTowards(_velocity, desiredVel, accel * dt);

        Vector3 p = _my.position + _velocity * dt;
        p.y = _baseY;
        _my.position = p;

        if (_velocity.sqrMagnitude > 0.25f) FaceToward(_my.position + _velocity, dt, DefenseTuning.BODY_TURN_SPEED);
    }

    void Decelerate(float dt)
    {
        if (_motion != null && _motion.IsLocked) return;
        _velocity = Vector3.MoveTowards(_velocity, Vector3.zero, _attr.Acceleration * 2.4f * dt);
        if (_velocity.sqrMagnitude > 0.0001f)
        {
            Vector3 p = _my.position + _velocity * dt;
            p.y = _baseY;
            _my.position = p;
        }
    }

    void FaceToward(Vector3 point, float dt, float speed = 10f)
    {
        if (_motion != null && _motion.IsLocked) return;
        Vector3 d = point - _my.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0004f) return;
        Quaternion look = Quaternion.LookRotation(d.normalized, Vector3.up);
        _my.rotation = (speed >= 999f)
            ? look
            : Quaternion.Slerp(_my.rotation, look, 1f - Mathf.Exp(-speed * dt));
    }

    // ============================================================
    //  포구 / 송구
    // ============================================================

    bool ShouldReactToLooseBall()
    {
        if (_ball == null || !_ball.isCatchable() || _hasBall) return false;
        if (_selfThrowLockout > 0f) return false;

        // 투구(아직 배트에 맞지 않은 공)나 마운드에 놓인 공에는 반응하지 않는다.
        // 이 가드가 없으면 투수가 대기 중인 공을 집어들어 플레이가 무한히 종료된다.
        if (!_ball.IsLiveForDefense) return false;

        Vector3 ballPos = _ball.GetPosition();
        Vector3 vel = _ball.GetVelocity();
        Vector3 toMe = _my.position - ballPos;
        toMe.y = 0f;
        float dist = toMe.magnitude;

        // 공이 거의 멈춰 있으면(악송구·펜스 반사 등) 베이스 커버가 아닌 야수가 멀리서도 회수하러 간다.
        // 이 안전장치가 없으면 플레이가 영원히 끝나지 않을 수 있다.
        bool atRest = vel.sqrMagnitude < FieldMetrics.M(1.2f) * FieldMetrics.M(1.2f);
        if (atRest && Assignment.Role != DEFENSIVE_ROLE.BASE_COVER)
        {
            return dist < FieldMetrics.M(26f);
        }

        if (dist > FieldMetrics.M(9f)) return false;
        if (dist < FieldMetrics.M(2.2f)) return true;

        // 나를 향해 오고 있는가
        return Vector3.Dot(vel, toMe.normalized) > FieldMetrics.M(1.5f);
    }

    void CatchBall()
    {
        if (_ball == null) return;

        // ★ GrabBall 이 플래그를 지우기 전에 먼저 읽어둔다.
        bool wasBattedBall = _ball._isBattedBall;
        bool directOut = _ball.CanBeDirectOut();

        // ── 설계도 8장 : 이번 포구의 자세를 판별한다 ──
        _posture = ClassifyPosture();
        CatchPostureSpec spec = CatchPostureTable.For(_posture);
        _throwPowerScale = spec.PowerScale;
        float readyTime = CatchPostureTable.ReadyTimeFor(_posture, _attr);

        _motion.ClearGloveTarget();
        _motion.StopAction();

        if (_controller != null && _item != null) _controller.SetItemPoint(_item);

        _item.position = _ball.GetPosition();
        _ball.GrabBall(_item);
        _hasBall = true;
        _ballSecuredTime = Time.time;      // 설계도 10.4 : 포스 아웃 판정의 기준 시각
        _controlBase = BASE_TYPE.E_SELF;
        _velocity = Vector3.zero;
        _returnAttempts = 0;
        _rundownThrows = 0;
        _pumpFaked = false;
        _turningTwo = false;

        if (_bp != null && _bp.MyTeam != null) _bp.MyTeam.NotifyBallCaught(_bp);

        // ── 노바운드 포구 = 플라이 아웃 ──
        if (wasBattedBall && directOut)
        {
            _ball.SetBattedBall(false);
            HandleFlyOut(readyTime);
            return;
        }

        _ball.SetBattedBall(false);
        ReplanThrow(readyTime);
    }

    /// <summary>포구 순간의 자세를 판별한다(설계도 8장 표).</summary>
    CATCH_POSTURE ClassifyPosture()
    {
        Vector3 ballPos = _ball.GetPosition();
        float ballHeight = ballPos.y - FieldMetrics.GroundY;

        Vector3 dir = ballPos - _my.position;
        dir.y = 0f;
        bool backhand = dir.sqrMagnitude > 0.0001f &&
                        Vector3.Dot(_my.right, dir.normalized) < -0.55f;   // 글러브 반대편

        bool nearFence = FieldMetrics.DistFromHome(_my.position) >
                         FieldMetrics.M(DefenseTuning.OUTFIELD_FENCE_DISTANCE - DefenseTuning.WALL_CONTACT_DISTANCE);

        bool weakGrounder = _ball.IsRolling &&
                            _ball.GetVelocity().magnitude < FieldMetrics.M(DefenseTuning.BAREHAND_BALL_SPEED);

        return CatchPostureTable.Classify(_motion.CurrentAction, IsRunning, ballHeight,
                                          backhand, nearFence, weakGrounder);
    }

    /// <summary>노바운드 포구 — 타자 아웃 처리 후 정상 반환 시퀀스로 이어진다.</summary>
    void HandleFlyOut(float readyTime)
    {
        // [수정] 예전 구현은 gp.PlayerOut(...) 이 내부에서 타자를 교체하고 그 뒤에 또
        //        ChangeAttacker() 를 불러 <b>타순이 한 명씩 건너뛰었다.</b>
        //        여기서는 아웃 기록만 하고, 타자 교체는 Team.CompletePlay() 에서 한 번만 수행한다.
        GamePlayerProvider gp = GamePlayerProvider.provider;
        if (gp != null) gp.RegisterBatterOut();

        // 리터치 실패(어필 플레이)나 남은 주자 견제가 가능한지 다시 판단하고,
        // 잡을 게 없으면 그대로 투수 반환 시퀀스로 넘어간다.
        RunnerTracker.Snapshot(true);
        ReplanThrow(readyTime);
    }

    /// <summary>후속 플레이를 다시 계산하고 SECURE 상태로 들어간다.</summary>
    void ReplanThrow(float transferDelay)
    {
        if (!_hasBall) return;

        Team myTeam = _bp != null ? _bp.MyTeam : null;
        _plan = ThrowDecision.Decide(_bp, _attr, _motion.GloveWorldPosition, myTeam, 0f);

        _secureTimer = Mathf.Max(DefenseTuning.MIN_SECURE_TIME, transferDelay);
        _crowHopped = false;
        SetState(FIELDER_STATE.SECURE);
    }

    /// <summary>
    /// 더 이상 아웃을 잡을 수 없다 — 설계도 12.2 의 투수 반환 시퀀스를 시작한다.
    ///   외야에서 주움 → 가장 가까운 내야수에게 1차 중계(24m/s) → 내야수가 투수에게(16m/s)
    ///   내야에서 주움 → 투수에게 직접 부드럽게(16m/s)
    ///   투수 본인    → 마운드에서 공 확보 → 전원 리셋 브로드캐스트
    /// </summary>
    void FinishPlayWithBall()
    {
        Team myTeam = _bp != null ? _bp.MyTeam : null;

        if (_type == PLAYER_TYPE.E_PITCHER)
        {
            HardFinishWithBall();
            return;
        }

        if (myTeam == null || _returnAttempts >= 2)
        {
            HardFinishWithBall();
            return;
        }

        ThrowPlan ret = ThrowDecision.BuildReturnToPitcher(_bp, _attr, _motion.GloveWorldPosition, myTeam);
        if (ret.Kind == THROW_KIND.HOLD)
        {
            HardFinishWithBall();
            return;
        }

        _returnAttempts++;
        _plan = ret;
        _secureTimer = Mathf.Max(DefenseTuning.MIN_SECURE_TIME, 0.18f);
        _crowHopped = false;
        SetState(FIELDER_STATE.SECURE);
    }

    /// <summary>
    /// 반환 송구를 만들 수 없는 예외 상황(투수 없음 · 반복 실패)에서의 안전한 마감.
    /// 공을 투수 글러브로 회수하고 플레이를 종료한다.
    /// </summary>
    void HardFinishWithBall()
    {
        _hasBall = false;

        Team myTeam = _bp != null ? _bp.MyTeam : null;
        if (myTeam != null)
        {
            if (_type != PLAYER_TYPE.E_PITCHER)
            {
                Transform pitcherHand = myTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
                if (pitcherHand != null && _ball != null) _ball.GrabBall(pitcherHand);
            }
            myTeam.OnPitcherSecuredBall();
        }

        SetState(FIELDER_STATE.RETURN);
    }

    BaseballPlayer FindRelayMan(Vector3 aim)
    {
        if (_bp == null || _bp.MyTeam == null) return null;
        return _bp.MyTeam.GetRelayFielder(_my.position, aim, _bp);
    }

    // ============================================================
    //  아웃 판정
    // ============================================================

    void CheckOutsWhileHoldingBall()
    {
        if (BasePositionProvider.provider == null) return;

        // 1) 베이스 포스 아웃
        BASE_TYPE near = GetClosestBase();
        if (near != BASE_TYPE.E_SELF)
        {
            Vector3 basePos = FieldMetrics.BasePos(near);
            if (FieldMetrics.FlatDistance(_my.position, basePos) < FieldMetrics.M(DefenseTuning.BASE_TOUCH_RANGE))
            {
                CheckBaseForceOut(near);
                if (!_hasBall) return;
            }
        }

        // 2) 태그 아웃
        List<RunnerInfo> runners = RunnerTracker.Snapshot(true);
        for (int i = 0; i < runners.Count; i++)
        {
            if (RunnerTracker.IsTaggable(runners[i], _my.position))
            {
                CheckTagOut(runners[i].Player);
                if (!_hasBall) return;
            }
        }
    }

    BASE_TYPE GetClosestBase()
    {
        BasePositionProvider bp = BasePositionProvider.provider;
        if (bp == null || bp.Bases == null) return BASE_TYPE.E_SELF;

        BASE_TYPE best = BASE_TYPE.E_SELF;
        float bestD = FieldMetrics.M(2.2f);
        for (int i = 0; i < bp.Bases.Count; i++)
        {
            if (bp.Bases[i] == null) continue;
            float d = FieldMetrics.FlatDistance(_my.position, bp.Bases[i].position);
            if (d < bestD) { bestD = d; best = (BASE_TYPE)(i + (int)BASE_TYPE.E_FIRST_BASE); }
        }
        return best;
    }

    /// <summary>
    /// 공을 쥔 채 베이스를 밟았을 때의 포스 아웃 판정.
    ///
    /// 설계도 10.3 「포스 상태 해제 → 태그 전환」:
    /// 후행 주자가 먼저 아웃되어 포스가 풀린 주자는 베이스를 밟는 것만으로는 아웃이 아니다.
    /// 이 경우 즉시 태그 플레이로 전환한다.
    /// </summary>
    public void CheckBaseForceOut(BASE_TYPE hitBase)
    {
        if (!_hasBall || hitBase == BASE_TYPE.E_SELF) return;
        if (CountsProvider.provider == null) return;

        RunnerTracker.Snapshot(true);
        RunnerInfo runner = RunnerTracker.RunnerHeadingTo(hitBase);
        if (!runner.IsValid) return;
        if (!runner.Role.IsOnBaseOrRunning) return;
        if (runner.IsSafe) return;

        if (!runner.IsForced)
        {
            // ★ 포스가 해제된 주자 — 발로 베이스를 밟아봐야 세이프. 직접 태그해야 한다.
            _plan.Kind = THROW_KIND.TAG_RUNNER;
            _plan.TargetRunner = runner;
            _plan.TargetBase = hitBase;
            _rundownThrows = 0;
            SetState(FIELDER_STATE.TAG);
            return;
        }

        // 공 + 베이스를 동시에 확보한 시각을 기록한다(설계도 10.4 정밀 판정의 기준 시각).
        if (_controlBase != hitBase)
        {
            _controlBase = hitBase;
            _baseControlTime = Time.time;
        }
        float controlTime = Mathf.Max(_ballSecuredTime, _baseControlTime);

        // ★ 설계도 10.4-B : 콜라이더 교차 + 확보/도달 타임스탬프 비교로 정밀 판정
        if (!OutJudge.EvaluateForceOut(_bp, controlTime, runner, hitBase)) return;

        Debug.Log($"[ForceOut] 수비수({_type})가 {hitBase} 베이스에서 포스 아웃 성공! (Time: {Time.time:F3}, 수비수 컨트롤: {controlTime:F3}, 주자 터치: {runner.Role.BaseTouchTimestamp:F3})");


        Attacker att = runner.Role;
        CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, delegate () { att.OnOut(); });
        BasePositionProvider.provider.SetAttackerBaseState(hitBase, -1);

        // 병살 연계 : 방금 아웃된 주자를 목록에서 지우고 다음 송구를 다시 계산
        RunnerTracker.Snapshot(true);

        // ── 설계도 10.1-4 : 2루 병살 피벗 ──
        // 베이스 위에 머물지 않고, 슬라이딩해 들어오는 주자를 피해 바깥으로 빠지며 송구한다.
        _turningTwo = false;
        if (hitBase != BASE_TYPE.E_FIRST_BASE && RunnerTracker.RunnerHeadingTo(BASE_TYPE.E_FIRST_BASE).IsValid)
        {
            _turningTwo = true;
            Vector3 firstBase = FieldMetrics.BasePos(BASE_TYPE.E_FIRST_BASE);
            Vector3 toFirst = firstBase - FieldMetrics.BasePos(hitBase);
            toFirst.y = 0f;
            // 주자 진행선(하위 베이스 → 이 베이스)의 바깥쪽으로 빠진다
            Vector3 escape = Vector3.Cross(Vector3.up, toFirst.sqrMagnitude > 0.01f ? toFirst.normalized : FieldMetrics.Forward);
            if (Vector3.Dot(escape, toFirst) < 0f) escape = -escape;
            _motion.PlayPivot((escape + toFirst.normalized * 0.6f).normalized);
        }

        ReplanThrow(ThrowDecision.PIVOT_TIME * 0.5f);
    }

    /// <summary>베이스에서 떨어진 주자를 직접 태그했을 때의 판정.</summary>
    public void CheckTagOut(BaseballPlayer runnerPlayer)
    {
        if (!_hasBall || runnerPlayer == null || !runnerPlayer.gameObject.activeSelf) return;
        if (CountsProvider.provider == null) return;

        Attacker att = runnerPlayer.GetRole() as Attacker;
        if (att == null) return;

        RunnerInfo target = new RunnerInfo();
        List<RunnerInfo> list = RunnerTracker.Snapshot(true);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Player == runnerPlayer) { target = list[i]; break; }
        }
        if (!target.IsValid) return;

        // ★ 설계도 10.4-C : 미트 트리거 ↔ 주자 히트박스 오버랩 + 주자의 베이스 미접촉
        if (!OutJudge.EvaluateTagOut(_hasBall, _motion.GloveWorldPosition, target)) return;

        CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, delegate () { att.OnOut(); });
        RunnerTracker.Snapshot(true);
        ReplanThrow(0.08f);
    }

    // ============================================================
    //  외부 알림
    // ============================================================

    /// <summary>다른 야수가 공을 잡았다 — 추격을 멈춘다.</summary>
    public void OnOtherFielderCaughtBall()
    {
        if (_hasBall) return;
        _motion.ClearGloveTarget();
        if (_state == FIELDER_STATE.PURSUE || _state == FIELDER_STATE.FIELD || _state == FIELDER_STATE.READ)
        {
            SetState(FIELDER_STATE.COVER);
        }
    }

    /// <summary>내가 수구 대상인 송구가 출발했다.</summary>
    public void OnIncomingThrow(Vector3 aim)
    {
        if (_hasBall) return;
        if (_selfThrowLockout > 0f) return;
        if (FieldMetrics.FlatDistance(_my.position, aim) < FieldMetrics.M(4f))
        {
            SetState(FIELDER_STATE.RECEIVE);
        }
    }

    public override void OnTriggerEnter(Collider collider)
    {
        if (collider == null) return;

        if (collider.transform.CompareTag("Base") || collider.transform.name.ToLower().Contains("base"))
        {
            BASE_TYPE hitBase = GetClosestBase();
            if (hitBase != BASE_TYPE.E_SELF && BasePositionProvider.provider != null)
            {
                BasePositionProvider.provider.SetDefencerBaseState(hitBase, DefencerCode(_type));
                if (_hasBall) CheckBaseForceOut(hitBase);
            }
        }

        if (_hasBall)
        {
            BaseballPlayer touched = collider.GetComponentInParent<BaseballPlayer>();
            if (touched != null && touched.GetRole() is Attacker) CheckTagOut(touched);
        }
    }

    public override string ToString()
    {
        return "Def";
    }
}
