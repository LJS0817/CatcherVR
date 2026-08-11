using UnityEngine;

public enum PITCHING_MODE
{
    E_FOUR_SEAM,
    E_RISING_FOUR_SEAM,
    E_TWO_SEAM,
    E_SINKER,
    E_CUTTER,

    E_SLOW_CURVE,
    E_CURVE,

    E_CHANGE_UP,

    E_SLIDER,

    //E_KNUCKLE,

    E_SPLITTER,
    E_LENGTH,
}

public class BallData : MonoBehaviour
{
    static readonly string[] names = {
        "Four\nseam",
        "Rising\nFour\nseam",
        "Two\nseam",
        "Sinker",
        "Cutter",
        "Slow\nCurve",
        "Curve",
        "Change\nUp",
        "Slider",
        "Splitter",
    };
    const float _baseAngle = Mathf.PI * 2 / (int)PITCHING_MODE.E_LENGTH;

    public PITCHING_MODE _type;

    private void Start()
    {
        _type = PITCHING_MODE.E_FOUR_SEAM;
    }

    public PITCHING_MODE PointToType(Vector2 pos)
    {
        float angle = Mathf.Atan2(pos.y, pos.x) + Mathf.PI;
        return (PITCHING_MODE)(((angle / _baseAngle) + 3) % 10);
    }

    public bool isValidType(PITCHING_MODE type)
    {
        return type != _type;
    }

    public string getText(int idx)
    {
        return names[idx];
    }

    public void setType(PITCHING_MODE mode)
    {
        if(_type != mode)_type = mode;
    }

    public PITCHING_MODE getType() { return _type; }
}
