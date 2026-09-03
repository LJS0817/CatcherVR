using UnityEngine;

public enum FIELDER_ACTION
{
    NONE,
    READY,          // 투구 직전 준비 자세 (스플릿 스텝)
    FIELD_GROUNDER, // 땅볼 포구 자세 (무릎 굽히고 글러브 지면)
    DIVE,           // 다이빙
    GETUP,          // 다이빙 후 일어나기
    JUMP,           // 점프 캐치
    SLIDE,          // 슬라이딩 캐치
    CROW_HOP,       // 외야 크로우 홉
    THROW,          // 송구 동작
    STRETCH,        // 1루수 스트레치
    SWEEP_TAG,      // 스윕 태그
    PIVOT,          // 2루 병살 피벗 (베이스를 스치며 슬라이딩 주자를 피해 점프/스텝스루)
    PUMP_FAKE       // 런다운 펌프 페이크 (주자 감속 유도)
}

/// <summary>
/// 애니메이션 클립 없이 <b>스크립트만으로</b> 야수의 몸동작을 만들어내는 절차적 모션 드라이버.
///
/// 리그 구조(기존 프로젝트 그대로):
///   root(BaseballPlayer) → body(0) → shoulder(1) → gloveHand(0) / throwHand(1) / offsets(2)
///   gloveHand → glove(0)
///
/// 레이어 구성
///   1) 로코모션 : 달리기 상하동 · 가속 기울기 · 회전 롤 · 팔 스윙
///   2) 액션     : 다이빙/점프/슬라이딩/크로우홉/송구/스트레치/태그
///   3) 글러브   : 공을 향해 팔을 뻗는 리치(위 두 레이어 위에 덮어씀)
///
/// 모든 값을 이 클래스 한 곳에서만 써서 다른 코드와 충돌하지 않게 한다.
/// </summary>
public class FielderMotion
{
    // ── 리그 ─────────────────────────────────────────────
    readonly Transform _root;
    readonly Transform _body;
    readonly Transform _shoulder;
    readonly Transform _gloveHand;
    readonly Transform _throwHand;
    readonly Transform _glove;
    readonly FielderAttributes _attr;

    // ── 기준(휴식) 포즈 ──────────────────────────────────
    Vector3 _bodyRestPos;
    Quaternion _bodyRestRot;
    Vector3 _shoulderRestPos;
    Quaternion _shoulderRestRot;
    Vector3 _gloveRestPos;
    Quaternion _gloveRestRot;
    Vector3 _throwRestPos;
    Quaternion _throwRestRot;
    float _armLength = 1f;

    // ── 로코모션 상태 ────────────────────────────────────
    float _strideCycle;
    float _speed;
    Vector3 _prevVelocity;
    float _leanPitch;
    float _leanRoll;

    // ── 액션 상태 ────────────────────────────────────────
    FIELDER_ACTION _action = FIELDER_ACTION.NONE;
    float _actionTime;
    float _actionDuration;
    Vector3 _actionDir = Vector3.forward;
    Vector3 _actionStartPos;
    Quaternion _actionStartRot;
    float _actionDistance;
    float _actionHeight;

    // ── 송구 상태 ────────────────────────────────────────
    public delegate void ReleaseCallback();
    ReleaseCallback _onRelease;
    bool _released;
    float _releaseNormalized = 0.72f;
    Vector3 _throwAim;
    bool _sidearm;

    // ── 글러브 리치 ──────────────────────────────────────
    bool _gloveReaching;
    Vector3 _gloveTarget;
    float _gloveBlend;

    public FIELDER_ACTION CurrentAction { get { return _action; } }

    /// <summary>이동을 막는 동작이 진행 중인가(다이빙/점프/슬라이딩/송구).</summary>
    public bool IsLocked
    {
        get
        {
            return _action == FIELDER_ACTION.DIVE ||
                   _action == FIELDER_ACTION.GETUP ||
                   _action == FIELDER_ACTION.JUMP ||
                   _action == FIELDER_ACTION.SLIDE ||
                   _action == FIELDER_ACTION.CROW_HOP ||
                   _action == FIELDER_ACTION.PIVOT ||
                   _action == FIELDER_ACTION.THROW;
        }
    }

    /// <summary>다이빙/슬라이딩 중이라 포구 판정 범위가 넓어진 상태인가.</summary>
    public bool IsExtended
    {
        get
        {
            return _action == FIELDER_ACTION.DIVE ||
                   _action == FIELDER_ACTION.SLIDE ||
                   _action == FIELDER_ACTION.JUMP ||
                   _action == FIELDER_ACTION.STRETCH;
        }
    }

    public bool IsThrowing { get { return _action == FIELDER_ACTION.THROW; } }

