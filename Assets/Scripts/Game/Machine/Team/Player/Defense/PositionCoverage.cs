using UnityEngine;

/// <summary>
/// 포지션별 기본 위치와 수비 범위 스펙. 설계도 13.1 의 <c>PositionCoverageConfig</c> 구현체.
///
/// 좌표 규약(설계도 4.2와 동일)
///   X : 좌익선 방향이 음수(-), 우익선 방향이 양수(+)   → FieldMetrics.Lateral
///   Z : 홈플레이트에서 외야 센터 방향이 양수(+)        → FieldMetrics.Depth
/// 값은 모두 <b>실제 야구장 규격(m)</b> 이며, 사용 시 구장 스케일로 환산한다.
/// </summary>
[System.Serializable]
public struct PositionCoverageConfig
{
    public PLAYER_TYPE Position;

    /// <summary>기본 수비 위치 (X=Lateral, Y=Depth), 실측 m. 설계도 4.2 배치도.</summary>
    public Vector2 DefaultSpot;

    /// <summary>1차 유효 포구 반경(실측 m). 설계도 4.3.</summary>
    public float PrimaryCatchRadius;

    /// <summary>가로(X) 커버리지 경계 (min, max), 실측 m.</summary>
    public Vector2 HorizontalRange;

    /// <summary>세로(Z) 깊이 경계 (min, max), 실측 m.</summary>
    public Vector2 DepthRange;
}

/// <summary>
/// 설계도 4.2 「필드 기준 좌표계 및 기본 위치 배치도」 +
///        4.3 「포지션별 기본 위치 및 수비 범위 스펙」 테이블.
///
/// ─────────────────────────────────────────────────────────────
/// ▣ 축소형 구장 대응
///   이 프로젝트의 내야는 실제 규격이지만 외야는 실제(95~115m)보다 훨씬 얕다.
///   그래서 <b>내야 계열 값은 그대로</b> 쓰고, <b>외야 깊이만</b> 실제 구장 대비
///   비율(<see cref="OutfieldScale"/>)로 눌러서 이 구장에 맞춘다.
///   덕분에 설계도 수치를 그대로 유지하면서도 실제 씬에서 어긋나지 않는다.
/// ─────────────────────────────────────────────────────────────
/// </summary>
public static class PositionCoverage
{
    /// <summary>설계도가 상정한 중견수 기본 깊이(m). 외야 스케일 산출 기준.</summary>
    public const float DESIGN_CF_DEPTH = 78f;

    static readonly PositionCoverageConfig[] _table = BuildTable();

    static float _outfieldScale = 1f;
    static bool _calibrated;

    /// <summary>
    /// 이 구장의 외야 깊이 비율. 1.0 이면 설계도와 동일한 정규 구장.
    /// <see cref="Calibrate"/> 로 실제 외야수 위치에서 측정한다.
    /// </summary>
    public static float OutfieldScale { get { return _outfieldScale; } }

    /// <summary>실제 씬의 가장 깊은 외야수 거리로 외야 깊이 비율을 보정한다(최초 1회).</summary>
    public static void Calibrate(float deepestOutfielderRealMeters)
    {
        if (deepestOutfielderRealMeters <= 5f) return;
        // 가장 깊은 외야수 ≒ 좌/우익수(설계도 68m) 기준으로 환산
        _outfieldScale = Mathf.Clamp(deepestOutfielderRealMeters / 68f, 0.35f, 1.4f);
        _calibrated = true;
    }

    public static bool IsCalibrated { get { return _calibrated; } }

    public static void InvalidateCalibration() { _calibrated = false; _outfieldScale = 1f; }

    // ============================================================
    //  설계도 4.3 테이블
    // ============================================================

    static PositionCoverageConfig[] BuildTable()
    {
        PositionCoverageConfig[] t = new PositionCoverageConfig[9];

        t[0] = Make(PLAYER_TYPE.E_FIRST_BASE, 18.0f, 23.5f, 7.5f, 12.0f, 22.0f, 15.0f, 31.0f);
        t[1] = Make(PLAYER_TYPE.E_SECOND_BASE, 8.5f, 33.5f, 8.5f, 1.5f, 13.5f, 24.0f, 38.0f);
        t[2] = Make(PLAYER_TYPE.E_SHORT_STOP, -8.5f, 34.5f, 9.0f, -14.5f, -1.5f, 24.0f, 39.0f);
        t[3] = Make(PLAYER_TYPE.E_THIRD_BASE, -17.5f, 22.5f, 7.0f, -22.0f, -11.5f, 14.0f, 30.0f);
        t[4] = Make(PLAYER_TYPE.E_RIGHT_FILED, 33.0f, 68.0f, 22.0f, 15.0f, 60.0f, 45.0f, 95.0f);
        t[5] = Make(PLAYER_TYPE.E_CENTER_FIELD, 0.0f, 78.0f, 26.0f, -20.0f, 20.0f, 45.0f, 115.0f);
        t[6] = Make(PLAYER_TYPE.E_LEFT_FILED, -33.0f, 68.0f, 22.0f, -60.0f, -15.0f, 45.0f, 95.0f);
        t[7] = Make(PLAYER_TYPE.E_PITCHER, 0.0f, 18.4f, 6.5f, -5.0f, 5.0f, 10.0f, 24.0f);
        t[8] = Make(PLAYER_TYPE.E_CATCHER, 0.0f, -1.5f, 4.0f, -3.0f, 3.0f, -4.0f, 4.0f);

        return t;
    }

