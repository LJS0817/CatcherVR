using UnityEngine;

public class CenterField : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_CENTER_FIELD;
        base.init();
        _role.SetRange(30f);   // 중견수는 가장 넓은 수비 반경 (콜 우선순위 최상위)
    }

    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateCenterField();
    }
}
