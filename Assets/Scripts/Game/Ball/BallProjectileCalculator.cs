using UnityEngine;

/// <summary>
/// 공의 미래 궤적을 계산하고 LineRenderer / 낙구 마커에 반영한다.
///
/// [변경점]
/// 예전 구현은 0.1초 간격으로 중력만 적분하다가 <b>첫 Raycast 충돌에서 예측을 중단</b>했다.
/// 그래서 땅볼은 "첫 바운드 지점"까지밖에 알 수 없었고, 수비수는 그 뒤 구르는 공을
/// 뒤에서 쫓아가야 했다. 이제 <see cref="BallTrajectory"/> 가
/// 공중(중력+항력+백스핀) → 바운드 → 구름 → 정지까지 전 구간을 시뮬레이션한다.
/// 같은 공식을 Ball 의 실제 물리에도 적용하므로 예측과 실제가 일치한다.
/// </summary>
public class BallProjectileCalculator : MonoBehaviour
{
    Transform _hitSprite;
    bool _spriteSearched;

    readonly BallTrajectory _trajectory = new BallTrajectory();

    /// <summary>가장 최근에 계산된 궤적. 수비 AI 의 단일 입력원.</summary>
    public BallTrajectory Trajectory { get { return _trajectory; } }

    Transform HitSprite
    {
        get
        {
            if (!_spriteSearched)
            {
                _spriteSearched = true;
                GameObject go = GameObject.Find("Point");
                _hitSprite = go != null ? go.transform : null;
            }
            return _hitSprite;
        }
    }

    /// <summary>기존 호출부 호환 시그니처. 낙구 마커를 표시한다.</summary>
    public Vector3 Calculate(Vector3 vel, float mass, LineRenderer line)
    {
        return Calculate(vel, mass, line, true);
    }

    /// <param name="showMarker">낙구 지점 마커를 띄울지(투구/송구에는 띄우지 않는다).</param>
    public Vector3 Calculate(Vector3 vel, float mass, LineRenderer line, bool showMarker)
    {
        FieldMetrics.Refresh();
        float groundY = FieldMetrics.Ready ? FieldMetrics.GroundY : GuessGroundY();

        _trajectory.Simulate(transform.position, vel, groundY, 0, transform);

        if (line != null) _trajectory.WriteToLine(line, 128);

        Vector3 land = _trajectory.LandPosition;
        land.y += 0.001f;

        Transform sprite = HitSprite;
        if (sprite != null)
        {
            sprite.gameObject.SetActive(showMarker);
            if (showMarker) sprite.position = land;
        }

        return land;
    }

    float GuessGroundY()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out hit, 60f,
                            ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return 0f;
    }

    public void init()
    {
        Transform sprite = HitSprite;
        if (sprite != null) sprite.gameObject.SetActive(false);
    }

    /// <summary>궤적 선과 마커를 즉시 숨긴다(포구 시 등).</summary>
    public void Clear(LineRenderer line)
    {
        _trajectory.Clear();
        if (line != null) line.positionCount = 0;
        Transform sprite = HitSprite;
        if (sprite != null) sprite.gameObject.SetActive(false);
    }
}
