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
}
