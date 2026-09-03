using UnityEngine;

/// <summary>
/// 야구공의 공기역학 / 지면 상호작용 파라미터.
///
/// 여기 정의된 가속도 공식은 <b>Ball.FixedUpdate 에서 실제 Rigidbody 에 그대로 적용</b>되고,
/// 동시에 <see cref="BallTrajectory"/> 예측기에서도 그대로 적분된다.
/// 즉 "AI 가 예측한 궤적 = 실제로 날아가는 궤적" 이 되도록 단일 진실 원천으로 유지한다.
/// </summary>
public static class BallAero
{
    /// <summary>항력 계수 k. a_drag = -k * |v| * v  (단위 1/m)</summary>
    public static float DragK = 0.0055f;

    /// <summary>백스핀 양력 계수. a_lift = L * |v| * (v × spinAxis)</summary>
    public static float LiftK = 0.0021f;

    /// <summary>양력(백스핀) 사용 여부.</summary>
    public static bool UseLift = true;

    /// <summary>지면 반발 계수(수직 속도 유지율).</summary>
    public static float Restitution = 0.42f;

    /// <summary>바운드 시 수평 속도 유지율.</summary>
    public static float BounceFriction = 0.72f;

    /// <summary>구름 마찰 계수(μ). 구르는 감속 = μ * g</summary>
    public static float RollFriction = 0.34f;

    /// <summary>이 수직 속도 미만이면 더 이상 튀지 않고 구르는 것으로 본다(m/s).</summary>
    public static float RollThreshold = 1.15f;

    /// <summary>이 속도 미만이면 정지로 본다(m/s).</summary>
    public static float StopSpeed = 0.45f;

    // ============================================================
    //  구장 규격 보정 (Park Calibration)
    // ============================================================
    //
    // 이 프로젝트의 내야는 실제 규격(베이스 간 26.6m, 마운드 17.8m)이지만
    // 외야수는 홈에서 50~58m 지점에 서 있다 — 실제(약 95~100m)보다 훨씬 얕은
    // 축소형 구장이다. 반면 Bat.cs 는 실제 MLB 타구 속도(14~46 m/s)를 만든다.
    //
    // 그대로 두면 잘 맞은 타구의 비거리가 100~126m 가 되어 <b>모든 강한 타구가
    // 외야수 키를 훌쩍 넘어가고</b>, 수비 AI 가 아무리 정확해도 잡을 수가 없다.
    // 그래서 타구가 발생하는 순간 초속에 CarrySpeedScale 을 곱해 이 구장에 맞는
    // 비거리로 압축한다. (예측 궤적과 실제 물리 모두 같은 값을 쓰므로 일관성 유지)
    //
    // ▸ 원래대로 실제 MLB 비거리를 쓰고 싶으면 ApplyCarryScaling = false 로 두면 된다.

    /// <summary>타구 비거리 압축을 사용할지.</summary>
    public static bool ApplyCarryScaling = true;

    /// <summary>비거리 비율(1.0 = 실제 MLB 비거리). AutoCalibrate 가 구장에서 측정해 설정한다.</summary>
    public static float CarryRatio = 0.58f;

    /// <summary>실제 MLB 최대 타구 비거리(m). 보정 기준값.</summary>
    public const float REFERENCE_MAX_CARRY = 126f;

    /// <summary>실제 야구에서 최대 비거리 대비 중견수 수비 위치 비율.</summary>
    public const float REFERENCE_OF_DEPTH_RATIO = 0.78f;

    /// <summary>비거리는 속도의 제곱에 비례하므로 초속에는 제곱근을 곱한다.</summary>
    public static float CarrySpeedScale
    {
        get { return ApplyCarryScaling ? Mathf.Sqrt(Mathf.Clamp(CarryRatio, 0.15f, 1f)) : 1f; }
    }

    /// <summary>
    /// 외야수가 서 있는 깊이로부터 이 구장에 맞는 비거리 비율을 산출한다.
    /// 가장 깊이 선 외야수 위치의 1/0.78 지점을 "최대 비거리" 로 잡는다.
    /// </summary>
    public static void AutoCalibrate(float deepestOutfielderDistance)
    {
        if (deepestOutfielderDistance <= 5f) return;
        float targetMaxCarry = deepestOutfielderDistance / REFERENCE_OF_DEPTH_RATIO;
        CarryRatio = Mathf.Clamp(targetMaxCarry / REFERENCE_MAX_CARRY, 0.25f, 1f);
    }

    /// <summary>
    /// 공중에 있는 공의 가속도(중력 + 항력 + 백스핀 양력).
    /// Rigidbody 에는 ForceMode.Acceleration 으로 (Accel - Physics.gravity) 만 추가한다.
    /// (중력은 Unity 가 이미 적용하므로)
    /// </summary>
    public static Vector3 Accel(Vector3 v)
    {
        Vector3 a = Physics.gravity;
        float sp = v.magnitude;
        if (sp < 0.05f) return a;

        a -= (DragK * sp) * v;

        if (UseLift)
        {
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            if (horiz.sqrMagnitude > 0.0004f)
            {
                // 순수 백스핀: 회전축은 진행 방향에 수직인 수평축
                Vector3 axis = Vector3.Cross(Vector3.up, horiz.normalized);
                // v × axis 가 위쪽을 향한다 → 떠받치는 힘
                a += (LiftK * sp) * Vector3.Cross(v, axis);
            }
        }

        return a;
    }

    /// <summary>Rigidbody 에 매 FixedUpdate 마다 더해줄 가속도(중력 제외분).</summary>
    public static Vector3 ExtraAccel(Vector3 v)
    {
        return Accel(v) - Physics.gravity;
    }

    /// <summary>지면 충돌 후 속도. 반환값이 구르는 상태면 rolling=true.</summary>
    public static Vector3 Bounce(Vector3 v, out bool rolling)
    {
        Vector3 r = v;
        r.y = -v.y * Restitution;
        r.x *= BounceFriction;
        r.z *= BounceFriction;
        rolling = r.y < RollThreshold;
        if (rolling) r.y = 0f;
        return r;
    }
}
