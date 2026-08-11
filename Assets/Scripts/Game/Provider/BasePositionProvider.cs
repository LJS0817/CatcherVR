using System.Collections.Generic;
using UnityEngine;

public class BasePositionProvider : MonoBehaviour
{
    //Base State == Transform.name
    // *     *
    //수비  공격
    // ex)
    // 1_  ->  1루수가 수비 중, 공격자 없음
    // 10  ->  1루수가 수비 중, 공격자 가는 중
    // 11  ->  1루수가 수비 중, 공격자 도착
    // 12  ->  1루수가 수비 중, 공격자가 있지만 베이스에는 없음
    // 3_  ->  3루수가 수비 중, 공격자 없음
    // _0  ->  수비수 없음, 공격자 가는 중

    // _, 0, 1, 2, 3, 4, 8
    // _ -> 없음
    // 0 -> 포수
    // 1 -> 1루수
    // 2 -> 2루수
    // 3 -> 유격수
    // 4 -> 3루수
    // 8 -> 투수
    public List<Transform> Bases;
    public List<Animator> BaseAnimators;
    public Transform HitterBox;

    private static BasePositionProvider _provider;

    const char DEFAULT_CHAR = '_';

    public static BasePositionProvider provider
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

        for (int i = Bases.Count - 1; i >= 0; i--)
        {
            setBaseState((BASE_TYPE)(i + 2), DEFAULT_CHAR, DEFAULT_CHAR);
        }
    }

    int getIndex(BASE_TYPE t) { return (int)t - (int)BASE_TYPE.E_FIRST_BASE; }

    char getState(int s) { return s > -1 ? ((char)('0' + s)) : DEFAULT_CHAR; }

    public Vector3 GetBasePosition(BASE_TYPE idx)
    {
        Vector3 rst = Bases[getIndex(idx)].position;
        rst.y = 0;

        return rst;
    }

    public BASE_TYPE GetNextBase(BASE_TYPE idx)
    {
        if (idx == BASE_TYPE.E_SELF) return BASE_TYPE.E_FIRST_BASE;
        else if(idx != BASE_TYPE.E_HOME_BASE) return idx + 1;
        return idx;
    }

    void setBaseState(BASE_TYPE i, char dS, char aS)
    {
        int index = getIndex(i);
        Bases[index].name = dS.ToString() + aS.ToString();
    }

    string getBaseState(BASE_TYPE idx)
    {
        return Bases[getIndex(idx)].name;
    }

    public char GetAttckerBaseState(BASE_TYPE idx) { return getBaseState(idx)[1]; }
    public char GetDefencerBaseState(BASE_TYPE idx) { return getBaseState(idx)[0]; }

    /// <summary>
    /// <para>-1  ->  공격자 없음</para>
    /// <para>Player Type + 0  ->  공격자 도착</para>
    /// <para>Player Type + 10  ->  공격자 가는 중</para>
    /// <para>Player Type + 20  ->  공격자 떨어져있음</para>
    /// </summary>
    /// <param name="idx"></param>
    /// <param name="state"></param>
    public void SetAttackerBaseState(BASE_TYPE idx, int state)
    {
        setBaseState(idx, getBaseState(idx)[0], getState(state));
        if (idx > BASE_TYPE.E_BALL && idx < BASE_TYPE.E_HOME_BASE) BaseAnimators[(int)(idx - BASE_TYPE.E_FIRST_BASE)].SetBool("Changed", state > -1 && state < 10);
        //int index = getIndex(idx);
        //Bases[index].name = Bases[index].name[0] + getState(state);
    }

    /// <summary>
    /// <para>-1  ->  수비수 없음</para>
    /// <para>0  ->  포수</para>
    /// <para>1  ->  1루수</para>
    /// <para>2  ->  2루수</para>
    /// <para>3  ->  유격수</para>
    /// <para>4  ->  3루수</para>
    /// <para>8  ->  투수</para>
    /// </summary>
    /// <param name="idx"></param>
    /// <param name="state"></param>
    public void SetDefencerBaseState(BASE_TYPE idx, int state)
    {
        setBaseState(idx, getState(state), getBaseState(idx)[1]);
        /*int index = getIndex(idx);
        Bases[index].name = getState(state) + Bases[index].name[1];*/
    }

    public PLAYER_TYPE GetThrowTarget()
    {
        for(int i = Bases.Count - 1; i >= 0; i--)
        {
            char state = getBaseState((BASE_TYPE)(i + 2))[1];
            if (state > '9')
            {
                if (state == DEFAULT_CHAR) continue;
                Debug.Log((PLAYER_TYPE)((int)state - ('0' + 10)));
                return (PLAYER_TYPE)((int)state - ('0' + 10));
            }
        }
        return PLAYER_TYPE.E_PITCHER;
    }
}
