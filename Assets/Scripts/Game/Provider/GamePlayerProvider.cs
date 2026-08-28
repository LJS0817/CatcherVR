using TMPro;
using UnityEngine;

public class GamePlayerProvider : MonoBehaviour
{
    public Team TeamA;
    public Team TeamB;

    public Ball _ball;
    public Transform BallPool;
    
    bool _leftSideIsAttacker;
    public bool LeftSideIsAttacker => _leftSideIsAttacker;
    public Team AttackTeam => _leftSideIsAttacker ? TeamA : TeamB;

    private static GamePlayerProvider _provider;

    public static GamePlayerProvider provider
    {
        get
        {
            return _provider;
        }
    }

    private void Awake()
    {
        if (_provider != null && _provider == this) Destroy(this.gameObject);
        else _provider = this;
    }

    void Start()
    {
        _leftSideIsAttacker = false;
        teamInit();
    }

    void teamInit()
    {
        TeamA.init(BallPool, _ball, _leftSideIsAttacker);
        TeamB.init(BallPool, _ball, !_leftSideIsAttacker);
    }

    public void NotifyBatterHit()
    {
        Team attackTeam = _leftSideIsAttacker ? TeamA : TeamB;
        if (attackTeam != null)
        {
            BaseballPlayer batter = attackTeam.GetCurrentBatter();
            if (batter != null && batter.GetRole() is Attacker attacker)
            {
                attacker.OnHitBall();
                attackTeam.NotifyRunnersToAdvance(_ball != null ? _ball.GetLandPosition() : Vector3.zero, batter);
            }
        }
    }

    public void PlayerOut(PLAYER_TYPE t)
    {
        if (!_leftSideIsAttacker) TeamB.PlayerOut(t);
        else TeamA.PlayerOut(t);
    }

    public void Walk()
    {
        // 볼넷 처리: 주자 1베이스 진루 후 다음 타자
        BasePositionProvider.provider.AdvanceRunnersForWalk();
        
        Team attackTeam = _leftSideIsAttacker ? TeamA : TeamB;
        attackTeam.ChangeAttacker();
    }

    public void Strikeout()
    {
        // 삼진 처리: 다음 타자
        Team attackTeam = _leftSideIsAttacker ? TeamA : TeamB;
        attackTeam.ChangeAttacker();
    }

    /// <summary>
    /// 공수전환 대기 없이 다음 이닝으로 즉시 전환 (Fast-forward Inning Simulation)
    /// 플레이어가 지루하게 대기하지 않도록 반대편 하프 이닝을 시뮬레이션하고 즉시 다음 이닝 시작
    /// </summary>
    public void ChangeInning()
    {
        // 1. 비플레이어 하프 이닝 즉시 시뮬레이션
        SimulateOffInning();

        // 2. 이닝 증가 및 카운트(Out, Strike, Ball) 0으로 초기화
        if (CountsProvider.provider != null)
        {
            CountsProvider.provider.NextInning();
        }

        // 3. 베이스의 모든 주자 초기화 (주자 리셋)
        BasePositionProvider.provider.ClearBases();

        // 4. 다음 이닝 첫 타자 준비 (타순 순환)
        Team attackTeam = _leftSideIsAttacker ? TeamA : TeamB;
        attackTeam.ChangeAttacker();

        // 5. 플레이 리셋 (수비진 원위치 복귀 + 투수에게 새 공 전달)
        ResetPlay();
    }

    /// <summary>
    /// 플레이어가 관여하지 않는 하프 이닝을 빠르게 시뮬레이션
    /// </summary>
    void SimulateOffInning()
    {
        // 간단한 야구 확률 시뮬레이션 (0점: 65%, 1점: 25%, 2점: 8%, 3점: 2%)
        int simulatedTeamRuns = 0;
        int simulatedOpponentRuns = 0;

        float r1 = Random.value;
        if (r1 > 0.98f) simulatedTeamRuns = 3;
        else if (r1 > 0.90f) simulatedTeamRuns = 2;
        else if (r1 > 0.65f) simulatedTeamRuns = 1;

        float r2 = Random.value;
        if (r2 > 0.98f) simulatedOpponentRuns = 3;
        else if (r2 > 0.90f) simulatedOpponentRuns = 2;
        else if (r2 > 0.65f) simulatedOpponentRuns = 1;

        if (CountsProvider.provider != null)
        {
            CountsProvider.provider.AddSimulatedRuns(simulatedTeamRuns, simulatedOpponentRuns);
        }
    }

    public void ResetPlay()
    {
        // 수비진을 제자리로 돌려보내고 투수에게 공을 지급하는 리셋
        TeamA.ResetDefense();
        TeamB.ResetDefense();
        
        if (_ball != null)
        {
            _ball.gameObject.SetActive(true);
            Transform pitcherItem = null;
            if (!_leftSideIsAttacker) pitcherItem = TeamA.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
            else pitcherItem = TeamB.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
            
            if (pitcherItem != null)
            {
                _ball.transform.position = pitcherItem.position;
            }
            _ball.ResetVelocity();
            _ball.init(BallPool, pitcherItem, Vector3.zero, 0, true);
        }
    }
}