    public float ActionProgress
    {
        get { return _actionDuration > 0.0001f ? Mathf.Clamp01(_actionTime / _actionDuration) : 1f; }
    }

    public FielderMotion(Transform root, Transform body, Transform shoulder,
                         Transform gloveHand, Transform throwHand, Transform glove,
                         FielderAttributes attr)
    {
        _root = root;
        _body = body;
        _shoulder = shoulder;
        _gloveHand = gloveHand;
        _throwHand = throwHand;
        _glove = glove;
        _attr = attr;
        CaptureRestPose();
    }

    public void CaptureRestPose()
    {
        if (_body != null) { _bodyRestPos = _body.localPosition; _bodyRestRot = _body.localRotation; }
        if (_shoulder != null) { _shoulderRestPos = _shoulder.localPosition; _shoulderRestRot = _shoulder.localRotation; }
        if (_gloveHand != null) { _gloveRestPos = _gloveHand.localPosition; _gloveRestRot = _gloveHand.localRotation; }
        if (_throwHand != null) { _throwRestPos = _throwHand.localPosition; _throwRestRot = _throwHand.localRotation; }

        if (_shoulder != null && _gloveHand != null)
        {
            float d = Vector3.Distance(_shoulder.position, _gloveHand.position);
            _armLength = d > 0.05f ? d : 1f;
        }
    }

    /// <summary>모든 상태를 초기화하고 기준 포즈로 되돌린다.</summary>
    public void ResetPose()
    {
        _action = FIELDER_ACTION.NONE;
        _actionTime = 0f;
        _onRelease = null;
        _released = true;
        _gloveReaching = false;
        _gloveBlend = 0f;
        _strideCycle = 0f;
        _speed = 0f;
        _leanPitch = 0f;
        _leanRoll = 0f;
        _prevVelocity = Vector3.zero;

        if (_body != null) { _body.localPosition = _bodyRestPos; _body.localRotation = _bodyRestRot; }
        if (_shoulder != null) { _shoulder.localPosition = _shoulderRestPos; _shoulder.localRotation = _shoulderRestRot; }
        if (_gloveHand != null) { _gloveHand.localPosition = _gloveRestPos; _gloveHand.localRotation = _gloveRestRot; }
        if (_throwHand != null) { _throwHand.localPosition = _throwRestPos; _throwHand.localRotation = _throwRestRot; }
    }

    // ============================================================
    //  액션 시작
    // ============================================================

    public void PlayReady()
    {
        if (IsLocked) return;
        Begin(FIELDER_ACTION.READY, 0.32f);
    }

    public void PlayFieldGrounder()
    {
        if (IsLocked) return;
        if (_action == FIELDER_ACTION.FIELD_GROUNDER) return;
        Begin(FIELDER_ACTION.FIELD_GROUNDER, 0.28f);
    }

    /// <summary>다이빙. dir 은 수평 방향, distance 는 몸이 날아갈 거리.</summary>
    public void PlayDive(Vector3 dir, float distance)
    {
        if (IsLocked) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = _root.forward;
        _actionDir = dir.normalized;
        _actionDistance = distance;
        _actionHeight = FieldMetrics.M(0.42f);
        _actionStartPos = _root.position;
        _actionStartRot = _root.rotation;
        Begin(FIELDER_ACTION.DIVE, DefenseTuning.DIVE_DURATION);
    }

    public void PlayJump(Vector3 dir, float height)
    {
        if (IsLocked) return;
        dir.y = 0f;
        _actionDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _root.forward;
        _actionDistance = FieldMetrics.M(0.5f);
        _actionHeight = height;
        _actionStartPos = _root.position;
        _actionStartRot = _root.rotation;
        Begin(FIELDER_ACTION.JUMP, DefenseTuning.JUMP_DURATION);
    }

    public void PlaySlide(Vector3 dir, float distance)
    {
        if (IsLocked) return;
        dir.y = 0f;
        _actionDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : _root.forward;
        _actionDistance = distance;
        _actionStartPos = _root.position;
        _actionStartRot = _root.rotation;
        Begin(FIELDER_ACTION.SLIDE, 0.70f);
    }

    /// <summary>외야수의 크로우 홉(송구 전 도움닫기).</summary>
    public void PlayCrowHop(Vector3 throwDir)
    {
        if (IsLocked) return;
        throwDir.y = 0f;
        _actionDir = throwDir.sqrMagnitude > 0.0001f ? throwDir.normalized : _root.forward;
        _actionDistance = FieldMetrics.M(1.1f);
        _actionHeight = FieldMetrics.M(0.18f);
        _actionStartPos = _root.position;
        _actionStartRot = _root.rotation;
        Begin(FIELDER_ACTION.CROW_HOP, DefenseTuning.CROW_HOP_DURATION);
    }

