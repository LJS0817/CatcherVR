using UnityEngine;

/// <summary>궤적 상의 한 표본.</summary>
public struct TrajectoryPoint
{
    public float Time;        // 예측 시작 시점부터의 경과 시간
    public Vector3 Position;
    public Vector3 Velocity;
    public bool InAir;        // 공중(아직 첫 바운드 전이거나 튀어오른 상태)
    public byte Bounces;      // 이 시점까지 지면에 닿은 횟수
}

/// <summary>
/// 타구/송구의 미래 궤적을 시뮬레이션한다.
///
/// 기존 BallProjectileCalculator 는 첫 지면 충돌에서 예측을 중단했기 때문에
/// 땅볼의 "바운드 → 구름" 구간을 전혀 알 수 없었고, 수비수는 공 뒤꽁무니만 쫓았다.
/// 이 클래스는 공중 구간(중력+항력+양력) → 바운드 → 구름 → 정지까지 전부 적분한다.
///
/// 공중 구간은 Ball 의 실제 물리와 동일한 공식(BallAero)을 쓰므로 정확하고,
/// 바운드 이후는 추정값이지만 실제 바운드가 일어날 때마다 재예측되므로
/// 실제 야수가 "바운드를 보고 다시 읽는" 것과 같은 결과가 된다.
/// </summary>
public class BallTrajectory
{
    public const float DT = 0.02f;
    public const float MAX_TIME = 9f;
    const int MAX_POINTS = (int)(MAX_TIME / DT) + 4;

    readonly TrajectoryPoint[] _points = new TrajectoryPoint[MAX_POINTS];
    int _count;

    /// <summary>궤적이 갱신될 때마다 증가. 캐시 무효화 판단용.</summary>
    public int Version { get; private set; }

    /// <summary>표본 개수.</summary>
    public int Count { get { return _count; } }

    /// <summary>첫 착지 지점(공중에서 처음 지면에 닿는 곳). 착지 없이 끝나면 마지막 지점.</summary>
    public Vector3 LandPosition { get; private set; }
    public float LandTime { get; private set; }

    /// <summary>최고점 높이 / 시각.</summary>
    public float ApexHeight { get; private set; }
    public float ApexTime { get; private set; }

    /// <summary>공이 최종적으로 멈추는(혹은 예측이 끝나는) 지점.</summary>
    public Vector3 RestPosition { get; private set; }
    public float RestTime { get; private set; }

    /// <summary>홈에서 착지 지점까지의 거리.</summary>
    public float CarryDistance { get; private set; }

    /// <summary>지면 이외의 콜라이더(펜스/백스톱 등)에 막혔는가.</summary>
    public bool HitObstacle { get; private set; }

    /// <summary>궤적이 유효한가.</summary>
    public bool IsValid { get { return _count >= 2; } }

    /// <summary>발사각(도). 땅볼/라인드라이브/플라이 분류에 사용.</summary>
    public float LaunchAngleDeg { get; private set; }

    /// <summary>초기 속도(m/s).</summary>
    public float LaunchSpeed { get; private set; }

    public void Clear()
    {
        _count = 0;
        Version++;
    }

    public TrajectoryPoint this[int i]
    {
        get { return _points[Mathf.Clamp(i, 0, Mathf.Max(0, _count - 1))]; }
    }

    static readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

