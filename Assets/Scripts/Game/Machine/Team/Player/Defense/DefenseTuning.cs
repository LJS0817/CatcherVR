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

    /// <summary>
    /// 설계도 11.1 「캐치 반경 내 100% 포구 보장」.
    /// 어떤 상태(베이스 커버 · 이동 중 · 백업)에서든 공과의 3D 거리가 이 안이면 무조건 포구한다(실측 m).
    /// </summary>
    public const float GUARANTEED_CATCH_RADIUS = 1.2f;

    /// <summary>1루수처럼 미트가 크고 스트레치 리치가 긴 포지션의 캐치 반경(실측 m).</summary>
    public const float GUARANTEED_CATCH_RADIUS_LONG = 1.5f;

    /// <summary>다이빙 / 슬라이딩 / 스트레치 중 일시 확장되는 캐치 반경(실측 m).</summary>
    public const float GUARANTEED_CATCH_RADIUS_EXTENDED = 1.8f;

    /// <summary>맨손 캐치로 볼 만큼 약하게 구르는 공의 속도 상한(실측 m/s).</summary>
    public const float BAREHAND_BALL_SPEED = 6.0f;

    /// <summary>펜스 충돌 포구로 판정하는 펜스와의 거리(실측 m).</summary>
    public const float WALL_CONTACT_DISTANCE = 2.5f;

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

    /// <summary>진루 억제 송구를 포기하는 여유 하한(초). 이보다 늦으면 던지지 않고 플레이를 접는다.</summary>
    public const float SUPPRESS_THROW_MIN_MARGIN = -0.60f;

    /// <summary>설계도 12.1 : 모든 베이스에 대해 이 이상 늦으면 "더 이상 아웃 불가"로 본다(초).</summary>
    public const float NO_OUT_POSSIBLE_MARGIN = -0.50f;

    // ────────────────────────────── 투수 반환 (설계도 12.2) ──────────────────────────────

    /// <summary>외야수 → 내야수 1차 중계 송구 속도(실측 m/s).</summary>
    public const float RETURN_RELAY_SPEED = 24f;

    /// <summary>내야수 → 투수 부드러운 전달 속도(실측 m/s).</summary>
    public const float RETURN_GENTLE_SPEED = 16f;

    /// <summary>이 거리 이상이면 내야수도 투수에게 직접 던지지 않고 중계를 거친다(실측 m).</summary>
    public const float RETURN_DIRECT_MAX_DISTANCE = 55f;

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

    /// <summary>
    /// 송구 라인 연장선상 백업 거리(실측 m).
    /// 설계도 7.2 : BackupDistance 는 통상 12~18m — 그 중앙값을 쓴다.
    /// </summary>
    public const float BACKUP_BEHIND_BASE = 15f;

    /// <summary>설계도 7.2 백업 거리의 허용 범위(실측 m).</summary>
    public const float BACKUP_DISTANCE_MIN = 12f;
    public const float BACKUP_DISTANCE_MAX = 18f;

    /// <summary>
    /// 설계도 7.2 허용 각도 — 백업 요원은 송구선 연장선 기준 ±15° 부채꼴 안에 있어야 한다.
    /// </summary>
    public const float BACKUP_TOLERANCE_ANGLE = 15f;

    /// <summary>타구 처리자 뒤쪽 트레일 백업 거리(실측 m).</summary>
    public const float TRAIL_BACKUP_DISTANCE = 9f;

    /// <summary>외야 펜스 추정 거리(실측 m). 백업 위치가 필드를 벗어나지 않게 클램프.</summary>
    public const float OUTFIELD_FENCE_DISTANCE = 104f;

    // ────────────────────────────── 주자 ──────────────────────────────

    /// <summary>주자 평균 최고 속도(실측 m/s).</summary>
    public const float RUNNER_TOP_SPEED = 7.6f;

    /// <summary>주자 가속도(실측 m/s^2).</summary>
    public const float RUNNER_ACCEL = 4.2f;

    /// <summary>이 속도 미만이면 주자가 정지한 것으로 본다(실측 m/s). 설계도 12.1.</summary>
    public const float RUNNER_STOPPED_SPEED = 0.2f;

    // ────────────────────────────── 아웃 SOP (설계도 10장) ──────────────────────────────

    /// <summary>포스 아웃 시 베이스 중앙을 피해 안쪽 모서리를 밟는 오프셋(실측 m).</summary>
    public const float FORCE_OUT_EDGE_OFFSET = 0.32f;

    /// <summary>1루수 등이 송구 방향으로 몸을 뻗는 스트레치 전진량(실측 m).</summary>
    public const float FORCE_OUT_STRETCH = 0.80f;

    /// <summary>태그 아웃 시 베이스 앞에 스트래들로 자리잡는 거리(실측 m).</summary>
    public const float TAG_STRADDLE_OFFSET = 0.85f;

    /// <summary>스윕 태그 후 미트를 붙여두는 링거 타임(초).</summary>
    public const float TAG_LINGER_TIME = 0.20f;

    // ────────────────────────────── 플레이어(포수) 연동 ──────────────────────────────

    /// <summary>포수가 홈에서 이만큼 벗어나면 "홈을 비웠다"로 판정한다(실측 m).</summary>
    public const float CATCHER_AWAY_FROM_HOME = 3.5f;

    /// <summary>
    /// 홈 근처 타구에서 플레이어(포수)에게 양보하는 반경(실측 m).
    /// 이 안쪽이고 포수가 더 빨리 닿을 수 있으면 AI 는 길을 비킨다(설계도 3.2-5).
    /// </summary>
    public const float PLAYER_YIELD_RADIUS = 16f;

    /// <summary>플레이어가 이 정도 시간 차 이내로 먼저 닿을 수 있으면 양보한다(초).</summary>
    public const float PLAYER_YIELD_WINDOW = 0.30f;

    // ────────────────────────────── 플레이 종료 / 페일세이프 (설계도 12장) ──────────────────────────────

    /// <summary>상황 종료 후 기본 위치로 걸어서 복귀하는 속도(실측 m/s).</summary>
    public const float RESET_WALK_SPEED = 3.0f;

    /// <summary>복귀 완료로 인정하는 반경(실측 m).</summary>
    public const float READY_ARRIVE_RADIUS = 0.5f;

    /// <summary>타격 후 이 시간이 지나도 플레이가 끝나지 않으면 강제 종료(초).</summary>
    public const float MAX_PLAY_TIME = 8.0f;

    /// <summary>주자가 모두 멈춘 뒤 이 시간 안에 공이 투수에게 가지 않으면 강제 종료(초).</summary>
    public const float STATIONARY_TIMEOUT = 3.0f;

    /// <summary>플레이어가 공을 쥐고 있을 때의 여유 있는 강제 종료 시간(초).</summary>
    public const float PLAYER_HOLD_TIMEOUT = 15.0f;

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

    /// <summary>2루 병살 피벗 스텝(베이스를 스치며 주자를 피해 빠지는 동작) 길이(초).</summary>
    public const float PIVOT_STEP_DURATION = 0.34f;

    /// <summary>런다운 펌프 페이크 동작 길이(초).</summary>
    public const float PUMP_FAKE_DURATION = 0.28f;
}
