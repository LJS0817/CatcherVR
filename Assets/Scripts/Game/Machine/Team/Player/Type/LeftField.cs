using UnityEngine;

public class LeftField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_LEFT_FILED;

        base.init();

        _role.SetRange(20f);
    }

    protected override void update()
    {
        base.update();
    }
}
