using UnityEngine;

/// <summary>
/// 야구장 기하 정보를 런타임에 측정해서 제공하는 정적 헬퍼.
///
/// 하드코딩된 월드 좌표(z &gt; 25f 같은)를 쓰지 않고, 실제 베이스 Transform 으로부터
/// 홈-2루 축(Forward), 홈-1루 축(Right), 베이스 간 거리(BasePath)를 매번 측정한다.
/// 덕분에 필드를 회전시키거나 스케일을 바꿔도 수비 로직이 그대로 동작한다.
///
/// 좌표 규약 (DefenseZone 과 동일):
///   Depth   : 홈 → 2루 방향 거리 (센터 방향이 +)
///   Lateral : 홈 → 1루 방향이 + (우측), 3루 방향이 - (좌측)
///   SprayAngle : 홈→2루 를 0°, 좌측(3루/좌익수)이 +, 우측(1루/우익수)이 -
/// </summary>
public static class FieldMetrics
{
    /// <summary>실제 MLB 규격 베이스 간 거리(m). 씬 스케일 산출 기준값.</summary>
    public const float REAL_BASE_PATH = 27.43f;

    public static bool Ready { get; private set; }

    public static Vector3 Home { get; private set; }
    public static Vector3 First { get; private set; }
    public static Vector3 Second { get; private set; }
    public static Vector3 Third { get; private set; }

    /// <summary>홈 → 2루(센터) 단위 벡터. y=0.</summary>
    public static Vector3 Forward { get; private set; }
    /// <summary>홈 → 1루(우측) 단위 벡터. y=0.</summary>
    public static Vector3 Right { get; private set; }

    /// <summary>이 씬에서 측정된 베이스 간 거리.</summary>
    public static float BasePath { get; private set; }
    /// <summary>실제 야구장 대비 축소 비율. 실측 거리를 씬 단위로 변환할 때 곱한다.</summary>
    public static float Scale { get; private set; }
    /// <summary>그라운드 높이(홈 플레이트 y).</summary>
    public static float GroundY { get; private set; }

    static int _lastRefreshFrame = -1;

    static FieldMetrics()
    {
        Forward = Vector3.forward;
        Right = Vector3.right;
        BasePath = REAL_BASE_PATH;
        Scale = 1f;
        GroundY = 0f;
        Ready = false;
    }

    /// <summary>프레임당 한 번만 실제로 다시 측정한다.</summary>
    public static void Refresh(bool force = false)
    {
        if (!force && _lastRefreshFrame == Time.frameCount) return;
        _lastRefreshFrame = Time.frameCount;

        BasePositionProvider bp = BasePositionProvider.provider;
        if (bp == null || bp.Bases == null || bp.Bases.Count < 4) return;

        Home = bp.GetBasePosition(BASE_TYPE.E_HOME_BASE);
        First = bp.GetBasePosition(BASE_TYPE.E_FIRST_BASE);
        Second = bp.GetBasePosition(BASE_TYPE.E_SECOND_BASE);
        Third = bp.GetBasePosition(BASE_TYPE.E_THIRD_BASE);

        Vector3 fwd = Second - Home;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return;
        Forward = fwd.normalized;
        Right = Vector3.Cross(Vector3.up, Forward).normalized;

        // 1루가 Right 반대편에 잡혀 있다면(필드 미러링) 축을 뒤집어 준다.
        Vector3 toFirst = First - Home;
        toFirst.y = 0f;
        if (Vector3.Dot(toFirst, Right) < 0f) Right = -Right;

        float path = toFirst.magnitude;
        if (path > 0.5f) BasePath = path;

        Scale = Mathf.Max(0.05f, BasePath / REAL_BASE_PATH);

        Transform homeT = bp.Bases[3];
        GroundY = homeT != null ? homeT.position.y : 0f;

        Ready = true;
    }

    /// <summary>실측(실제 야구장) 거리를 이 씬의 단위로 변환.</summary>
    public static float M(float realMeters)
    {
        Refresh();
        return realMeters * Scale;
    }

    public static Vector3 BasePos(BASE_TYPE b)
    {
        Refresh();
        switch (b)
        {
            case BASE_TYPE.E_FIRST_BASE: return First;
            case BASE_TYPE.E_SECOND_BASE: return Second;
            case BASE_TYPE.E_THIRD_BASE: return Third;
            case BASE_TYPE.E_HOME_BASE: return Home;
            default: return Home;
        }
    }

    /// <summary>홈 기준 센터 방향 깊이.</summary>
    public static float Depth(Vector3 p)
    {
        Refresh();
        Vector3 d = p - Home;
        d.y = 0f;
        return Vector3.Dot(d, Forward);
    }

    /// <summary>홈 기준 좌우 오프셋. + 가 1루(우측).</summary>
    public static float Lateral(Vector3 p)
    {
        Refresh();
        Vector3 d = p - Home;
        d.y = 0f;
        return Vector3.Dot(d, Right);
    }

    public static float DistFromHome(Vector3 p)
    {
        Refresh();
        Vector3 d = p - Home;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>홈→2루를 0°로 하는 타구 방향각. + 가 좌측(3루/좌익수), - 가 우측.</summary>
    public static float SprayAngle(Vector3 p)
    {
        Refresh();
        Vector3 d = p - Home;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) return 0f;
        return Mathf.Atan2(-Vector3.Dot(d, Right), Vector3.Dot(d, Forward)) * Mathf.Rad2Deg;
    }

    /// <summary>파울 라인 안쪽인지 판정 (홈 기준 ±45°, 홈 뒤쪽은 파울).</summary>
    public static bool IsFair(Vector3 p)
    {
        Refresh();
        if (Depth(p) < 0.2f) return false;
        return Mathf.Abs(SprayAngle(p)) <= 45.5f;
    }

    /// <summary>필드 로컬 (lateral, depth) 좌표를 월드 좌표로 변환.</summary>
    public static Vector3 FromLocal(float lateral, float depth)
    {
        Refresh();
        Vector3 p = Home + Right * lateral + Forward * depth;
        p.y = GroundY;
        return p;
    }

    /// <summary>홈 기준 각도(도)와 거리로 월드 좌표를 만든다. 각도 + 가 좌측.</summary>
    public static Vector3 FromPolar(float angleDeg, float distance)
    {
        Refresh();
        float rad = angleDeg * Mathf.Deg2Rad;
        return FromLocal(-Mathf.Sin(rad) * distance, Mathf.Cos(rad) * distance);
    }

    /// <summary>수평 거리(y 무시).</summary>
    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static Vector3 Flat(Vector3 p)
    {
        Refresh();
        return new Vector3(p.x, GroundY, p.z);
    }
}
