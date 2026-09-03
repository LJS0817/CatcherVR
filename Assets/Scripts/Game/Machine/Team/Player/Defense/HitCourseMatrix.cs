using UnityEngine;

/// <summary>설계도 5.5 의 12개 타구 코스.</summary>
public enum HIT_COURSE
{
    NONE = 0,

    // A. 내야 타구
    THIRD_LINE_SMASH,      // 1. 3루선상 강습 땅볼
    THIRD_SHORT_HOLE,      // 2. 3-유간 깊은 땅볼
    SHORTSTOP_FRONT,       // 3. 유격수 정면 땅볼
    SECOND_FRONT,          // 4. 2루수 정면 땅볼
    FIRST_SECOND_HOLE,     // 5. 1-2루간 깊은 땅볼
    FIRST_LINE_SMASH,      // 6. 1루선상 강습 땅볼

    // B. 외야 타구
    LEFT_LINE_DOUBLE,      // 7. 좌익선상 2루타(펜스 코너)
    LEFT_SINGLE,           // 8. 좌전 단타
    LEFT_CENTER_GAP,       // 9. 좌중간 가르는 장타
    CENTER_SINGLE,         // 10. 중전 단타
    RIGHT_CENTER_GAP,      // 11. 우중간 가르는 장타
    RIGHT_SINGLE           // 12. 우전 단타 / 우익선상
}

/// <summary>매트릭스 셀이 지정하는 행동.</summary>
public enum COURSE_ACTION
{
    NONE,
    PRIMARY,      // [P]   직접 타구를 포구하러 질주
    COVER,        // [C-]  해당 베이스로 전력 질주하여 포구/태그 대기
    CUTOFF,       // [Cut-] 해당 송구선 사이에서 중계/컷오프 정렬
    BACKUP,       // [B-]  해당 송구/포구 지점 후방으로 백업 질주
    OBSERVE       // [O]   제자리에서 2차 바운드 대비 및 주자 주시
}

/// <summary>한 포지션에게 지정된 셀.</summary>
public struct CourseCell
{
    public COURSE_ACTION Action;
    /// <summary>COVER / CUTOFF / BACKUP 의 대상 베이스. 대상이 타구 처리 지점이면 E_SELF.</summary>
    public BASE_TYPE Target;

    public static CourseCell Of(COURSE_ACTION a, BASE_TYPE b)
    {
        CourseCell c = new CourseCell();
        c.Action = a;
        c.Target = b;
        return c;
    }
}

/// <summary>
/// 설계도 5.5 「타구 코스별 8인 AI 전원 실시간 행동 매트릭스」의 룩업 테이블.
///
/// 조율자(FieldingCoordinator)는 ETA 기반으로 1차 처리자를 고른 뒤,
/// <b>나머지 7명의 커버 · 중계 · 백업 배정을 이 표에 맞춰 확정</b>한다.
/// 표에 없는 상황(주자 조합 등)은 기존 동적 로직이 계속 담당한다.
///
/// 표기 대응
///   [P] → PRIMARY, [C-베이스] → COVER, [Cut-타겟] → CUTOFF,
///   [B-대상] → BACKUP, [O] → OBSERVE
/// </summary>
public static class HitCourseMatrix
{
    // 포지션 인덱스 (표의 열 순서 : P, 1B, 2B, 3B, SS, LF, CF, RF)
    const int P = 0, B1 = 1, B2 = 2, B3 = 3, SS = 4, LF = 5, CF = 6, RF = 7;

    static readonly CourseCell[][] _rows = BuildRows();

    static int IndexOf(PLAYER_TYPE t)
    {
        switch (t)
        {
            case PLAYER_TYPE.E_PITCHER: return P;
            case PLAYER_TYPE.E_FIRST_BASE: return B1;
            case PLAYER_TYPE.E_SECOND_BASE: return B2;
            case PLAYER_TYPE.E_THIRD_BASE: return B3;
            case PLAYER_TYPE.E_SHORT_STOP: return SS;
            case PLAYER_TYPE.E_LEFT_FILED: return LF;
            case PLAYER_TYPE.E_CENTER_FIELD: return CF;
            case PLAYER_TYPE.E_RIGHT_FILED: return RF;
            default: return -1;
        }
    }

