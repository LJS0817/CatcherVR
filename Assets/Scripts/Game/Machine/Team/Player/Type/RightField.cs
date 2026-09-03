using UnityEngine;

public class RightField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_RIGHT_FILED;
        base.init();
        _role.SetRange(25f);
    }

    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateRightField();
    }
}