    /// <summary>
    /// 궤적을 새로 계산한다.
    /// </summary>
    /// <param name="startPos">시작 위치</param>
    /// <param name="startVel">시작 속도</param>
    /// <param name="groundY">그라운드 높이</param>
    /// <param name="obstacleMask">펜스/백스톱 등 막히는 판정을 할 레이어. 0이면 검사 안 함.</param>
    /// <param name="ignoreRoot">자기 자신(공)의 콜라이더를 무시하기 위한 루트</param>
    public void Simulate(Vector3 startPos, Vector3 startVel, float groundY, int obstacleMask, Transform ignoreRoot)
    {
        _count = 0;
        HitObstacle = false;

        Vector3 pos = startPos;
        Vector3 vel = startVel;
        float t = 0f;
        byte bounces = 0;
        bool rolling = false;

        LaunchSpeed = startVel.magnitude;
        float horizSpeed = new Vector2(startVel.x, startVel.z).magnitude;
        LaunchAngleDeg = Mathf.Atan2(startVel.y, Mathf.Max(0.001f, horizSpeed)) * Mathf.Rad2Deg;

        ApexHeight = pos.y;
        ApexTime = 0f;
        LandPosition = pos;
        LandTime = 0f;
        bool landed = pos.y <= groundY + 0.02f;
        if (landed) { rolling = Mathf.Abs(vel.y) < BallAero.RollThreshold; }

        Push(t, pos, vel, !landed, bounces);

        int obstacleCheckCounter = 0;
        Vector3 lastCheckPos = pos;

        while (t < MAX_TIME && _count < MAX_POINTS - 1)
        {
            Vector3 prevPos = pos;

            if (rolling)
            {
                // ── 구름 구간 : 수평 마찰 감속 ──
                Vector3 h = new Vector3(vel.x, 0f, vel.z);
                float sp = h.magnitude;
                if (sp < BallAero.StopSpeed)
                {
                    vel = Vector3.zero;
                    pos.y = groundY;
                    Push(t, pos, vel, false, bounces);
                    break;
                }
                float decel = BallAero.RollFriction * Mathf.Abs(Physics.gravity.y);
                float newSp = Mathf.Max(0f, sp - decel * DT);
                Vector3 dir = h / sp;
                vel = dir * newSp;
                pos += vel * DT;
                pos.y = groundY;
            }
            else
            {
                // ── 공중 구간 : 중력 + 항력 + 백스핀 양력 ──
                Vector3 a = BallAero.Accel(vel);
                vel += a * DT;
                pos += vel * DT;

                if (pos.y > ApexHeight) { ApexHeight = pos.y; ApexTime = t; }

                if (pos.y <= groundY)
                {
                    // 지면과의 교점을 선형 보간으로 근사
                    float dy = prevPos.y - pos.y;
                    float frac = dy > 0.0001f ? (prevPos.y - groundY) / dy : 0f;
                    pos = Vector3.Lerp(prevPos, pos, Mathf.Clamp01(frac));
                    pos.y = groundY;

                    if (!landed)
                    {
                        landed = true;
                        LandPosition = pos;
                        LandTime = t;
                    }

                    bounces++;
                    vel = BallAero.Bounce(vel, out rolling);
                }
            }

            t += DT;
            Push(t, pos, vel, !rolling && pos.y > groundY + 0.01f, bounces);

            // ── 장애물(펜스/백스톱) 검사 : 비용을 줄이려고 일정 간격으로만 ──
            obstacleCheckCounter++;
            if (obstacleMask != 0 && obstacleCheckCounter >= 3)
            {
                obstacleCheckCounter = 0;
                Vector3 seg = pos - lastCheckPos;
                float segLen = seg.magnitude;
                if (segLen > 0.01f)
                {
                    int n = Physics.RaycastNonAlloc(lastCheckPos, seg / segLen, _hitBuffer, segLen, obstacleMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        RaycastHit h = _hitBuffer[i];
                        if (h.collider == null) continue;
                        if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
                        HitObstacle = true;
                        pos = h.point;
                        Push(t, pos, Vector3.zero, false, bounces);
                        break;
                    }
                }
                lastCheckPos = pos;
                if (HitObstacle) break;
            }
        }

        if (_count == 0) Push(0f, startPos, startVel, true, 0);

        if (!landed)
        {
            LandPosition = _points[_count - 1].Position;
            LandTime = _points[_count - 1].Time;
        }

        RestPosition = _points[_count - 1].Position;
        RestTime = _points[_count - 1].Time;
        CarryDistance = FieldMetrics.FlatDistance(LandPosition, FieldMetrics.Home);

        Version++;
    }

    void Push(float t, Vector3 p, Vector3 v, bool inAir, byte bounces)
    {
        if (_count >= MAX_POINTS) return;
        _points[_count].Time = t;
        _points[_count].Position = p;
        _points[_count].Velocity = v;
        _points[_count].InAir = inAir;
        _points[_count].Bounces = bounces;
        _count++;
    }

    /// <summary>특정 시각의 궤적 표본을 보간해서 얻는다.</summary>
    public bool TryGetAt(float time, out TrajectoryPoint result)
    {
        result = default(TrajectoryPoint);
        if (_count == 0) return false;
        if (time <= 0f) { result = _points[0]; return true; }
        if (time >= _points[_count - 1].Time) { result = _points[_count - 1]; return true; }

        int idx = Mathf.Clamp(Mathf.FloorToInt(time / DT), 0, _count - 2);
        TrajectoryPoint a = _points[idx];
        TrajectoryPoint b = _points[idx + 1];
        float span = b.Time - a.Time;
        float k = span > 0.0001f ? (time - a.Time) / span : 0f;

        result.Time = time;
        result.Position = Vector3.Lerp(a.Position, b.Position, k);
        result.Velocity = Vector3.Lerp(a.Velocity, b.Velocity, k);
        result.InAir = a.InAir;
        result.Bounces = a.Bounces;
        return true;
    }

    /// <summary>
    /// 지정한 높이 이하로 처음 내려오는 시점의 인덱스. 없으면 -1.
    /// </summary>
    public int FirstIndexBelowHeight(float height, float afterTime)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_points[i].Time < afterTime) continue;
            if (_points[i].Position.y <= height) return i;
        }
        return -1;
    }

    /// <summary>궤적을 LineRenderer 에 그린다(최대 maxPoints 개로 다운샘플링).</summary>
    public void WriteToLine(LineRenderer line, int maxPoints = 128)
    {
        if (line == null || _count == 0) return;

        int n = Mathf.Clamp(Mathf.Min(maxPoints, _count), 2, Mathf.Max(2, maxPoints));
        line.positionCount = n;

        if (_count < 2)
        {
            for (int i = 0; i < n; i++) line.SetPosition(i, _points[0].Position);
            return;
        }

        for (int i = 0; i < n; i++)
        {
            int src = Mathf.RoundToInt((float)i / (n - 1) * (_count - 1));
            line.SetPosition(i, _points[Mathf.Clamp(src, 0, _count - 1)].Position);
        }
    }

    /// <summary>표본 간격(초). WriteToLine 이후 LineRenderer 인덱스 → 시간 변환용.</summary>
    public float SampleInterval { get { return DT; } }
}
