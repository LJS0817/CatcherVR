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

        CountsUI.SetText("<b>" + GetCount(COUNT_TYPE.E_OUT) + "</b><size=80%>Out</size>   <b>" + GetCount(COUNT_TYPE.E_BALL) + "-" + GetCount(COUNT_TYPE.E_STRIKE) + "</b>");
    }

    public byte GetCount(COUNT_TYPE t) { return _counts[(byte)t]; }
    
    public void IncreaseCount(COUNT_TYPE t, ClippingEvent e) { setCount(t, (byte)(GetCount(t) + 1), e); }

    void setCount(COUNT_TYPE t, byte v, ClippingEvent e)
    {
        if (t == COUNT_TYPE.E_FOUL) {
            if(GetCount(COUNT_TYPE.E_STRIKE) > 2) return;
            else t = COUNT_TYPE.E_STRIKE;
        }
        _counts[(int)t] = v;
        ClippingCount(t, e);
        if (e != null) e();
        CountsUI.SetText("<b>" + GetCount(COUNT_TYPE.E_OUT) + "</b><size=80%>Out</size>   <b>" + GetCount(COUNT_TYPE.E_BALL) + "-" + GetCount(COUNT_TYPE.E_STRIKE) + "</b>");
    }

    public void ClippingCount(COUNT_TYPE t, ClippingEvent e)
    {
        if (t == COUNT_TYPE.E_BALL && GetCount(t) > 4) {
            setCount(t, 0, null);
        }
        else if (GetCount(t) > 3)
        {
            setCount(t, 0, null);
            if (t == COUNT_TYPE.E_STRIKE) { 
                IncreaseCount(COUNT_TYPE.E_OUT, null);
            }
        }
    }

    public bool ContainsStrikeZone(Vector3 pos)
    {
        return Mathf.Abs(pos.x) < StrikeZone.localScale.x &&
            (StrikeZone.localScale.y + StrikeZone.position.y > pos.y &&
            StrikeZone.localScale.y - StrikeZone.position.y + 0.05f < pos.y);
    }
}
