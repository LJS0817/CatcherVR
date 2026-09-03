using UnityEngine;

/// <summary>
/// 수비 AI 의 모든 튜닝 상수를 한 곳에 모아둔 클래스.
/// 값은 "실제 야구장 기준(m, s)"으로 적고, 거리 계열은 FieldMetrics.M() 으로
/// 씬 스케일에 맞춰 변환해서 사용한다.
/// </summary>
public static class DefenseTuning
{
    // ────────────────────────────── 판단 주기 ──────────────────────────────

    /// <summary>수비 역할 재평가 주기(초). 공이 살아있는 동안 이 간격으로 다시 계산.</summary>
    public const float ROLE_REEVALUATE_INTERVAL = 0.12f;

    /// <summary>1차 처리자(Primary)를 교체하려면 이만큼은 더 빨리 도달할 수 있어야 한다(초). 깜빡임 방지.</summary>
    public const float PRIMARY_SWITCH_HYSTERESIS = 0.30f;

    /// <summary>도달 시간이 이 차이 이내면 "동등"으로 보고 콜 우선순위로 결정(초).</summary>
    public const float CALLOFF_TIE_WINDOW = 0.28f;

    // ────────────────────────────── 포구 ──────────────────────────────

    /// <summary>다이빙을 시도해도 되는 최대 시간 부족량(초).</summary>
    public const float DIVE_TIME_WINDOW = 0.42f;

    /// <summary>점프 캐치를 시도할 수 있는 최대 시간 부족량(초).</summary>
    public const float JUMP_TIME_WINDOW = 0.22f;

    /// <summary>공이 이 거리 안에 들어오면 포구 액션 판단 시작(실측 m).</summary>
    public const float FIELD_ACTION_RANGE = 3.2f;

    /// <summary>글러브가 공을 잡았다고 판정하는 거리(실측 m).</summary>
    public const float CATCH_SNAP_DISTANCE = 0.30f;

    /// <summary>글러브를 공 쪽으로 마중 보내기 시작하는 거리(실측 m).</summary>
    public const float GLOVE_REACH_RANGE = 2.6f;

    // ────────────────────────────── 송구 ──────────────────────────────

    /// <summary>포구 후 송구까지 최소 대기(초). 발 세팅 시간과 별개의 최소 텀.</summary>
    public const float MIN_SECURE_TIME = 0.06f;

    /// <summary>수비수가 베이스를 직접 밟으러 갈지 판단하는 최대 거리(실측 m).</summary>
    public const float RUN_TO_BASE_MAX_DIST = 6.5f;

    /// <summary>태그 아웃이 가능한 거리(실측 m).</summary>
    public const float TAG_RANGE = 1.6f;

    /// <summary>베이스를 밟았다고 인정하는 거리(실측 m).</summary>
    public const float BASE_TOUCH_RANGE = 0.9f;

    /// <summary>주자가 이 시간 이상 여유로 세이프면 송구 포기(초).</summary>
    public const float THROW_GIVE_UP_MARGIN = -0.05f;

    /// <summary>송구가 이 거리를 넘으면 중계(컷오프) 사용(실측 m).</summary>
    public const float RELAY_TRIGGER_DISTANCE = 62f;

    /// <summary>중계맨은 송구 라인 위 이 비율 지점에 선다(외야수 기준).</summary>
    public const float RELAY_LINE_RATIO = 0.42f;

    /// <summary>수구자가 베이스에서 이 거리보다 멀면 베이스 위 허공으로 송구(실측 m).</summary>
    public const float RECEIVER_AT_BASE_TOLERANCE = 1.8f;

    /// <summary>송구 도착점 높이(가슴 높이, 실측 m).</summary>
    public const float THROW_TARGET_HEIGHT = 1.35f;

    // ────────────────────────────── 진로/루트 ──────────────────────────────

    /// <summary>플라이볼 낙구 지점에 미리 도착해서 대기할 여유 시간(초).</summary>
    public const float CAMP_UNDER_BALL_SLACK = 0.35f;

    /// <summary>땅볼을 처리할 때 송구 방향으로 몸이 향하도록 우회하는 각도 가중치.</summary>
    public const float GROUNDER_ROUNDING_WEIGHT = 0.55f;

    /// <summary>우회 경로를 만드는 최대 오프셋(실측 m).</summary>
    public const float GROUNDER_ROUNDING_MAX = 2.6f;

    /// <summary>목표 지점에 이 거리 안이면 도착으로 간주(실측 m).</summary>
    public const float ARRIVE_DISTANCE = 0.45f;

    // ────────────────────────────── 백업 / 커버 ──────────────────────────────

    /// <summary>송구 라인 연장선상 백업 거리(실측 m).</summary>
    public const float BACKUP_BEHIND_BASE = 11f;

    /// <summary>타구 처리자 뒤쪽 트레일 백업 거리(실측 m).</summary>
    public const float TRAIL_BACKUP_DISTANCE = 9f;

    /// <summary>외야 펜스 추정 거리(실측 m). 백업 위치가 필드를 벗어나지 않게 클램프.</summary>
    public const float OUTFIELD_FENCE_DISTANCE = 104f;

    // ────────────────────────────── 주자 ──────────────────────────────

    /// <summary>주자 평균 최고 속도(실측 m/s).</summary>
    public const float RUNNER_TOP_SPEED = 7.6f;

    /// <summary>주자 가속도(실측 m/s^2).</summary>
    public const float RUNNER_ACCEL = 4.2f;

    // ────────────────────────────── 모션 ──────────────────────────────

    public const float RUN_BOB_HEIGHT = 0.055f;
    public const float RUN_BOB_FREQUENCY = 2.05f;
    public const float RUN_LEAN_MAX = 16f;
    public const float RUN_ROLL_MAX = 9f;
    public const float BODY_TURN_SPEED = 11f;
    public const float DIVE_DURATION = 0.62f;
    public const float DIVE_GETUP_DURATION = 0.75f;
    public const float JUMP_DURATION = 0.72f;
    public const float CROW_HOP_DURATION = 0.30f;
}
