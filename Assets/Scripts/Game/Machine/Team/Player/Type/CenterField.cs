using UnityEngine;

public class CenterField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_CENTER_FIELD;
        base.init();
        _role.SetRange(20f);
    }

    protected override void update()
    {
        base.update();
    }
}