    /// <summary>송구 동작 시작. duration 은 포지션별 릴리스 속도.</summary>
    public void PlayThrow(Vector3 aimPoint, float duration, bool sidearm, ReleaseCallback onRelease)
    {
        _throwAim = aimPoint;
        _sidearm = sidearm;
        _onRelease = onRelease;
        _released = false;
        _releaseNormalized = 0.70f;
        _actionStartRot = _root.rotation;
        Begin(FIELDER_ACTION.THROW, Mathf.Max(0.2f, duration));
    }

    /// <summary>1루수 스트레치(송구가 오는 방향으로 몸을 뻗는다).</summary>
    public void PlayStretch(Vector3 fromPoint)
    {
        if (_action == FIELDER_ACTION.THROW || _action == FIELDER_ACTION.DIVE) return;
        Vector3 d = fromPoint - _root.position;
        d.y = 0f;
        _actionDir = d.sqrMagnitude > 0.0001f ? d.normalized : _root.forward;
        _actionStartPos = _root.position;
        Begin(FIELDER_ACTION.STRETCH, 0.5f);
    }

    public void PlaySweepTag(Vector3 runnerPos)
    {
        if (IsLocked) return;
        Vector3 d = runnerPos - _root.position;
        d.y = 0f;
        _actionDir = d.sqrMagnitude > 0.0001f ? d.normalized : _root.forward;
        Begin(FIELDER_ACTION.SWEEP_TAG, 0.45f);
    }

    /// <summary>
    /// 2루 병살 피벗 (설계도 10.1-4).
    /// 베이스 위에 머물지 않고 발끝으로 스치듯 찍은 뒤,
    /// 슬라이딩해 들어오는 주자를 피해 베이스 바깥으로 점프/스텝스루하며 송구 자세를 잡는다.
    /// </summary>
    /// <param name="escapeDir">주자를 피해 빠져나갈 방향(보통 송구 방향의 옆)</param>
    public void PlayPivot(Vector3 escapeDir)
    {
        if (_action == FIELDER_ACTION.THROW) return;
        escapeDir.y = 0f;
        _actionDir = escapeDir.sqrMagnitude > 0.0001f ? escapeDir.normalized : _root.forward;
        _actionDistance = FieldMetrics.M(0.9f);
        _actionHeight = FieldMetrics.M(0.22f);
        _actionStartPos = _root.position;
        _actionStartRot = _root.rotation;
        Begin(FIELDER_ACTION.PIVOT, DefenseTuning.PIVOT_STEP_DURATION);
    }

    /// <summary>
    /// 런다운 펌프 페이크 (설계도 9장 원칙 ③).
    /// 던지는 시늉으로 주자의 감속·방향 전환을 유도한다. 루트 이동은 막지 않는다.
    /// </summary>
    public void PlayPumpFake(Vector3 towardPoint)
    {
        if (IsLocked) return;
        if (_action == FIELDER_ACTION.PUMP_FAKE) return;
        Vector3 d = towardPoint - _root.position;
        d.y = 0f;
        _actionDir = d.sqrMagnitude > 0.0001f ? d.normalized : _root.forward;
        Begin(FIELDER_ACTION.PUMP_FAKE, DefenseTuning.PUMP_FAKE_DURATION);
    }

    public void StopAction()
    {
        if (_action == FIELDER_ACTION.THROW && !_released && _onRelease != null)
        {
            // 던지는 도중 강제 중단 — 공이 손에 남지 않도록 릴리스는 취소만 한다.
            _onRelease = null;
        }
        _action = FIELDER_ACTION.NONE;
        _actionTime = 0f;
    }

    void Begin(FIELDER_ACTION a, float duration)
    {
        _action = a;
        _actionTime = 0f;
        _actionDuration = duration;
    }

    // ============================================================
    //  글러브 리치
    // ============================================================

    /// <summary>공 쪽으로 글러브를 뻗는다. 매 프레임 호출.</summary>
    public void SetGloveTarget(Vector3 worldPos)
    {
        _gloveReaching = true;
        _gloveTarget = worldPos;
    }

    public void ClearGloveTarget()
    {
        _gloveReaching = false;
    }

    /// <summary>현재 글러브(포구 판정점)의 월드 위치.</summary>
    public Vector3 GloveWorldPosition
    {
        get { return _glove != null ? _glove.position : (_gloveHand != null ? _gloveHand.position : _root.position); }
    }

    // ============================================================
    //  매 프레임 갱신
    // ============================================================

