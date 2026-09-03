using UnityEngine;

/// <summary>
/// 조율자가 한 야수에게 내려주는 지시.
/// 기존 DEFENSIVE_ROLE + RoleTargetPosition 을 확장한 형태이며,
/// 하위 호환을 위해 BaseballPlayer 는 두 표현을 모두 유지한다.
/// </summary>
public struct FieldAssignment
{
    public DEFENSIVE_ROLE Role;

    /// <summary>이동해야 할 지점(월드).</summary>
    public Vector3 Target;

    /// <summary>BASE_COVER 일 때 담당 베이스.</summary>
    public BASE_TYPE CoverBase;

    /// <summary>내가 이 타구의 1차 처리자인가.</summary>
    public bool IsPrimary;

    /// <summary>1차 처리자를 뒤에서 받쳐주는 2차 처리자인가.</summary>
    public bool IsSecondary;

    /// <summary>1차 처리자일 때의 인터셉트 계산 결과.</summary>
    public InterceptResult Intercept;

    /// <summary>대기 중 바라볼 지점(보통 공 또는 송구가 올 방향).</summary>
    public Vector3 FaceTarget;

    /// <summary>이 지시가 유효한 상황(타구가 살아있음).</summary>
    public bool Live;

    public static FieldAssignment Idle(Vector3 home)
    {
        FieldAssignment a = new FieldAssignment();
        a.Role = DEFENSIVE_ROLE.IDLE;
        a.Target = home;
        a.CoverBase = BASE_TYPE.E_SELF;
        a.FaceTarget = FieldMetrics.Home;
        a.Intercept = InterceptResult.None(home);
        a.Live = false;
        return a;
    }
}
