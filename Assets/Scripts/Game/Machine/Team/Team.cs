using System.Collections.Generic;
using UnityEngine;

public class Team : MonoBehaviour
{
    List<BaseballPlayer> _players;
    int _attackNum;
    bool _isAttack;

    FieldingCoordinator _coordinator;
    int _lastHandledTrajVersion = -1;
    float _playStartTime;
    bool _playLive;

    // ── 페일세이프 (설계도 12.4) ──────────────────────────────
    float _runnersStoppedSince = -1f;
    bool _failsafeFired;

    // ── 위치 초기화 시퀀스 (설계도 12.3) ──────────────────────
    bool _resetPending;
    float _resetStartTime;
    /// <summary>이번 플레이의 타자 교체 · 리셋이 이미 처리되었는가(중복 방지).</summary>
    bool _playResolved = true;

    /// <summary>이 팀의 수비 조율자.</summary>
    public FieldingCoordinator Coordinator
    {
        get
        {
            if (_coordinator == null) _coordinator = new FieldingCoordinator(this);
            return _coordinator;
        }
    }

    public int PlayerCount { get { return _players != null ? _players.Count : 0; } }
    public bool IsAttackTeam { get { return _isAttack; } }

    void Start()
    {
        _attackNum = 0;
    }

    void Update()
    {
        if (_isAttack) return;

        // 투수가 공을 확보한 뒤 : 전원이 걸어서 원위치로 돌아오길 기다린다.
        if (_resetPending)
        {
            TickResetSequence();
            return;
        }

        if (!_playLive) return;

        GamePlayerProvider gp = GamePlayerProvider.provider;
        Ball ball = gp != null ? gp.GameBall : null;
        if (ball == null) return;

        EvaluateTermination(ball);

        TickFailsafe(ball);
        if (!_playLive) return;

        BallTrajectory traj = ball.Trajectory;
        if (traj == null || !traj.IsValid) return;

        Coordinator.Tick(traj, Time.time - _playStartTime);
    }

    // ============================================================
    //  위치 초기화 시퀀스 (설계도 12.3)
    // ============================================================