    /// <summary>해당 코스에서 그 포지션이 맡을 행동.</summary>
    public static CourseCell Cell(HIT_COURSE course, PLAYER_TYPE t)
    {
        int row = (int)course - 1;
        int col = IndexOf(t);
        if (row < 0 || row >= _rows.Length || col < 0) return CourseCell.Of(COURSE_ACTION.NONE, BASE_TYPE.E_SELF);
        return _rows[row][col];
    }

    // ============================================================
    //  타구 코스 판별
    // ============================================================

    /// <summary>
    /// 타구 착지/처리 지점과 타구 유형으로 12개 코스 중 하나를 판별한다.
    /// </summary>
    /// <param name="spot">타구가 처리될 지점(낙구 또는 차단 지점)</param>
    /// <param name="type">타구 유형</param>
    public static HIT_COURSE Classify(Vector3 spot, BATTED_BALL_TYPE type)
    {
        if (!FieldMetrics.Ready) return HIT_COURSE.NONE;

        float dist = FieldMetrics.DistFromHome(spot);
        // + 가 좌측(3루/좌익), - 가 우측(1루/우익)
        float angle = FieldMetrics.SprayAngle(spot);

        bool outfield = dist > FieldMetrics.M(48f);
        bool deep = dist > FieldMetrics.M(72f);

        if (!outfield)
        {
            // ── A. 내야 타구 ──
            if (angle > 30f) return HIT_COURSE.THIRD_LINE_SMASH;      // 3루선상
            if (angle > 14f) return HIT_COURSE.THIRD_SHORT_HOLE;      // 3-유간
            if (angle > 2f) return HIT_COURSE.SHORTSTOP_FRONT;        // 유격수 정면
            if (angle > -14f) return HIT_COURSE.SECOND_FRONT;         // 2루수 정면
            if (angle > -30f) return HIT_COURSE.FIRST_SECOND_HOLE;    // 1-2루간
            return HIT_COURSE.FIRST_LINE_SMASH;                       // 1루선상
        }

        // ── B. 외야 타구 ──
        if (angle > 30f) return HIT_COURSE.LEFT_LINE_DOUBLE;          // 좌익선상
        if (angle > 13f) return deep ? HIT_COURSE.LEFT_CENTER_GAP : HIT_COURSE.LEFT_SINGLE;
        if (angle > -13f)
        {
            // 좌/우중간 갭인지 중견수 정면인지
            if (deep) return angle >= 0f ? HIT_COURSE.LEFT_CENTER_GAP : HIT_COURSE.RIGHT_CENTER_GAP;
            return HIT_COURSE.CENTER_SINGLE;
        }
        if (angle > -30f) return deep ? HIT_COURSE.RIGHT_CENTER_GAP : HIT_COURSE.RIGHT_SINGLE;
        return HIT_COURSE.RIGHT_SINGLE;                               // 우익선상
    }

    /// <summary>이 코스에서 1차 처리자로 표에 지정된 포지션(참고용).</summary>
    public static bool IsMatrixPrimary(HIT_COURSE course, PLAYER_TYPE t)
    {
        return Cell(course, t).Action == COURSE_ACTION.PRIMARY;
    }

    // ============================================================
    //  설계도 5.5 표 원문
    // ============================================================

    static CourseCell[] Row(params CourseCell[] cells) { return cells; }

