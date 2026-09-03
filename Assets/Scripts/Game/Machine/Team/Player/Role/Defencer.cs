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
    TAG,         // 태그 시도
    RETURN       // 원위치 복귀
}

/// <summary>
/// 수비수 AI — 전면 재작성.
///
/// 예전 구현의 문제
///   · 1,180줄 한 클래스에 이동 · 포구 · 송구 · 아웃 판정 · 모션이 뒤섞여 있었다.
///   · 도달 가능성을 세 곳에서 서로 다른 하드코딩 속도로 따로 계산했다.
///   · 땅볼 궤적은 첫 바운드에서 끊겨서 "공 뒤꽁무니 쫓기"가 발생했다.
///   · 송구는 주자 도착 시간을 전혀 보지 않고 거리 점수만으로 골랐다.
///   · 포구 직후 GrabBall 이 _isBattedBall 을 지워버려서 <b>플라이 아웃이 아예 발동하지 않았다.</b>
///
/// 새 구조
///   BallTrajectory     : 바운드·구름까지 포함한 궤적 예측 (실제 물리와 동일 공식)
///   InterceptSolver    : 반응 · 가속 · 후진 페널티 · 글러브 리치를 반영한 도달 판정
///   FieldingCoordinator: 콜오프 우선순위 기반 역할 배정(1차/트레일/중계/커버/백업)
///   ThrowDecision      : 송구 도착 시간 vs 주자 ETA 비교로 아웃 가능한 최선의 플레이
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

    // ── 외부 조회용 ───────────────────────────────────────
    public FIELDER_STATE State { get { return _state; } }
    public bool HasBall { get { return _hasBall; } }
    public BATTED_BALL_TYPE CurrentBallType { get { return _ballType; } }
    public FielderAttributes Attributes { get { return _attr; } }
    public bool IsRunning { get { return _velocity.sqrMagnitude > 0.6f; } }
    public DefenseZone Zone { get { return _defenseZone; } }
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
        if (_controller != null && _item != null) _controller.SetItemPoint(_item);
        if (_my != null) _baseY = _my.position.y;
    }

    // ============================================================
    //  타구 이벤트 (Ball → 모든 수비수)
    // ============================================================

    public override void BallEventListener(Vector3 playerPos, Vector3 startPos, Vector3 endPos, Vector3 inc)
    {
        if (_ball == null) return;
        if (_ball.DontNeedToMove()) return;          // 투구 · 송구는 타구가 아니다

        BallTrajectory traj = _ball.Trajectory;
        if (traj == null || !traj.IsValid) return;
        if (traj.Version == _lastTrajVersion) return;
        _lastTrajVersion = traj.Version;

        _ballType = BattedBallClassifier.ClassifyFromTrajectory(traj);

        // 팀 조율자에게 알림 (팀 쪽에서 Version 으로 중복 호출을 막는다)
        if (_bp != null && _bp.MyTeam != null)
        {
            _bp.MyTeam.OnBattedBall(traj, _ballType);
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

        FieldMetrics.Refresh();

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
        if (_ball.isCatchable() && ballDist < FieldMetrics.M(DefenseTuning.FIELD_ACTION_RANGE))
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
        if (!_crowHopped && _attr.IsOutfielder && _plan.Kind == THROW_KIND.TO_BASE)
        {
            _crowHopped = true;
            Vector3 d = _plan.AimPoint - _my.position;
            d.y = 0f;
            _motion.PlayCrowHop(d);
        }

        if (_plan.Kind == THROW_KIND.TO_BASE || _plan.Kind == THROW_KIND.RELAY)
        {
            FaceToward(_plan.AimPoint, dt, 16f);
        }

        if (_secureTimer > 0f || _motion.IsLocked) return;

        switch (_plan.Kind)
        {
            case THROW_KIND.TO_BASE:
            case THROW_KIND.RELAY:
            case THROW_KIND.TO_PITCHER:
                BeginThrow();
                break;

            case THROW_KIND.RUN_TO_BASE:
                SetState(FIELDER_STATE.COVER);
                break;

            case THROW_KIND.TAG_RUNNER:
                SetState(FIELDER_STATE.TAG);
                break;

            default:
                // 던질 곳이 없음 — 플레이 종료
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
                FinishPlayWithBall();
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

        float flight = _attr.ThrowFlightTime(dist);
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

        Transform origin = _throwHand != null ? _throwHand : _item;
        _ball.ThrowByFielder(origin.position, aim, flightTime);

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
        bool canReceive = a.Role == DEFENSIVE_ROLE.BASE_COVER || a.Role == DEFENSIVE_ROLE.CUTOFF;
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
            // 베이스에 발을 붙인 채 상체만 뻗는다
            Vector3 basePos = FieldMetrics.BasePos(a.CoverBase);
            basePos.y = _baseY;
            if (FieldMetrics.FlatDistance(_my.position, basePos) > FieldMetrics.M(0.5f))
            {
                Steer(basePos, dt, 1f);
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

    void TickTag(float dt)
    {
        RunnerInfo r = _plan.TargetRunner;
        if (!r.IsValid || !r.Player.gameObject.activeSelf || !_hasBall)
        {
            SetState(FIELDER_STATE.COVER);
            return;
        }

        Vector3 rp = r.Player.transform.position;
        Steer(rp, dt, 1f);
        FaceToward(rp, dt, 18f);

        if (FieldMetrics.FlatDistance(_my.position, rp) < FieldMetrics.M(DefenseTuning.TAG_RANGE))
        {
            if (_motion.CurrentAction != FIELDER_ACTION.SWEEP_TAG) _motion.PlaySweepTag(rp);
            CheckTagOut(r.Player);
            if (_hasBall) ReplanThrow(0.1f);
        }

        if (_stateTime > 2.5f && _hasBall) ReplanThrow(0f);
    }

    void TickReturn(float dt)
    {
        Vector3 home = _bp != null ? _bp.HomePosition : _my.position;
        home.y = _baseY;

        if (FieldMetrics.FlatDistance(_my.position, home) > FieldMetrics.M(DefenseTuning.ARRIVE_DISTANCE))
        {
            Steer(home, dt, 0.85f);
        }
        else
        {
            Decelerate(dt);
            FaceToward(FieldMetrics.Home, dt);
            SetState(FIELDER_STATE.PRE_PITCH);
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

        // 투구(아직 배트에 맞지 않은 공)나 마운드에 놓인 공에는 반응하지 않는다.
        // 이 가드가 없으면 투수가 대기 중인 공을 집어들어 플레이가 무한히 종료된다.
        if (!_ball._isBattedBall && !_ball.IsFielderThrow) return false;

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
        //    기존 구현은 GrabBall 이후에 검사해서 플라이 아웃이 아예 발동하지 않았다.
        bool wasBattedBall = _ball._isBattedBall;
        bool directOut = _ball.CanBeDirectOut();

        _motion.ClearGloveTarget();
        _motion.StopAction();

        if (_controller != null && _item != null) _controller.SetItemPoint(_item);

        _item.position = _ball.GetPosition();
        _ball.GrabBall(_item);
        _hasBall = true;
        _velocity = Vector3.zero;

        if (_bp != null && _bp.MyTeam != null) _bp.MyTeam.NotifyBallCaught(_bp);

        // ── 노바운드 포구 = 플라이 아웃 ──
        if (wasBattedBall && directOut)
        {
            _ball.SetBattedBall(false);
            HandleFlyOut();
            return;
        }

        _ball.SetBattedBall(false);
        ReplanThrow(_attr.TransferTime);
    }

    void HandleFlyOut()
    {
        if (CountsProvider.provider == null) return;

        CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, delegate ()
        {
            PLAYER_TYPE batterType = PLAYER_TYPE.E_PITCHER;
            GamePlayerProvider gp = GamePlayerProvider.provider;
            if (gp != null && gp.AttackTeam != null)
            {
                BaseballPlayer batter = gp.AttackTeam.GetCurrentBatter();
                if (batter != null) batterType = batter.Type;
            }
            if (gp != null) gp.PlayerOut(batterType);

            _hasBall = false;
            if (_bp != null && _bp.MyTeam != null)
            {
                _bp.MyTeam.EndPlay();
                Transform pitcherHand = _bp.MyTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
                if (pitcherHand != null && _ball != null) _ball.GrabBall(pitcherHand);
            }
            if (gp != null)
            {
                if (gp.AttackTeam != null) gp.AttackTeam.ChangeAttacker();
                gp.TriggerAReset();
            }
        });

        SetState(FIELDER_STATE.RETURN);
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

    void FinishPlayWithBall()
    {
        _hasBall = false;

        Team myTeam = _bp != null ? _bp.MyTeam : null;
        if (myTeam != null)
        {
            myTeam.EndPlay();
            if (_type != PLAYER_TYPE.E_PITCHER)
            {
                Transform pitcherHand = myTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
                if (pitcherHand != null && _ball != null) _ball.GrabBall(pitcherHand);
            }
        }

        GamePlayerProvider gp = GamePlayerProvider.provider;
        if (gp != null)
        {
            if (_type == PLAYER_TYPE.E_PITCHER)
            {
                if (gp.AttackTeam != null) gp.AttackTeam.ChangeAttacker();
                gp.TriggerAReset();
            }
        }

        SetState(FIELDER_STATE.RETURN);
    }

    BaseballPlayer FindRelayMan(Vector3 aim)
    {
        if (_bp == null || _bp.MyTeam == null) return null;

        Vector3 line = aim - _my.position;
        line.y = 0f;
        float total = line.magnitude;
        if (total < 1f) return null;
        line /= total;

        BaseballPlayer best = null;
        float bestScore = float.MaxValue;

        for (int i = 0; i < _bp.MyTeam.PlayerCount; i++)
        {
            BaseballPlayer p = _bp.MyTeam.GetPlayerAt(i);
            if (p == null || p == _bp || !p.gameObject.activeSelf) continue;
            if (!(p.GetRole() is Defencer)) continue;

            Vector3 rel = p.transform.position - _my.position;
            rel.y = 0f;
            float along = Vector3.Dot(rel, line);
            if (along < total * 0.15f || along > total * 0.85f) continue;

            float off = Vector3.Cross(line, rel).magnitude;
            float score = off + Mathf.Abs(along - total * 0.45f) * 0.4f;
            if (score < bestScore) { bestScore = score; best = p; }
        }

        return best;
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

    /// <summary>공을 쥔 채 베이스를 밟았을 때의 포스 아웃 판정.</summary>
    public void CheckBaseForceOut(BASE_TYPE hitBase)
    {
        if (!_hasBall || hitBase == BASE_TYPE.E_SELF) return;
        if (CountsProvider.provider == null) return;

        RunnerTracker.Snapshot(true);
        RunnerInfo runner = RunnerTracker.RunnerHeadingTo(hitBase);
        if (!runner.IsValid) return;
        if (!runner.Role.IsOnBaseOrRunning) return;
        if (!runner.IsForced) return;
        if (runner.IsSafe) return;

        Vector3 basePos = FieldMetrics.BasePos(hitBase);
        float runnerDist = FieldMetrics.FlatDistance(runner.Position, basePos);
        if (runnerDist <= FieldMetrics.M(0.45f)) return;    // 주자가 먼저 도착 — 세이프

        Attacker att = runner.Role;
        CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, delegate () { att.OnOut(); });
        BasePositionProvider.provider.SetAttackerBaseState(hitBase, -1);

        // 병살 연계 : 방금 아웃된 주자를 목록에서 지우고 다음 송구를 다시 계산
        RunnerTracker.Snapshot(true);
        ReplanThrow(ThrowDecision.PIVOT_TIME * 0.5f);
    }

    /// <summary>베이스에서 떨어진 주자를 직접 태그했을 때의 판정.</summary>
    public void CheckTagOut(BaseballPlayer runnerPlayer)
    {
        if (!_hasBall || runnerPlayer == null || !runnerPlayer.gameObject.activeSelf) return;
        if (CountsProvider.provider == null) return;

        Attacker att = runnerPlayer.GetRole() as Attacker;
        if (att == null) return;
        if (!att.IsOnBaseOrRunning || att.IsSafeOnBase()) return;

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
