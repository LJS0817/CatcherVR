using UnityEngine;

public class GamePlayerProvider : MonoBehaviour
{
    public Team TeamA;
    public Team TeamB;

    public Ball _ball;
    public Transform BallPool;

    /// <summary>현재 경기 공. 수비 조율자가 궤적을 읽기 위해 사용.</summary>
    public Ball GameBall => _ball;

    bool _leftSideIsAttacker;
    public bool LeftSideIsAttacker => _leftSideIsAttacker;
    public Team AttackTeam => _leftSideIsAttacker ? TeamA : TeamB;
    public Team DefenseTeam => _leftSideIsAttacker ? TeamB : TeamA;

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
        FieldMetrics.Refresh(true);
        teamInit();
    }

    void teamInit()
    {
        TeamA.init(BallPool, _ball, _leftSideIsAttacker);
        TeamB.init(BallPool, _ball, !_leftSideIsAttacker);
    }

    public void TriggerAReset()
    {
        if (TeamA != null) TeamA.TriggerAReset();
        if (TeamB != null) TeamB.TriggerAReset();
    }

    public void NotifyBatterHit()
    {
        Team attackTeam = AttackTeam;
        if (attackTeam != null)
        {
            BaseballPlayer batter = attackTeam.GetCurrentBatter();
            if (batter != null && batter.GetRole() is Attacker attacker)
            {
                // OnHitBall 내부에서 기존 주자들에게 진루 명령을 전파하므로
                // 여기서 NotifyRunnersToAdvance 를 다시 부르면 이중 진루가 발생한다.
                attacker.OnHitBall();
            }
        }
    }

    public void PlayerOut(PLAYER_TYPE t)
    {
        Team attackTeam = AttackTeam;
        if (attackTeam != null) attackTeam.PlayerOut(t);
    }

    public void Walk()
    {
        BasePositionProvider.provider.AdvanceRunnersForWalk();

        Team attackTeam = AttackTeam;
        if (attackTeam != null) attackTeam.ChangeAttacker();
    }

    public void Strikeout()
    {
        Team attackTeam = AttackTeam;
        if (attackTeam != null) attackTeam.ChangeAttacker();
    }

    /// <summary>
    /// 공수전환 대기 없이 다음 이닝으로 즉시 전환.
    /// </summary>
    public void ChangeInning()
    {
        SimulateOffInning();

        if (CountsProvider.provider != null)
        {
            CountsProvider.provider.NextInning();
        }

        BasePositionProvider.provider.ClearBases();

        Team attackTeam = AttackTeam;
        if (attackTeam != null) attackTeam.ChangeAttacker();

        ResetPlay();
    }

    void SimulateOffInning()
    {
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
        FieldMetrics.Refresh(true);

        // 수비 조율자 종료 + 상황별 수비 포메이션(뎁스/시프트) 재계산 후 원위치
        if (TeamA != null) TeamA.ResetDefense();
        if (TeamB != null) TeamB.ResetDefense();

        if (_ball != null)
        {
            _ball.gameObject.SetActive(true);
            Transform pitcherItem = DefenseTeam != null ? DefenseTeam.GetPlayerItem(PLAYER_TYPE.E_PITCHER) : null;

            if (pitcherItem != null)
            {
                _ball.transform.position = pitcherItem.position;
            }
            _ball.ResetVelocity();
            _ball.init(BallPool, pitcherItem, Vector3.zero, 0, true);
        }
    }
}
