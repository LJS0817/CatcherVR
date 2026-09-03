using UnityEngine;

/// <summary>포구 자세. 설계도 8장 「포구 자세 및 송구 전환 딜레이 물리 스펙」.</summary>
public enum CATCH_POSTURE
{
    ROUTINE_STANDING,   // 정면 안정 포구
    BACKHAND,           // 역모션 포구
    FOREHAND_RUNNING,   // 러닝 캐치
    KNEE_SLIDING,       // 무릎 슬라이딩 블로킹
    FULL_DIVING,        // 완전 다이빙
    BAREHAND,           // 맨손 캐치(약한 땅볼 / 기습 번트)
    WALL_COLLISION      // 펜스 충돌 포구
}

/// <summary>자세별 지연 시간과 송구 보정치.</summary>
public struct CatchPostureSpec
{
    /// <summary>기상 / 균형 회복 시간(초).</summary>
    public float GetUpTime;
    /// <summary>공 빼기(글러브 → 송구손) 기준 시간(초).</summary>
    public float TransferTime;
    /// <summary>송구 파워 배율(1.0 = 100%). 낮을수록 구속이 느려 도착이 늦다.</summary>
    public float PowerScale;
    /// <summary>
    /// 송구 정확도 배율. AI → AI 송구는 설계도 11.2 에 따라 무오차이므로
    /// 현재 로직에서는 사용하지 않는다(플레이어 송구/난이도 확장용 값).
    /// </summary>
    public float AccuracyScale;

    /// <summary>포구 완료부터 송구 릴리스까지의 총 대기 시간(초).</summary>
    public float TotalReadyTime { get { return GetUpTime + TransferTime; } }
}

/// <summary>
/// 포구 자세 판별 및 자세별 스펙 테이블.
/// 설계도 8장의 수치를 그대로 옮겨 담았다.
///
///   Total Throw Ready Time = T_GetUp + T_Transfer ( + 송구 모션의 ArmCocking )
/// </summary>
public static class CatchPostureTable
{
    /// <summary>설계도 기준 표준 볼 빼기 시간(초). 포지션 능력치 보정의 기준값.</summary>
    public const float REFERENCE_TRANSFER = 0.35f;

    public static CatchPostureSpec For(CATCH_POSTURE p)
    {
        CatchPostureSpec s = new CatchPostureSpec();
        switch (p)
        {
            case CATCH_POSTURE.ROUTINE_STANDING:
                s.GetUpTime = 0.00f; s.TransferTime = 0.35f; s.PowerScale = 1.00f; s.AccuracyScale = 1.00f;
                break;
            case CATCH_POSTURE.BACKHAND:
                s.GetUpTime = 0.20f; s.TransferTime = 0.55f; s.PowerScale = 0.85f; s.AccuracyScale = 0.85f;
                break;
            case CATCH_POSTURE.FOREHAND_RUNNING:
                s.GetUpTime = 0.00f; s.TransferTime = 0.30f; s.PowerScale = 0.90f; s.AccuracyScale = 0.90f;
                break;
            case CATCH_POSTURE.KNEE_SLIDING:
                s.GetUpTime = 0.65f; s.TransferTime = 0.40f; s.PowerScale = 0.80f; s.AccuracyScale = 0.80f;
                break;
            case CATCH_POSTURE.FULL_DIVING:
                s.GetUpTime = 0.90f; s.TransferTime = 0.45f; s.PowerScale = 0.70f; s.AccuracyScale = 0.70f;
                break;
            case CATCH_POSTURE.BAREHAND:
                s.GetUpTime = 0.00f; s.TransferTime = 0.15f; s.PowerScale = 0.75f; s.AccuracyScale = 0.65f;
                break;
            case CATCH_POSTURE.WALL_COLLISION:
                s.GetUpTime = 1.20f; s.TransferTime = 0.50f; s.PowerScale = 0.70f; s.AccuracyScale = 0.75f;
                break;
            default:
                s.GetUpTime = 0.00f; s.TransferTime = 0.35f; s.PowerScale = 1.00f; s.AccuracyScale = 1.00f;
                break;
        }
        return s;
    }

    /// <summary>
    /// 실제 포구 순간의 상황으로부터 자세를 판별한다.
    /// </summary>
    /// <param name="action">포구 시점의 절차적 모션 상태</param>
    /// <param name="running">달리면서 잡았는가</param>
    /// <param name="ballHeight">지면 기준 공 높이(씬 단위)</param>
    /// <param name="backhand">글러브 반대편으로 뻗어 잡았는가</param>
    /// <param name="nearFence">펜스에 붙어서 잡았는가</param>
    /// <param name="weakGrounder">맨손으로 집을 만큼 약하게 구르는 공인가</param>
    public static CATCH_POSTURE Classify(FIELDER_ACTION action, bool running, float ballHeight,
                                         bool backhand, bool nearFence, bool weakGrounder)
    {
        if (nearFence) return CATCH_POSTURE.WALL_COLLISION;

        switch (action)
        {
            case FIELDER_ACTION.DIVE:
            case FIELDER_ACTION.GETUP:
                return CATCH_POSTURE.FULL_DIVING;
            case FIELDER_ACTION.SLIDE:
                return CATCH_POSTURE.KNEE_SLIDING;
            case FIELDER_ACTION.JUMP:
                // 점프 캐치는 착지 균형 회복이 필요하므로 러닝 캐치보다 조금 무겁게 본다
                return CATCH_POSTURE.BACKHAND;
        }

        if (weakGrounder && ballHeight < FieldMetrics.M(0.35f)) return CATCH_POSTURE.BAREHAND;
        if (backhand) return CATCH_POSTURE.BACKHAND;
        if (running) return CATCH_POSTURE.FOREHAND_RUNNING;
        return CATCH_POSTURE.ROUTINE_STANDING;
    }

    /// <summary>
    /// 포지션 능력치를 반영한 실제 볼 빼기 시간.
    /// 자세별 기준 시간에 (그 포지션의 볼 빼기 / 표준 0.35s) 비율을 곱한다.
    /// 포수·2루수처럼 트랜스퍼가 빠른 포지션은 어떤 자세에서도 조금 더 빠르다.
    /// </summary>
    public static float TransferTimeFor(CATCH_POSTURE p, FielderAttributes attr)
    {
        CatchPostureSpec s = For(p);
        if (attr == null) return s.TransferTime;
        float skill = Mathf.Clamp(attr.TransferTime / REFERENCE_TRANSFER, 0.6f, 1.5f);
        return s.TransferTime * skill;
    }

    /// <summary>포구 완료 → 송구 준비 완료까지의 총 시간(기상 + 볼 빼기).</summary>
    public static float ReadyTimeFor(CATCH_POSTURE p, FielderAttributes attr)
    {
        return For(p).GetUpTime + TransferTimeFor(p, attr);
    }
}