    static PositionCoverageConfig Make(PLAYER_TYPE p, float x, float z, float radius,
                                       float xMin, float xMax, float zMin, float zMax)
    {
        PositionCoverageConfig c = new PositionCoverageConfig();
        c.Position = p;
        c.DefaultSpot = new Vector2(x, z);
        c.PrimaryCatchRadius = radius;
        c.HorizontalRange = new Vector2(xMin, xMax);
        c.DepthRange = new Vector2(zMin, zMax);
        return c;
    }

    /// <summary>포지션별 설계 스펙(실측 m 원본).</summary>
    public static PositionCoverageConfig For(PLAYER_TYPE t)
    {
        for (int i = 0; i < _table.Length; i++)
        {
            if (_table[i].Position == t) return _table[i];
        }
        return _table[5]; // 안전값 : 중견수
    }

    static bool IsOutfield(PLAYER_TYPE t)
    {
        return t == PLAYER_TYPE.E_LEFT_FILED || t == PLAYER_TYPE.E_CENTER_FIELD || t == PLAYER_TYPE.E_RIGHT_FILED;
    }

    /// <summary>깊이 값을 이 구장 크기에 맞게 환산(외야만 압축).</summary>
    static float ScaleDepth(PLAYER_TYPE t, float realMeters)
    {
        return IsOutfield(t) ? realMeters * _outfieldScale : realMeters;
    }

    // ============================================================
    //  조회 API
    // ============================================================

    /// <summary>설계도 4.2 기준 기본 수비 위치(월드). 구장 스케일이 반영된다.</summary>
    public static Vector3 DesignDefaultPosition(PLAYER_TYPE t)
    {
        PositionCoverageConfig c = For(t);
        return FieldMetrics.FromLocal(FieldMetrics.M(ScaleDepth(t, c.DefaultSpot.x)),
                                      FieldMetrics.M(ScaleDepth(t, c.DefaultSpot.y)));
    }

    /// <summary>1차 유효 포구 반경(씬 단위).</summary>
    public static float CatchRadius(PLAYER_TYPE t)
    {
        return FieldMetrics.M(ScaleDepth(t, For(t).PrimaryCatchRadius));
    }

    /// <summary>이 지점이 해당 포지션의 담당 커버리지 경계 안인가(설계도 4.3).</summary>
    public static bool Contains(PLAYER_TYPE t, Vector3 worldPos)
    {
        PositionCoverageConfig c = For(t);

        float lateral = FieldMetrics.Lateral(worldPos) / Mathf.Max(0.0001f, FieldMetrics.Scale);
        float depth = FieldMetrics.Depth(worldPos) / Mathf.Max(0.0001f, FieldMetrics.Scale);

        float xMin = ScaleDepth(t, c.HorizontalRange.x);
        float xMax = ScaleDepth(t, c.HorizontalRange.y);
        float zMin = ScaleDepth(t, c.DepthRange.x);
        float zMax = ScaleDepth(t, c.DepthRange.y);

        return lateral >= xMin && lateral <= xMax && depth >= zMin && depth <= zMax;
    }

    /// <summary>
    /// 담당 구역 우선도(0~1). 구역 중심에 가까울수록 1에 가깝다.
    /// 설계도 4.3 「중첩 구역 충돌 해결」에서 콜 우선순위의 보조 가중치로 쓴다.
    /// </summary>
    public static float Priority(PLAYER_TYPE t, Vector3 worldPos)
    {
        if (!Contains(t, worldPos)) return 0f;

        PositionCoverageConfig c = For(t);

        float lateral = FieldMetrics.Lateral(worldPos) / Mathf.Max(0.0001f, FieldMetrics.Scale);
        float depth = FieldMetrics.Depth(worldPos) / Mathf.Max(0.0001f, FieldMetrics.Scale);

        float xMin = ScaleDepth(t, c.HorizontalRange.x);
        float xMax = ScaleDepth(t, c.HorizontalRange.y);
        float zMin = ScaleDepth(t, c.DepthRange.x);
        float zMax = ScaleDepth(t, c.DepthRange.y);

        float xCenter = (xMin + xMax) * 0.5f;
        float xHalf = Mathf.Max(0.01f, (xMax - xMin) * 0.5f);
        float zCenter = (zMin + zMax) * 0.5f;
        float zHalf = Mathf.Max(0.01f, (zMax - zMin) * 0.5f);

        float dx = Mathf.Abs(lateral - xCenter) / xHalf;
        float dz = Mathf.Abs(depth - zCenter) / zHalf;

        return Mathf.Clamp01(1f - (dx * 0.5f + dz * 0.5f));
    }

    /// <summary>
    /// 이 지점이 해당 포지션의 <b>1차 유효 반경</b>(기본 위치 기준) 안인가.
    /// 설계도 4.3 의 "1차 유효 반경" 판정.
    /// </summary>
    public static bool WithinPrimaryRadius(PLAYER_TYPE t, Vector3 fielderHome, Vector3 worldPos)
    {
        return FieldMetrics.FlatDistance(fielderHome, worldPos) <= CatchRadius(t);
    }

    /// <summary>
    /// 이 포지션이 담당하는 커버리지 경계의 <b>중심점</b>(월드).
    /// 시프트가 없을 때의 이상적인 서 있을 자리이며, 백업/커버 계산의 참조점으로 쓴다.
    /// </summary>
    public static Vector3 CoverageCenter(PLAYER_TYPE t)
    {
        PositionCoverageConfig c = For(t);
        float x = (ScaleDepth(t, c.HorizontalRange.x) + ScaleDepth(t, c.HorizontalRange.y)) * 0.5f;
        float z = (ScaleDepth(t, c.DepthRange.x) + ScaleDepth(t, c.DepthRange.y)) * 0.5f;
        return FieldMetrics.FromLocal(FieldMetrics.M(x), FieldMetrics.M(z));
    }
}
