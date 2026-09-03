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
        // 공이 살아있는 동안 수비 역할을 주기적으로 재평가한다.
        if (_isAttack || !_playLive) return;

        GamePlayerProvider gp = GamePlayerProvider.provider;
        Ball ball = gp != null ? gp.GameBall : null;
        if (ball == null) return;

        BallTrajectory traj = ball.Trajectory;
        if (traj == null || !traj.IsValid) return;

        Coordinator.Tick(traj, Time.time - _playStartTime);
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
        }

        DefensiveAlignment.ApplyInPlay(this, traj, type);
        Coordinator.OnBattedBall(traj, type);
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
        if (!_isAttack) Coordinator.EndPlay();
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