    static CourseCell Pri() { return CourseCell.Of(COURSE_ACTION.PRIMARY, BASE_TYPE.E_SELF); }
    static CourseCell Cov(BASE_TYPE b) { return CourseCell.Of(COURSE_ACTION.COVER, b); }
    static CourseCell Cut(BASE_TYPE b) { return CourseCell.Of(COURSE_ACTION.CUTOFF, b); }
    static CourseCell Bak(BASE_TYPE b) { return CourseCell.Of(COURSE_ACTION.BACKUP, b); }
    static CourseCell BakBall() { return CourseCell.Of(COURSE_ACTION.BACKUP, BASE_TYPE.E_BALL); }
    static CourseCell Obs() { return CourseCell.Of(COURSE_ACTION.OBSERVE, BASE_TYPE.E_SELF); }

    static CourseCell[][] BuildRows()
    {
        CourseCell[][] r = new CourseCell[12][];

        // 열 순서 :        P             1B                      2B                      3B                      SS                      LF                      CF                      RF

        // 1. 3루선상 강습 땅볼
        r[0] = Row(Bak(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Pri(), Bak(BASE_TYPE.E_THIRD_BASE), Bak(BASE_TYPE.E_THIRD_BASE), Obs(), Bak(BASE_TYPE.E_FIRST_BASE));

        // 2. 3-유간 깊은 땅볼
        r[1] = Row(Bak(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Obs(), Pri(), BakBall(), Obs(), Bak(BASE_TYPE.E_FIRST_BASE));

        // 3. 유격수 정면 땅볼
        r[2] = Row(Obs(), Cov(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Pri(), BakBall(), Bak(BASE_TYPE.E_SECOND_BASE), Bak(BASE_TYPE.E_FIRST_BASE));

        // 4. 2루수 정면 땅볼
        r[3] = Row(Obs(), Cov(BASE_TYPE.E_FIRST_BASE), Pri(), Cov(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Obs(), Bak(BASE_TYPE.E_SECOND_BASE), Bak(BASE_TYPE.E_SECOND_BASE));

        // 5. 1-2루간 깊은 땅볼  (투수가 1루 커버로 전력질주)
        r[4] = Row(Cov(BASE_TYPE.E_FIRST_BASE), Obs(), Pri(), Cov(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Obs(), Obs(), Bak(BASE_TYPE.E_FIRST_BASE));

        // 6. 1루선상 강습 땅볼  (P-1B 토스 커버)
        r[5] = Row(Cov(BASE_TYPE.E_FIRST_BASE), Pri(), Bak(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Obs(), Obs(), Bak(BASE_TYPE.E_FIRST_BASE));

        // 7. 좌익선상 2루타(펜스 코너)
        r[6] = Row(Bak(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cut(BASE_TYPE.E_THIRD_BASE), Pri(), BakBall(), Obs());

        // 8. 좌전 단타
        r[7] = Row(Bak(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cut(BASE_TYPE.E_SECOND_BASE), Pri(), BakBall(), Obs());

        // 9. 좌중간 가르는 장타
        r[8] = Row(Bak(BASE_TYPE.E_HOME_BASE), Cut(BASE_TYPE.E_HOME_BASE), Cut(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cut(BASE_TYPE.E_HOME_BASE), Pri(), BakBall(), Bak(BASE_TYPE.E_SECOND_BASE));

        // 10. 중전 단타
        r[9] = Row(Bak(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_FIRST_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cut(BASE_TYPE.E_SECOND_BASE), BakBall(), Pri(), BakBall());

        // 11. 우중간 가르는 장타
        r[10] = Row(Bak(BASE_TYPE.E_HOME_BASE), Cut(BASE_TYPE.E_HOME_BASE), Cut(BASE_TYPE.E_HOME_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cut(BASE_TYPE.E_SECOND_BASE), Obs(), BakBall(), Pri());

        // 12. 우전 단타 / 우익선상
        r[11] = Row(Bak(BASE_TYPE.E_THIRD_BASE), Cut(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Cov(BASE_TYPE.E_THIRD_BASE), Cov(BASE_TYPE.E_SECOND_BASE), Bak(BASE_TYPE.E_THIRD_BASE), BakBall(), Pri());

        return r;
    }
}
