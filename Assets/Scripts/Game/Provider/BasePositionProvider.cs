using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

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

    [Header("Base UI Images")]
    public Image ImageFirst;
    public Image ImageSecond;
    public Image ImageThird;

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

    public PLAYER_TYPE GetAttackerPlayerType(char playerState)
    {
        if (playerState == DEFAULT_CHAR) return PLAYER_TYPE.E_PITCHER; // Default to pitcher (wildcard for current batter)
        int val = playerState - '0';
        if (val >= 20) return (PLAYER_TYPE)(val - 20);
        if (val >= 10) return (PLAYER_TYPE)(val - 10);
        return (PLAYER_TYPE)val;
    }

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
        
        UpdateBaseUI(idx, state > -1);
    }

    void UpdateBaseUI(BASE_TYPE idx, bool hasRunner)
    {
        Image targetImg = null;
        if (idx == BASE_TYPE.E_FIRST_BASE) targetImg = ImageFirst;
        else if (idx == BASE_TYPE.E_SECOND_BASE) targetImg = ImageSecond;
        else if (idx == BASE_TYPE.E_THIRD_BASE) targetImg = ImageThird;

        if (targetImg != null)
        {
            Color targetColor = Color.white;
            if (hasRunner)
            {
                ColorUtility.TryParseHtmlString("#F00", out targetColor);
            }
            targetImg.DOColor(targetColor, 0.3f);
        }
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
    }

    public void ClearBases()
    {
        for (int i = Bases.Count - 1; i >= 0; i--)
        {
            BASE_TYPE baseType = (BASE_TYPE)(i + 2);
            SetAttackerBaseState(baseType, -1);
            SetDefencerBaseState(baseType, -1);
        }
    }

    public void AdvanceRunnersForWalk()
    {
        // 만루(3루, 2루, 1루 모두 주자가 있을 때) 처리 등 연쇄 밀어내기
        bool firstOccupied = GetAttckerBaseState(BASE_TYPE.E_FIRST_BASE) > '9';
        bool secondOccupied = GetAttckerBaseState(BASE_TYPE.E_SECOND_BASE) > '9';
        bool thirdOccupied = GetAttckerBaseState(BASE_TYPE.E_THIRD_BASE) > '9';

        if (firstOccupied)
        {
            if (secondOccupied)
            {
                if (thirdOccupied)
                {
                    // 만루 밀어내기 -> 3루 주자는 홈으로 (득점 처리는 추후 고도화, 일단 3루 비움)
                    SetAttackerBaseState(BASE_TYPE.E_THIRD_BASE, -1);
                }
                // 2루 주자를 3루로
                SetAttackerBaseState(BASE_TYPE.E_THIRD_BASE, GetAttckerBaseState(BASE_TYPE.E_SECOND_BASE) - '0');
            }
            // 1루 주자를 2루로
            SetAttackerBaseState(BASE_TYPE.E_SECOND_BASE, GetAttckerBaseState(BASE_TYPE.E_FIRST_BASE) - '0');
        }
    }

    public Transform GetThrowTarget(Transform throwerTransform, PLAYER_TYPE throwerType = PLAYER_TYPE.E_PITCHER)
    {
        // 포스 아웃 가능한 베이스를 찾음 (1루부터 순서대로 - 확실한 아웃 우선)
        Transform forceOutTarget = null;
        float bestForceOutScore = float.MaxValue;
        
        // 선행 주자 중 가장 가까운(잡기 쉬운) 타겟도 탐색
        Transform leadRunnerTarget = null;
        float bestLeadRunnerScore = float.MaxValue;
        
        for(int i = 0; i < Bases.Count; i++)
        {
            BASE_TYPE baseType = (BASE_TYPE)(i + 2); // E_FIRST_BASE = 2
            char state = getBaseState(baseType)[1];
            
            // 주자가 해당 베이스로 향하고 있거나(> '9') 도착해있음
            if (state > '9' && state != DEFAULT_CHAR)
            {
                Transform targetBase = Bases[i];
                
                // 던지는 사람이 타겟 본인이면 패스
                if (Vector3.Distance(throwerTransform.position, targetBase.position) < 2.0f)
                {
                    continue; 
                }

                float throwDist = Vector3.Distance(throwerTransform.position, targetBase.position);
                
                // 주자가 달리는 중(+10 상태)인지, 이미 도착(+0 상태)인지 판별
                int stateVal = state - '0';
                bool isRunning = stateVal >= 10; // 달리는 중
                
                // 포스 아웃 점수: 송구 거리가 짧을수록 + 1루에 가까울수록 점수가 좋음
                float forceOutScore = throwDist + (i * 5f); // 1루(i=0) 우선
                
                // 달리는 중인 주자는 시간이 촉박하므로 더 높은 우선순위
                if (isRunning) forceOutScore -= 10f;
                
                // 너무 멀면 페널티
                if (throwDist > 30f && baseType != BASE_TYPE.E_FIRST_BASE)
                {
                    forceOutScore += 50f; // 매우 큰 페널티
                }
                
                if (forceOutScore < bestForceOutScore)
                {
                    bestForceOutScore = forceOutScore;
                    forceOutTarget = targetBase;
                }
                
                // 선행 주자 타겟 (3루 > 2루 > 1루 순 - 이미 진루한 주자 잡기)
                if (!isRunning && baseType > BASE_TYPE.E_FIRST_BASE)
                {
                    float leadScore = throwDist - (i * 3f); // 높은 베이스 우선
                    if (leadScore < bestLeadRunnerScore)
                    {
                        bestLeadRunnerScore = leadScore;
                        leadRunnerTarget = targetBase;
                    }
                }
            }
        }
        
        // 포스 아웃이 가능한 타겟이 있으면 우선
        if (forceOutTarget != null) return forceOutTarget;
        
        // 선행 주자 태그 아웃 가능하면 시도
        if (leadRunnerTarget != null) return leadRunnerTarget;
        
        // 아무 주자도 뛰지 않거나 마땅한 타겟이 없으면 투수에게 반환
        if (throwerType == PLAYER_TYPE.E_PITCHER) 
        {
            return throwerTransform;
        }
        else
        {
            // 투수의 Transform을 가져오기 (Team 클래스를 통해)
            Team team = throwerTransform.parent.GetComponent<Team>();
            if (team != null)
            {
                return team.GetPlayerItem(PLAYER_TYPE.E_PITCHER);
            }
            return Bases[getIndex(BASE_TYPE.E_SECOND_BASE)]; 
        }
    }
}