    /// <summary>
    /// 모든 수비수가 자연스러운 걸음으로 기본 위치에 도착하면 READY 로 넘어간다.
    /// 물리 이상으로 도착하지 못하는 경우를 대비해 상한 시간을 둔다.
    /// </summary>
    void TickResetSequence()
    {
        bool allHome = true;
        float radius = FieldMetrics.M(DefenseTuning.READY_ARRIVE_RADIUS);

        if (_players != null)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                BaseballPlayer p = _players[i];
                if (p == null || !p.gameObject.activeSelf) continue;
                if (!(p.GetRole() is Defencer)) continue;
                if (FieldMetrics.FlatDistance(p.transform.position, p.HomePosition) > radius)
                {
                    allHome = false;
                    break;
                }
            }
        }

        if (!allHome && Time.time - _resetStartTime < 3.5f) return;

        _resetPending = false;
        CompletePlay();
    }

    /// <summary>
    /// 한 플레이의 마무리(타자 교체 + 전원 위치 리셋)를 수행하는 <b>유일한</b> 지점.
    /// 여러 경로(정상 반환 · 플라이 아웃 · 페일세이프)에서 호출되어도 한 번만 실행된다.
    /// </summary>
    void CompletePlay()
    {
        if (_playResolved) return;
        _playResolved = true;

        GamePlayerProvider gp = GamePlayerProvider.provider;
        if (gp == null) return;
        if (gp.AttackTeam != null) gp.AttackTeam.ChangeAttacker();
        gp.TriggerAReset();
    }

    // ============================================================
    //  페일세이프 (설계도 12.4 무한 루프 방지)
    // ============================================================

    /// <summary>
    /// 물리 이상 · 버그 · 송구 실책 연쇄로 플레이가 영원히 끝나지 않는 사태를 막는다.
    ///   · 타격 후 MAX_PLAY_TIME(8초) 경과
    ///   · 또는 주자가 모두 멈춘 뒤 STATIONARY_TIMEOUT(3초) 동안 공이 투수에게 오지 않음
    /// 플레이어가 공을 쥐고 있는 동안에는 더 긴 여유(PLAYER_HOLD_TIMEOUT)를 준다.
    /// </summary>
    void TickFailsafe(Ball ball)
    {
        if (_failsafeFired) return;

        float elapsed = Time.time - _playStartTime;

        // 플레이어(VR 포수)가 공을 들고 있으면 조작 중일 수 있으므로 넉넉히 기다린다.
        bool playerHolding = !ball.isCatchable() && !ball.HeldByFielder;
        float hardLimit = playerHolding ? DefenseTuning.PLAYER_HOLD_TIMEOUT : DefenseTuning.MAX_PLAY_TIME;

        bool overTime = elapsed > hardLimit;

        // 주자 정지 감시
        bool allStopped = AllRunnersStationary();
        if (allStopped)
        {
            if (_runnersStoppedSince < 0f) _runnersStoppedSince = Time.time;
        }
        else
        {
            _runnersStoppedSince = -1f;
        }

        bool stalled = !playerHolding && allStopped && _runnersStoppedSince > 0f &&
                       (Time.time - _runnersStoppedSince) > DefenseTuning.STATIONARY_TIMEOUT &&
                       !BallIsWithPitcher(ball);

        if (overTime || stalled)
        {
            ForceTerminatePlay(ball);
        }
    }

    /// <summary>
    /// 설계도 12.1 「상황 종료(No Further Out Possible)」 판정.
    /// 아래 조건이 <b>모두</b> 충족되어야 종료로 본다.
    ///   1. 더 이상 아웃시킬 수 없음 (모든 주자에 대해 어떤 베이스로 던져도 늦음)
    ///   2. 모든 주자가 베이스에 안착하고 정지
    ///   3. 야수 중 한 명이 공을 완전히 확보
    ///   4. 추가 진루 시도 없음
    /// </summary>
    public bool IsPlayTerminated { get; private set; }

    void EvaluateTermination(Ball ball)
    {
        IsPlayTerminated = false;
        if (ball == null) return;

        // 3. 공의 완전 제어 — 굴러가거나 공중에 있으면 종료가 아니다
        if (!ball.HeldByFielder) return;
        BaseballPlayer holder = ball.Holder != null ? ball.Holder.GetComponentInParent<BaseballPlayer>() : null;
        if (holder == null) return;

        // 2 · 4. 모든 주자 정지 + 추가 진루 시도 없음
        if (!AllRunnersStationary()) return;

        // 1. 더 이상 아웃 불가
        if (!ThrowDecision.NoFurtherOutPossible(holder, holder.Attributes, ball.GetPosition(), this)) return;

        IsPlayTerminated = true;

        // 전술 컨텍스트에도 반영해 다른 판단이 같은 스냅샷을 보게 한다
        GameTacticalContext.Current().IsPlayTerminated = true;
    }

    static bool AllRunnersStationary()
    {
        List<RunnerInfo> runners = RunnerTracker.Snapshot();
        for (int i = 0; i < runners.Count; i++)
        {
            if (runners[i].IsRunning) return false;
        }
        return true;
    }

    bool BallIsWithPitcher(Ball ball)
    {
        if (ball == null || ball.Holder == null) return false;
        BaseballPlayer holder = ball.Holder.GetComponentInParent<BaseballPlayer>();
        return holder != null && holder.Type == PLAYER_TYPE.E_PITCHER;
    }

    /// <summary>공을 투수에게 강제 회수하고 플레이를 안전하게 마감한다.</summary>
    public void ForceTerminatePlay(Ball ball)
    {
        if (_isAttack) return;

        _failsafeFired = true;
        Debug.LogWarning("[Defense] 페일세이프 발동 — 플레이를 강제 종료하고 공을 투수에게 회수합니다.");

        Transform pitcherHand = GetPlayerItem(PLAYER_TYPE.E_PITCHER);
        if (ball != null && pitcherHand != null) ball.ForceReturnTo(pitcherHand);

        EndPlay();
        _resetPending = false;
        CompletePlay();
    }

    /// <summary>
    /// 투수가 마운드에서 공을 확보했다 — 전원 기본 위치 리셋을 브로드캐스트한다(설계도 12.3).
    /// 수비수들은 전력 질주가 아니라 걸어서 복귀하고, 전원이 도착하면 READY 로 넘어간다.
    /// </summary>
    public void OnPitcherSecuredBall()
    {
        if (_isAttack) return;
        if (_resetPending) return;      // 이미 리셋 진행 중

        bool needsCompletion = !_playResolved;

        EndPlay();

        // 이닝 교체 등으로 이미 마무리된 플레이거나, 타격 없이 공만 회수한 경우에는
        // 타자 교체 · 전원 리셋을 다시 하지 않는다.
        if (!needsCompletion) return;

        _resetPending = true;
        _resetStartTime = Time.time;
    }

    public BaseballPlayer GetPlayerAt(int index)
    {
        if (_players == null || index < 0 || index >= _players.Count) return null;
        return _players[index];
    }

    public Transform GetPlayerItem(PLAYER_TYPE type)
    {
        BaseballPlayer p = GetPlayer(type);
        return p != null ? p.GetItem() : null;
    }

    public void TriggerAReset()
    {
        if (_players == null) return;
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].gameObject.activeSelf)
            {
                _players[i].TriggerAReset();
            }
        }
    }

    public BaseballPlayer GetCurrentBatter()
    {
        if (_players != null && _attackNum >= 0 && _attackNum < _players.Count)
        {
            return _players[_attackNum];
        }
        return null;
    }

    // ============================================================
    //  수비 조율 (FieldingCoordinator 위임)
    // ============================================================

    /// <summary>타구 발생. 궤적 버전으로 중복 호출을 막는다(수비수 9명이 모두 알려온다).</summary>
    public void OnBattedBall(BallTrajectory traj, BATTED_BALL_TYPE type)
    {
        if (_isAttack || traj == null) return;
        if (traj.Version == _lastHandledTrajVersion)
        {
            // 같은 궤적이면 재평가만
            Coordinator.Tick(traj, Time.time - _playStartTime);
            return;
        }

        _lastHandledTrajVersion = traj.Version;

        if (!_playLive)
        {
            _playLive = true;
            _playStartTime = Time.time;
            _runnersStoppedSince = -1f;
            _failsafeFired = false;
            _resetPending = false;
            _playResolved = false;      // 이 플레이는 아직 마무리되지 않았다
        }

        DefensiveAlignment.ApplyInPlay(this, traj, type);
        Coordinator.OnBattedBall(traj, type);
    }

    /// <summary>
    /// 루즈볼(플레이어 송구 미스 · 악송구 · 펜스 반사)이 발생했다.
    /// 설계도 11.3 : 타격 시와 <b>동일한</b> 유동적 수비 시스템을 즉시 재가동한다.
    /// </summary>
    public void OnLooseBall(BallTrajectory traj)
    {
        if (_isAttack || traj == null || !traj.IsValid) return;

        if (!_playLive)
        {
            // 타격 없이 발생한 루즈볼(플레이어가 공을 놓치거나 그냥 던진 경우)은
            // "공 회수" 만 하고 끝낸다. _playResolved 를 건드리지 않으므로
            // 회수가 끝나도 타자가 바뀌지 않는다.
            _playLive = true;
            _playStartTime = Time.time;
            _runnersStoppedSince = -1f;
            _failsafeFired = false;
            _resetPending = false;
        }

        _lastHandledTrajVersion = traj.Version;
        Coordinator.OnBattedBall(traj, BATTED_BALL_TYPE.GROUND_BALL);
    }

    /// <summary>상황이 바뀌었으니 수비 역할을 즉시 다시 계산한다(주자 출발 등).</summary>
    public void ForceReevaluate()
    {
        if (_isAttack || !_playLive) return;
        GamePlayerProvider gp = GamePlayerProvider.provider;
        Ball ball = gp != null ? gp.GameBall : null;
        if (ball == null) return;
        BallTrajectory traj = ball.Trajectory;
        if (traj == null || !traj.IsValid) return;
        Coordinator.OnBattedBall(traj, Coordinator.BallType);
    }

    /// <summary>플레이 종료 — 전원 원위치.</summary>
    public void EndPlay()
    {
        _playLive = false;
        _lastHandledTrajVersion = -1;
        _runnersStoppedSince = -1f;
        if (!_isAttack)
        {
            Coordinator.EndPlay();
            // 이번 플레이의 사용자 지휘 명령(베이스 지정 · 컷 콜)은 여기서 만료시킨다.
            DefenseCommand.ResetAll();
        }
    }

    /// <summary>송구가 출발했다 — 수구자에게 알린다.</summary>
    public void OnThrowReleased(BaseballPlayer thrower, BASE_TYPE targetBase, Vector3 aim)
    {
        if (_players == null) return;

        // 던진 야수가 자기 송구를 쫓아가지 않도록 역할을 다시 나눈다.
        if (!_isAttack) Coordinator.OnThrowStarted(thrower, targetBase, aim);

        for (int i = 0; i < _players.Count; i++)
        {
            BaseballPlayer p = _players[i];
            if (p == null || p == thrower || !p.gameObject.activeSelf) continue;
            Defencer d = p.GetRole() as Defencer;
            if (d != null) d.OnIncomingThrow(aim);
        }
    }

    /// <summary>특정 베이스를 커버하도록 배정된 야수.</summary>
    public BaseballPlayer GetBaseCoverFielder(BASE_TYPE b, BaseballPlayer exclude)
    {
        return Coordinator.GetBaseCoverFielder(b, exclude);
    }

    /// <summary>송구 라인 위에서 중계할 수 있는 야수.</summary>
    public BaseballPlayer GetRelayFielder(Vector3 from, Vector3 to, BaseballPlayer exclude)
    {
        return Coordinator.GetRelayFielder(from, to, exclude);
    }

    /// <summary>가장 가까운 내야수(외야 → 투수 반환 시 1차 중계 대상).</summary>
    public BaseballPlayer GetNearestInfielder(Vector3 from, BaseballPlayer exclude)
    {
        return Coordinator.GetNearestInfielder(from, exclude);
    }

    /// <summary>현재 배정된 중계맨(컷오프). 플레이어의 컷/통과 지시 대상.</summary>
    public BaseballPlayer GetCutoffMan()
    {
        return Coordinator.CutoffMan;
    }

    /// <summary>수비수가 공을 잡았을 때 다른 수비수들에게 알림.</summary>
    public void NotifyBallCaught(BaseballPlayer catcher)
    {
        if (_players == null) return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i] != catcher && _players[i].gameObject.activeSelf)
            {
                Defencer def = _players[i].GetRole() as Defencer;
                if (def != null) def.OnOtherFielderCaughtBall();
            }
        }

        GamePlayerProvider gp = GamePlayerProvider.provider;
        Ball ball = gp != null ? gp.GameBall : null;
        Coordinator.OnBallSecured(catcher, ball != null ? ball.Trajectory : null);
    }

    /// <summary>베이스에 가장 가까운/커버 중인 야수(하위 호환).</summary>
    public BaseballPlayer GetFielderCoveringBase(Transform targetBase)
    {
        if (targetBase == null || _players == null) return null;

        BaseballPlayer bestFielder = null;
        float minDist = float.MaxValue;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (!(_players[i].GetRole() is Defencer)) continue;

            if (_players[i].DefRole == DEFENSIVE_ROLE.BASE_COVER &&
                Vector3.Distance(_players[i].RoleTargetPosition, targetBase.position) < 1.5f)
            {
                return _players[i];
            }

            float dist = Vector3.Distance(_players[i].transform.position, targetBase.position);
            if (dist < minDist)
            {
                minDist = dist;
                bestFielder = _players[i];
            }
        }

        return minDist < 8.0f ? bestFielder : null;
    }

    /// <summary>하위 호환 : 예전 시그니처를 새 조율자로 위임.</summary>
    public void AssignDefensiveRoles(Vector3 landPos)
    {
        AssignDefensiveRoles(landPos, BATTED_BALL_TYPE.GROUND_BALL);
    }

    public void AssignDefensiveRoles(Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (_isAttack) return;
        GamePlayerProvider gp = GamePlayerProvider.provider;
        Ball ball = gp != null ? gp.GameBall : null;
        if (ball == null) return;
        OnBattedBall(ball.Trajectory, ballType);
    }

    /// <summary>하위 호환 : 낙구 지점 기준 최적 야수.</summary>
    public BaseballPlayer GetBestFielderForCatch(Vector3 landPos, BATTED_BALL_TYPE ballType = BATTED_BALL_TYPE.GROUND_BALL)
    {
        if (Coordinator.Primary != null) return Coordinator.Primary;

        BaseballPlayer best = null;
        float bestD = float.MaxValue;
        if (_players == null) return null;
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (!(_players[i].GetRole() is Defencer)) continue;
            float d = Vector3.Distance(_players[i].transform.position, landPos);
            if (d < bestD) { bestD = d; best = _players[i]; }
        }
        return best;
    }

    // ============================================================
    //  공격 / 주자
    // ============================================================

    public void ChangeAttacker()
    {
        if (_players != null && _players.Count > 0)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (i != _attackNum && _players[i] != null && _players[i].gameObject.activeSelf)
                {
                    if (!_players[i].IsActiveRunner)
                    {
                        _players[i].ExitGame();
                    }
                }
            }

            if (_attackNum >= 0 && _attackNum < _players.Count)
            {
                BaseballPlayer prevBatter = _players[_attackNum];
                if (prevBatter != null && !prevBatter.IsActiveRunner)
                {
                    prevBatter.ExitGame();
                }
            }

            _attackNum = (_attackNum + 1) % _players.Count;
            _players[_attackNum].JoinGame();
        }
    }

    public void NotifyRunnersToAdvance(Vector3 hitLandPos, BaseballPlayer currentBatter)
    {
        if (_players == null) return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i] != currentBatter && _players[i].gameObject.activeSelf)
            {
                if (_players[i].GetRole() is Attacker runner)
                {
                    runner.AdvanceRunnerOnNextHit(hitLandPos);
                }
            }
        }
    }

    public BaseballPlayer GetRunnerHeadingToBase(BASE_TYPE targetBase)
    {
        if (_players == null) return null;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].gameObject.activeSelf)
            {
                if (_players[i].GetRole() is Attacker runner)
                {
                    if (runner.IsOnBaseOrRunning && runner.TargetBase == targetBase)
                    {
                        return _players[i];
                    }
                }
            }
        }
        return null;
    }

    public BaseballPlayer GetPlayer(PLAYER_TYPE type)
    {
        if (_players == null) return null;
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].Type == type) return _players[i];
        }
        return null;
    }

    public void ChangeRole(bool isAttacker)
    {
        _isAttack = isAttacker;
        if (_players == null) return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null) continue;

            if (isAttacker)
            {
                if (i == _attackNum) _players[i].JoinGame();
                else _players[i].ExitGame();
            }
            else
            {
                _players[i].gameObject.SetActive(true);
                _players[i].ResetFieldingPosition();
                _players[i].ResetPosition();
            }
        }
    }

    public void ResetDefense()
    {
        if (_isAttack) return;

        EndPlay();
        _failsafeFired = false;
        _resetPending = false;
        _playResolved = true;           // 완전 리셋 — 추가 마무리 불필요
        DefensiveAlignment.ApplyPrePitch(this);

        if (_players == null) return;
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].gameObject.activeSelf)
            {
                _players[i].ResetPosition();
            }
        }
    }

    public void PlayerOut(PLAYER_TYPE t)
    {
        if (_players != null)
        {
            BaseballPlayer outPlayer = GetPlayer(t);
            if (outPlayer != null)
            {
                if (outPlayer.GetRole() is Attacker att)
                {
                    att.OnOut();
                }
                else
                {
                    outPlayer.ExitGame();
                }
            }
        }

        if (BasePositionProvider.provider != null)
        {
            for (int i = 0; i < 3; i++)
            {
                BASE_TYPE bType = (BASE_TYPE)(i + 2);
                char stateChar = BasePositionProvider.provider.GetAttckerBaseState(bType);
                if (stateChar != '_' && BasePositionProvider.provider.GetAttackerPlayerType(stateChar) == t)
                {
                    BasePositionProvider.provider.SetAttackerBaseState(bType, -1);
                }
            }
        }

        // E_PITCHER 는 "현재 타자" 와일드카드로도 쓰인다(Glove.cs 등 기존 호출 규약).
        bool isCurrentBatter = _players != null && _attackNum >= 0 && _attackNum < _players.Count &&
                               _players[_attackNum] != null && _players[_attackNum].Type == t;
        if (isCurrentBatter || t == PLAYER_TYPE.E_PITCHER)
        {
            ChangeAttacker();
        }
    }

    public void init(Transform pool, Ball ball, bool isAttacker)
    {
        _players = new List<BaseballPlayer>();
        _isAttack = isAttacker;
        for (int i = 0; i < transform.childCount; i++)
        {
            BaseballPlayer bp = transform.GetChild(i).GetComponent<BaseballPlayer>();
            if (bp == null) continue;
            bp.SetPlayerType((PLAYER_TYPE)i);
            _players.Add(bp);
            bp.EnterTheGame(pool, ball, isAttacker);
            if (isAttacker && _attackNum != i) bp.ExitGame();
        }

        _coordinator = new FieldingCoordinator(this);

        if (!isAttacker) DefensiveAlignment.ApplyPrePitch(this);
    }
}
