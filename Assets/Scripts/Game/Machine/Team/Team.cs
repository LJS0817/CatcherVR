using System.Collections.Generic;
using UnityEngine;

public class Team : MonoBehaviour
{
    List<BaseballPlayer> _players;
    int _attackNum;
    bool _isAttack;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _attackNum = 0;
    }

    // Update is called once per frame
    void Update()
    {
     
    }

    public Transform GetPlayerItem(PLAYER_TYPE type)
    {
        return _players[(int)type].GetItem();
    }

    public void ChangeAttacker()
    {
        setActivePlayer(_attackNum, false);
        _attackNum++;
        setActivePlayer(_attackNum, true);
    }

    void setActivePlayer(int idx, bool active)
    {
        if(active) _players[idx].JoinGame();
        else _players[idx].ExitGame();
    }

    public void PlayerOut(PLAYER_TYPE t)
    {
        if ((int)t == _attackNum || t == PLAYER_TYPE.E_PITCHER) ChangeAttacker();
        else setActivePlayer((int)t, false);
    }

    public void init(Transform pool, Ball ball, bool isAttacker)
    {
        //Debug.Log(transform.name + "  " + role);
        _players = new List<BaseballPlayer>();
        _isAttack = isAttacker;
        for (int i = 0; i < transform.childCount; i++)
        {
            _players.Add(transform.GetChild(i).GetComponent<BaseballPlayer>());
            //_players[i].FocusBall(ball, role);
            _players[i].EnterTheGame(pool, ball, isAttacker);
            if (isAttacker && _attackNum != i) _players[i].ExitGame();
        }
    }
}