    /// <summary>
    /// 몸 포즈를 갱신한다. 루트 이동/회전은 호출자(Defencer)가 담당하고,
    /// 여기서는 그 위에 얹히는 상체 표현과 액션 중의 루트 오프셋만 처리한다.
    /// </summary>
    /// <param name="dt">델타 타임</param>
    /// <param name="velocity">현재 이동 속도 벡터(월드)</param>
    public void Tick(float dt, Vector3 velocity)
    {
        if (_root == null) return;
        if (dt <= 0f) dt = Time.deltaTime;

        // ── 기준 포즈로 리셋 후 레이어를 얹는다 ──
        Vector3 bodyPos = _bodyRestPos;
        Quaternion bodyRot = _bodyRestRot;
        Vector3 shoulderRot = Vector3.zero;
        Vector3 glovePos = _gloveRestPos;
        Quaternion gloveRot = _gloveRestRot;
        Vector3 throwPos = _throwRestPos;
        Quaternion throwRot = _throwRestRot;

        // ────────── 레이어 1 : 로코모션 ──────────
        velocity.y = 0f;
        float speed = velocity.magnitude;
        _speed = Mathf.Lerp(_speed, speed, 1f - Mathf.Exp(-12f * dt));

        float topSpeed = Mathf.Max(0.1f, _attr != null ? _attr.TopSpeed : 5f);
        float norm = Mathf.Clamp01(_speed / topSpeed);

        if (_speed > 0.15f)
        {
            _strideCycle += dt * DefenseTuning.RUN_BOB_FREQUENCY * Mathf.PI * 2f * (0.6f + norm * 1.1f);
            if (_strideCycle > Mathf.PI * 4f) _strideCycle -= Mathf.PI * 4f;
        }
        else
        {
            _strideCycle = Mathf.Lerp(_strideCycle, 0f, 1f - Mathf.Exp(-8f * dt));
        }

        // 가속도 → 상체 앞뒤 기울기
        Vector3 accel = (velocity - _prevVelocity) / Mathf.Max(0.0001f, dt);
        _prevVelocity = velocity;
        float forwardAccel = Vector3.Dot(accel, _root.forward);
        float sideAccel = Vector3.Dot(accel, _root.right);

        float targetPitch = Mathf.Clamp(norm * DefenseTuning.RUN_LEAN_MAX * 0.55f +
                                        forwardAccel * 0.55f, -12f, DefenseTuning.RUN_LEAN_MAX);
        float targetRoll = Mathf.Clamp(-sideAccel * 0.42f, -DefenseTuning.RUN_ROLL_MAX, DefenseTuning.RUN_ROLL_MAX);

        _leanPitch = Mathf.Lerp(_leanPitch, targetPitch, 1f - Mathf.Exp(-9f * dt));
        _leanRoll = Mathf.Lerp(_leanRoll, targetRoll, 1f - Mathf.Exp(-9f * dt));

        float bob = Mathf.Sin(_strideCycle) * DefenseTuning.RUN_BOB_HEIGHT * norm;
        bodyPos += new Vector3(0f, bob, 0f);
        bodyRot = _bodyRestRot * Quaternion.Euler(_leanPitch, 0f, _leanRoll);

        // 팔 스윙 (좌우 반대 위상)
        float swing = Mathf.Sin(_strideCycle) * 42f * norm;
        float armLift = norm * 0.22f;
        glovePos += new Vector3(0f, -armLift * 0.3f, armLift * 0.25f);
        gloveRot = _gloveRestRot * Quaternion.Euler(swing, 0f, 0f);
        throwPos += new Vector3(0f, -armLift * 0.3f, armLift * 0.25f);
        throwRot = _throwRestRot * Quaternion.Euler(-swing, 0f, 0f);

        shoulderRot.y = -swing * 0.18f;

        // ────────── 레이어 2 : 액션 ──────────
        if (_action != FIELDER_ACTION.NONE)
        {
            _actionTime += dt;
            float p = ActionProgress;

            switch (_action)
            {
                case FIELDER_ACTION.READY:
                    ApplyReady(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.FIELD_GROUNDER:
                    ApplyFieldGrounder(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot, ref throwPos);
                    // 유지형 동작 — 자동 종료하지 않음(호출자가 StopAction)
                    if (p >= 1f) _actionTime = _actionDuration;
                    break;

                case FIELDER_ACTION.DIVE:
                    ApplyDive(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot, ref throwPos, ref throwRot);
                    if (p >= 1f)
                    {
                        _actionStartPos = _root.position;
                        Begin(FIELDER_ACTION.GETUP, DefenseTuning.DIVE_GETUP_DURATION);
                    }
                    break;

                case FIELDER_ACTION.GETUP:
                    ApplyGetUp(p, ref bodyPos, ref bodyRot, ref glovePos, ref throwPos);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.JUMP:
                    ApplyJump(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.SLIDE:
                    ApplySlide(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.CROW_HOP:
                    ApplyCrowHop(p, ref bodyPos, ref bodyRot, ref glovePos, ref throwPos);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.THROW:
                    ApplyThrow(p, ref bodyPos, ref bodyRot, ref shoulderRot,
                               ref glovePos, ref gloveRot, ref throwPos, ref throwRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.STRETCH:
                    ApplyStretch(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot, ref throwPos);
                    if (p >= 1f) _actionTime = _actionDuration; // 유지
                    break;

                case FIELDER_ACTION.SWEEP_TAG:
                    ApplySweepTag(p, ref bodyPos, ref bodyRot, ref glovePos, ref gloveRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.PIVOT:
                    ApplyPivot(p, ref bodyPos, ref bodyRot, ref glovePos, ref throwPos, ref throwRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;

                case FIELDER_ACTION.PUMP_FAKE:
                    ApplyPumpFake(p, ref bodyPos, ref bodyRot, ref shoulderRot, ref throwPos, ref throwRot);
                    if (p >= 1f) _action = FIELDER_ACTION.NONE;
                    break;
            }
        }

        // ────────── 적용 ──────────
        if (_body != null) { _body.localPosition = bodyPos; _body.localRotation = bodyRot; }
        if (_shoulder != null) _shoulder.localRotation = _shoulderRestRot * Quaternion.Euler(shoulderRot);
        if (_gloveHand != null) { _gloveHand.localPosition = glovePos; _gloveHand.localRotation = gloveRot; }
        if (_throwHand != null) { _throwHand.localPosition = throwPos; _throwHand.localRotation = throwRot; }

        // ────────── 레이어 3 : 글러브 리치 (월드 공간 덮어쓰기) ──────────
        float blendTarget = (_gloveReaching && _action != FIELDER_ACTION.THROW) ? 1f : 0f;
        _gloveBlend = Mathf.MoveTowards(_gloveBlend, blendTarget, dt * 6.5f);
        if (_gloveBlend > 0.001f && _gloveHand != null && _glove != null)
        {
            ApplyGloveReach(_gloveBlend, dt);
        }

        // 리치 요청은 한 프레임만 유효하다. 매 프레임 SetGloveTarget 을 호출해야 유지된다.
        _gloveReaching = false;
    }

    // ============================================================
    //  개별 액션 포즈
    // ============================================================

    static float EaseOut(float t) { return 1f - (1f - t) * (1f - t); }
    static float EaseIn(float t) { return t * t; }
    static float Arc(float t) { return Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); }

    void ApplyReady(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 glovePos, ref Quaternion gloveRot)
    {
        float k = Arc(p);
        bodyPos += new Vector3(0f, -0.10f * k, 0f);
        bodyRot = bodyRot * Quaternion.Euler(9f * k, 0f, 0f);
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.45f, -0.55f, 0.55f), k * 0.85f);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(-25f, 20f, 0f), k * 0.85f);
    }

    void ApplyFieldGrounder(float p, ref Vector3 bodyPos, ref Quaternion bodyRot,
                            ref Vector3 glovePos, ref Quaternion gloveRot, ref Vector3 throwPos)
    {
        float k = EaseOut(p);
        // 무릎을 굽히고 상체를 숙인다
        bodyPos += new Vector3(0f, -0.30f * k, 0.10f * k);
        bodyRot = bodyRot * Quaternion.Euler(42f * k, 0f, 0f);
        // 글러브를 지면 앞쪽으로
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.30f, -1.05f, 1.05f), k);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(-58f, 12f, 0f), k);
        // 반대 손은 글러브 옆에서 공을 덮는다
        throwPos = Vector3.Lerp(throwPos, new Vector3(-0.05f, -1.00f, 1.00f), k);
    }

    void ApplyDive(float p, ref Vector3 bodyPos, ref Quaternion bodyRot,
                   ref Vector3 glovePos, ref Quaternion gloveRot,
                   ref Vector3 throwPos, ref Quaternion throwRot)
    {
        float launch = EaseOut(Mathf.Clamp01(p / 0.55f));
        Vector3 pos = _actionStartPos + _actionDir * (_actionDistance * launch);
        pos.y = _actionStartPos.y + Arc(Mathf.Clamp01(p / 0.85f)) * _actionHeight;
        _root.position = pos;

        // 몸을 다이빙 방향으로 눕힌다
        Quaternion look = Quaternion.LookRotation(_actionDir, Vector3.up);
        _root.rotation = Quaternion.Slerp(_actionStartRot, look, Mathf.Clamp01(p * 3f));

        float lay = Mathf.Clamp01(p / 0.5f);
        bodyPos += new Vector3(0f, -0.20f * lay, 0.15f * lay);
        bodyRot = bodyRot * Quaternion.Euler(78f * lay, 0f, 0f);

        // 글러브 팔을 최대한 앞으로 뻗는다
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.15f, 0.25f, 1.85f), lay);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(-40f, 0f, 0f), lay);
        // 반대 팔은 뒤로 벌린다
        throwPos = Vector3.Lerp(throwPos, new Vector3(-1.05f, -0.20f, 0.35f), lay);
        throwRot = Quaternion.Slerp(throwRot, _throwRestRot * Quaternion.Euler(0f, -35f, 0f), lay);
    }

    void ApplyGetUp(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 glovePos, ref Vector3 throwPos)
    {
        float k = 1f - EaseOut(p);   // 1 → 0 으로 복귀
        bodyPos += new Vector3(0f, -0.20f * k, 0.15f * k);
        bodyRot = bodyRot * Quaternion.Euler(78f * k, 0f, 0f);
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.15f, 0.25f, 1.85f), k);
        throwPos = Vector3.Lerp(throwPos, new Vector3(-1.05f, -0.20f, 0.35f), k);
    }

    void ApplyJump(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 glovePos, ref Quaternion gloveRot)
    {
        float arc = Arc(p);
        Vector3 pos = _actionStartPos + _actionDir * (_actionDistance * EaseOut(p));
        pos.y = _actionStartPos.y + arc * _actionHeight;
        _root.position = pos;

        // 몸을 곧게 펴고 글러브 팔을 위로
        bodyPos += new Vector3(0f, 0.06f * arc, 0f);
        bodyRot = bodyRot * Quaternion.Euler(-10f * arc, 0f, 0f);
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.30f, 1.55f, 0.55f), arc);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(35f, 0f, 0f), arc);
    }

    void ApplySlide(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 glovePos, ref Quaternion gloveRot)
    {
        float k = EaseOut(p);
        Vector3 pos = _actionStartPos + _actionDir * (_actionDistance * k);
        pos.y = _actionStartPos.y;
        _root.position = pos;
        _root.rotation = Quaternion.Slerp(_actionStartRot,
            Quaternion.LookRotation(_actionDir, Vector3.up), Mathf.Clamp01(p * 4f));

        float lay = Arc(Mathf.Clamp01(p / 0.9f));
        bodyPos += new Vector3(0f, -0.45f * lay, 0f);
        bodyRot = bodyRot * Quaternion.Euler(62f * lay, 0f, 18f * lay);
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.55f, -0.85f, 1.35f), lay);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(-55f, 25f, 0f), lay);
    }

    void ApplyCrowHop(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 glovePos, ref Vector3 throwPos)
    {
        float k = EaseOut(p);
        Vector3 pos = _actionStartPos + _actionDir * (_actionDistance * k);
        pos.y = _actionStartPos.y + Arc(p) * _actionHeight;
        _root.position = pos;
        _root.rotation = Quaternion.Slerp(_actionStartRot,
            Quaternion.LookRotation(_actionDir, Vector3.up), Mathf.Clamp01(p * 3.2f));

        float gather = Arc(p);
        bodyPos += new Vector3(0f, -0.08f * gather, 0f);
        bodyRot = bodyRot * Quaternion.Euler(-6f * gather, 0f, 0f);
        // 공을 가슴 쪽으로 모은다
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.40f, -0.55f, 0.70f), gather);
        throwPos = Vector3.Lerp(throwPos, new Vector3(0.30f, -0.55f, 0.70f), gather);
    }

    /// <summary>
    /// 송구 4단계 : 볼 빼기(Transfer) → 테이크백(Cock) → 릴리스(Release) → 팔로우스루(Follow)
    /// </summary>
    void ApplyThrow(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 shoulderRot,
                    ref Vector3 glovePos, ref Quaternion gloveRot,
                    ref Vector3 throwPos, ref Quaternion throwRot)
    {
        const float T_TRANSFER = 0.30f;
        const float T_COCK = 0.62f;
        const float T_RELEASE = 0.74f;

        // 몸을 목표 방향으로 정렬
        Vector3 aimDir = _throwAim - _root.position;
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
            _root.rotation = Quaternion.Slerp(_actionStartRot, look, Mathf.Clamp01(p / T_COCK));
        }

        // 팔 슬롯 : 사이드암이면 팔을 낮게 돌린다
        float slot = _sidearm ? -1f : 1f;

        Vector3 chestGlove = new Vector3(0.40f, -0.70f, 0.70f);
        Vector3 chestThrow = new Vector3(0.32f, -0.70f, 0.70f);
        Vector3 cockPos = _sidearm ? new Vector3(-1.55f, -0.15f, -0.55f) : new Vector3(-1.45f, 0.70f, -0.65f);
        Vector3 releasePos = _sidearm ? new Vector3(-0.25f, 0.05f, 1.45f) : new Vector3(-0.20f, 0.62f, 1.40f);
        Vector3 followPos = new Vector3(0.85f, -1.25f, 0.45f);

        if (p < T_TRANSFER)
        {
            float k = EaseOut(p / T_TRANSFER);
            glovePos = Vector3.Lerp(_gloveRestPos, chestGlove, k);
            gloveRot = Quaternion.Slerp(_gloveRestRot, _gloveRestRot * Quaternion.Euler(-20f, 35f, 0f), k);
            throwPos = Vector3.Lerp(_throwRestPos, chestThrow, k);
            throwRot = Quaternion.Slerp(_throwRestRot, _throwRestRot * Quaternion.Euler(40f, -40f, 0f), k);
            bodyRot = bodyRot * Quaternion.Euler(4f * k, 0f, 0f);
            shoulderRot.y += 8f * k;
        }
        else if (p < T_COCK)
        {
            float k = EaseOut((p - T_TRANSFER) / (T_COCK - T_TRANSFER));
            throwPos = Vector3.Lerp(chestThrow, cockPos, k);
            throwRot = Quaternion.Slerp(_throwRestRot * Quaternion.Euler(40f, -40f, 0f),
                                        _throwRestRot * Quaternion.Euler(-70f * slot, 15f, 25f), k);
            glovePos = Vector3.Lerp(chestGlove, new Vector3(0.85f, -0.20f, 1.25f), k);  // 글러브로 조준
            gloveRot = Quaternion.Slerp(_gloveRestRot * Quaternion.Euler(-20f, 35f, 0f),
                                        _gloveRestRot * Quaternion.Euler(10f, -15f, 0f), k);
            bodyRot = bodyRot * Quaternion.Euler(0f, 0f, -10f * k * slot);
            shoulderRot.y += Mathf.Lerp(8f, -32f, k);
            bodyPos += new Vector3(0f, -0.06f * k, 0f);
        }
        else if (p < T_RELEASE)
        {
            float k = EaseIn((p - T_COCK) / (T_RELEASE - T_COCK));
            throwPos = Vector3.Lerp(cockPos, releasePos, k);
            throwRot = Quaternion.Slerp(_throwRestRot * Quaternion.Euler(-70f * slot, 15f, 25f),
                                        _throwRestRot * Quaternion.Euler(55f, 0f, 0f), k);
            glovePos = Vector3.Lerp(new Vector3(0.85f, -0.20f, 1.25f), new Vector3(0.75f, -0.85f, 0.25f), k);
            bodyRot = bodyRot * Quaternion.Euler(10f * k, 0f, -10f * (1f - k) * slot);
            shoulderRot.y += Mathf.Lerp(-32f, 26f, k);
        }
        else
        {
            float k = EaseOut((p - T_RELEASE) / (1f - T_RELEASE));
            throwPos = Vector3.Lerp(releasePos, Vector3.Lerp(followPos, _throwRestPos, k), Mathf.Min(1f, k * 2f));
            throwRot = Quaternion.Slerp(_throwRestRot * Quaternion.Euler(55f, 0f, 0f), _throwRestRot, k);
            glovePos = Vector3.Lerp(new Vector3(0.75f, -0.85f, 0.25f), _gloveRestPos, k);
            gloveRot = Quaternion.Slerp(_gloveRestRot * Quaternion.Euler(10f, -15f, 0f), _gloveRestRot, k);
            bodyRot = bodyRot * Quaternion.Euler(12f * (1f - k), 0f, 0f);
            shoulderRot.y += 26f * (1f - k);
        }

        if (!_released && p >= _releaseNormalized)
        {
            _released = true;
            if (_onRelease != null)
            {
                ReleaseCallback cb = _onRelease;
                _onRelease = null;
                cb();
            }
        }
    }

    void ApplyStretch(float p, ref Vector3 bodyPos, ref Quaternion bodyRot,
                      ref Vector3 glovePos, ref Quaternion gloveRot, ref Vector3 throwPos)
    {
        float k = EaseOut(p);
        // 뒷발은 베이스에 두고 상체와 글러브만 송구 쪽으로 뻗는다
        bodyPos += new Vector3(0f, -0.34f * k, 0.30f * k);
        bodyRot = bodyRot * Quaternion.Euler(26f * k, 0f, 0f);
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.20f, 0.05f, 1.95f), k);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(-15f, 0f, 0f), k);
        throwPos = Vector3.Lerp(throwPos, new Vector3(-1.10f, -0.35f, 0.20f), k);
    }

    void ApplySweepTag(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 glovePos, ref Quaternion gloveRot)
    {
        float k = Arc(p);
        bodyPos += new Vector3(0f, -0.22f * k, 0f);
        bodyRot = bodyRot * Quaternion.Euler(30f * k, 0f, 0f);
        // 글러브가 아래로 스윕
        float sweep = Mathf.Lerp(-0.6f, 0.9f, p);
        glovePos = Vector3.Lerp(glovePos, new Vector3(sweep, -1.15f, 1.15f), k);
        gloveRot = Quaternion.Slerp(gloveRot, _gloveRestRot * Quaternion.Euler(-70f, 0f, 0f), k);
    }

    /// <summary>
    /// 2루 병살 피벗 (설계도 10.1-4).
    /// 베이스를 발끝으로 찍는 순간(전반부) → 슬라이딩 주자를 피해 베이스 바깥으로
    /// 점프/스텝스루하며 착지(후반부). 착지와 동시에 송구 팔이 뒤로 젖혀진다.
    /// </summary>
    void ApplyPivot(float p, ref Vector3 bodyPos, ref Quaternion bodyRot,
                    ref Vector3 glovePos, ref Vector3 throwPos, ref Quaternion throwRot)
    {
        // 루트 : 베이스를 스치고 바깥쪽으로 빠진다
        Vector3 offset = _actionDir * (_actionDistance * EaseOut(p));
        offset.y = Arc(p) * _actionHeight;
        _root.position = _actionStartPos + offset;

        // 몸통 : 살짝 웅크렸다가 펴면서 송구 방향으로 비튼다
        float crouch = Arc(p);
        bodyPos += new Vector3(0f, -0.10f * crouch, 0f);
        bodyRot = bodyRot * Quaternion.Euler(14f * crouch, 0f, -18f * crouch);

        // 글러브는 가슴 앞으로 모으고(볼 빼기), 송구 손은 뒤로 젖힌다
        glovePos = Vector3.Lerp(glovePos, new Vector3(0.12f, -0.35f, 0.42f), crouch);
        float cock = EaseIn(p);
        throwPos = Vector3.Lerp(throwPos, new Vector3(-0.45f, 0.30f, -0.45f), cock);
        throwRot = Quaternion.Slerp(throwRot, _throwRestRot * Quaternion.Euler(-95f, 0f, 0f), cock);
    }

    /// <summary>
    /// 런다운 펌프 페이크 (설계도 9장 원칙 ③).
    /// 던지는 시늉만 하고 공은 놓지 않는다 — 주자의 감속·역방향 전환을 유도한다.
    /// </summary>
    void ApplyPumpFake(float p, ref Vector3 bodyPos, ref Quaternion bodyRot, ref Vector3 shoulderRot,
                       ref Vector3 throwPos, ref Quaternion throwRot)
    {
        // 팔을 뒤로 젖혔다가(전반) 앞으로 내미는 척하고 멈춘다(후반)
        float k = Arc(p);
        float cock = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI * 0.85f);

        bodyPos += new Vector3(0f, -0.04f * k, 0f);
        bodyRot = bodyRot * Quaternion.Euler(8f * k, 0f, 0f);
        shoulderRot.y += -26f * cock;

        throwPos = Vector3.Lerp(throwPos, new Vector3(-0.40f, 0.34f, -0.38f), cock);
        throwRot = Quaternion.Slerp(throwRot, _throwRestRot * Quaternion.Euler(-105f * cock, 0f, 0f), cock);
    }

    /// <summary>글러브가 공에 정확히 닿도록 팔 위치를 월드 공간에서 보정한다.</summary>
    void ApplyGloveReach(float blend, float dt)
    {
        Vector3 gloveNow = _glove.position;
        Vector3 handNow = _gloveHand.position;
        Vector3 shoulderPos = _shoulder != null ? _shoulder.position : handNow;

        // 글러브가 목표에 놓이려면 손이 얼마나 움직여야 하는가
        Vector3 desiredHand = handNow + (_gloveTarget - gloveNow);

        // 팔 길이를 넘어서 늘어나지 않게 제한
        Vector3 off = desiredHand - shoulderPos;
        float maxReach = _armLength * (IsExtended ? 2.15f : 1.75f);
        if (off.magnitude > maxReach) desiredHand = shoulderPos + off.normalized * maxReach;

        Vector3 finalHand = Vector3.Lerp(handNow, desiredHand, blend);
        _gloveHand.position = finalHand;

        Vector3 lookDir = _gloveTarget - finalHand;
        if (lookDir.sqrMagnitude > 0.0004f)
        {
            Quaternion look = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            _gloveHand.rotation = Quaternion.Slerp(_gloveHand.rotation, look, blend * 0.85f);
        }
    }
}
