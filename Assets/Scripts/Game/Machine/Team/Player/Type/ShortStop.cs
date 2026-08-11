using UnityEngine;

public class ShortStop : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_SHORT_STOP;
        base.init(bT);
    }

    protected override void update()
    {
        base.update();
    }
}
