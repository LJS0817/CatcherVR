using TMPro;
using UnityEngine;

public enum COUNT_TYPE
{
    E_OUT,
    E_BALL,
    E_STRIKE,
    E_FOUL
}

public class CountsProvider : MonoBehaviour
{
    byte[] _counts;

    private static CountsProvider _provider;

    public delegate void ClippingEvent();
    public TMP_Text CountsUI;
    public Transform StrikeZone;

    public int CurrentInning { get; private set; } = 1;
    public int TeamAScore { get; private set; } = 0;
    public int TeamBScore { get; private set; } = 0;

    public static CountsProvider provider
    {
        get
        {
            return _provider;
        }
    }

    private void Awake()
    {
        _counts = new byte[3] { 0, 0, 0 };

        if (_provider != null && _provider == this) Destroy(this.gameObject);
        else _provider = this;

        updateUI();
    }

    void updateUI()
    {
        if (CountsUI != null)
        {
            CountsUI.SetText($"<b>{CurrentInning} ▲</b>   <b>{GetCount(COUNT_TYPE.E_OUT)}</b><size=80%>Out</size>   <b>{GetCount(COUNT_TYPE.E_STRIKE)}-{GetCount(COUNT_TYPE.E_BALL)}</b>");
        }
    }

    public byte GetCount(COUNT_TYPE t) { return _counts[(byte)t]; }
    
    public void IncreaseCount(COUNT_TYPE t, ClippingEvent e) 
    { 
        if (t == COUNT_TYPE.E_FOUL)
        {
            if (GetCount(COUNT_TYPE.E_STRIKE) < 2)
            {
                setCount(COUNT_TYPE.E_STRIKE, (byte)(GetCount(COUNT_TYPE.E_STRIKE) + 1), e);
            }
            return;
        }
        setCount(t, (byte)(GetCount(t) + 1), e); 
    }

    void setCount(COUNT_TYPE t, byte v, ClippingEvent e)
    {
        if (t == COUNT_TYPE.E_FOUL) return;
        _counts[(int)t] = v;

        // [수정] 콜백을 먼저 실행한다.
        // 예전에는 ClippingCount(3아웃 → 이닝 교체 → 타자 교체 → ResetPlay)가 먼저 돌고
        // 그 뒤에 콜백(주자 아웃 처리 · ExitGame)이 실행되어,
        // 새로 타석에 들어온 타자가 곧바로 비활성화되는 순서 역전이 발생했다.
        if (e != null) e();

        ClippingCount(t, e);
        updateUI();
    }

    public void ResetAtBatCounts()
    {
        _counts[(int)COUNT_TYPE.E_BALL] = 0;
        _counts[(int)COUNT_TYPE.E_STRIKE] = 0;
        updateUI();
    }

    public void ResetInningCounts()
    {
        _counts[(int)COUNT_TYPE.E_BALL] = 0;
        _counts[(int)COUNT_TYPE.E_STRIKE] = 0;
        _counts[(int)COUNT_TYPE.E_OUT] = 0;
        updateUI();
    }

    public void NextInning()
    {
        CurrentInning++;
        ResetInningCounts();
    }

    public void AddSimulatedRuns(int teamARuns, int teamBRuns)
    {
        TeamAScore += teamARuns;
        TeamBScore += teamBRuns;
        updateUI();
    }

    public void ClippingCount(COUNT_TYPE t, ClippingEvent e)
    {
        if (t == COUNT_TYPE.E_BALL && GetCount(t) > 3) {
            // 4 Balls = Walk
            ResetAtBatCounts();
            if (GamePlayerProvider.provider != null) GamePlayerProvider.provider.Walk();
        }
        else if (t == COUNT_TYPE.E_STRIKE && GetCount(t) > 2)
        {
            // 3 Strikes = Strikeout
            ResetAtBatCounts();
            IncreaseCount(COUNT_TYPE.E_OUT, null);
            if (GamePlayerProvider.provider != null) GamePlayerProvider.provider.Strikeout();
        }
        else if (t == COUNT_TYPE.E_OUT && GetCount(t) > 2)
        {
            // 3 Outs = Inning Change
            if (GamePlayerProvider.provider != null) GamePlayerProvider.provider.ChangeInning();
        }
    }

    public bool ContainsStrikeZone(Vector3 pos)
    {
        return Mathf.Abs(pos.x) < StrikeZone.localScale.x &&
            (StrikeZone.localScale.y + StrikeZone.position.y > pos.y &&
            StrikeZone.localScale.y - StrikeZone.position.y + 0.05f < pos.y);
    }
}
