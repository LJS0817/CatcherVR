using UnityEngine;

public class SecondBase : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_SECOND_BASE;
        base.init(BASE_TYPE.E_SECOND_BASE);
    }

    protected override void update()
    {
        base.update();
    }
}
