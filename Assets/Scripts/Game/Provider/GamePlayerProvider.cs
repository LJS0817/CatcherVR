using TMPro;
using UnityEngine;

public class GamePlayerProvider : MonoBehaviour
{
    public Team TeamA;
    public Team TeamB;

    public Ball _ball;
    public Transform BallPool;
    
    bool _leftSideIsAttacker;

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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        teamInit();
        _leftSideIsAttacker = false;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void teamInit()
    {
        TeamA.init(BallPool, _ball, _leftSideIsAttacker);
        TeamB.init(BallPool, _ball, !_leftSideIsAttacker);
    }

    public void PlayerOut(PLAYER_TYPE t)
    {
        if (!_leftSideIsAttacker) TeamB.PlayerOut(t);
        else TeamA.PlayerOut(t);
    }

    public void Walk()
    {
        // 볼넷 처리
        BasePositionProvider.provider.AdvanceRunnersForWalk();
        
        Team attackTeam = _leftSideIsAttacker ? TeamA : TeamB;
        attackTeam.ChangeAttacker(); // 다음 타자
    }

    public void Strikeout()
    {
        // 삼진 처리
        Team attackTeam = _leftSideIsAttacker ? TeamA : TeamB;
        attackTeam.ChangeAttacker(); // 다음 타자
    }

    public void ChangeInning()
    {
        // 공수 교대
        _leftSideIsAttacker = !_leftSideIsAttacker;

        TeamA.ChangeRole(_leftSideIsAttacker);
        TeamB.ChangeRole(!_leftSideIsAttacker);

        BasePositionProvider.provider.ClearBases(); // 베이스 리셋
        if (CountsProvider.provider != null) CountsProvider.provider.ResetInningCounts();
    }

    public void ResetPlay()
    {
        // 투수에게 공을 돌려주고 수비진을 제자리로 돌려보내는 리셋 로직 (타자 교체 안 함)
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
