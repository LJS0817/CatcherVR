using UnityEngine;

public class FirstBase : BaseballPlayer
{
    protected override void init(BASE_TYPE bT = BASE_TYPE.E_SELF)
    {
        _type = PLAYER_TYPE.E_FIRST_BASE;
        base.init(BASE_TYPE.E_FIRST_BASE);
    }

    public override DefenseZone GetDefenseZone()
    {
        return DefenseZone.CreateFirstBase();
    }

    // 역할 배정(베이스 커버 / 백업 / 중계)은 FieldingCoordinator 가 상황을 보고 결정한다.
    // 예전의 포지션별 if-else 체인은 제거되었다.
}
