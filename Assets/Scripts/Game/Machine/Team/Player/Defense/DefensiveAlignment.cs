using UnityEngine;

/// <summary>
/// 투구 전 수비 포메이션(뎁스 / 시프트)을 결정한다.
///
/// MLB The Show 나 실제 야구처럼, 아웃카운트 · 주자 상황 · 이닝에 따라
/// 내야 뎁스(병살 뎁스 / 전진 수비 / 정상)와 외야 뎁스(노 다블 / 정상 / 전진)를 바꾼다.
/// 각 야수의 원래 위치(DefaultPosition)를 기준으로 홈 방향 축을 따라 밀고 당긴다.
/// </summary>
public static class DefensiveAlignment
{
    public enum INFIELD_DEPTH { NORMAL, DOUBLE_PLAY, IN, BACK }
    public enum OUTFIELD_DEPTH { NORMAL, NO_DOUBLES, SHALLOW }

    public static INFIELD_DEPTH CurrentInfield { get; private set; }
    public static OUTFIELD_DEPTH CurrentOutfield { get; private set; }

    /// <summary>투구 전 포메이션 적용.</summary>
    public static void ApplyPrePitch(Team team)
    {
        if (team == null || team.IsAttackTeam) return;
        FieldMetrics.Refresh();
        if (!FieldMetrics.Ready) return;

        DecideDepth();
        Calibrate(team);

        for (int i = 0; i < team.PlayerCount; i++)
        {
            BaseballPlayer p = team.GetPlayerAt(i);
            if (p == null) continue;

            Vector3 basePos = p.DefaultPosition;
            Vector3 fromHome = basePos - FieldMetrics.Home;
            fromHome.y = 0f;
            float dist = fromHome.magnitude;
            if (dist < 0.5f) { p.SetFieldingPosition(basePos); continue; }
            Vector3 outward = fromHome / dist;

            float depthOffset = 0f;
            float lateralOffset = 0f;

            FielderAttributes attr = p.Attributes;

            if (attr.IsInfielder)
            {
                switch (CurrentInfield)
                {
                    case INFIELD_DEPTH.DOUBLE_PLAY:
                        // 병살 뎁스 : 2·유는 2루 쪽으로 붙고 조금 앞으로
                        depthOffset = -FieldMetrics.M(1.8f);
                        if (p.Type == PLAYER_TYPE.E_SECOND_BASE) lateralOffset = -FieldMetrics.M(2.4f);
                        else if (p.Type == PLAYER_TYPE.E_SHORT_STOP) lateralOffset = FieldMetrics.M(2.4f);
                        break;
                    case INFIELD_DEPTH.IN:
                        // 전진 수비 : 홈 송구를 위해 크게 전진
                        depthOffset = -FieldMetrics.M(5.5f);
                        break;
                    case INFIELD_DEPTH.BACK:
                        depthOffset = FieldMetrics.M(2.2f);
                        break;
                }

                // 코너(1·3루수)는 장타 상황에서 라인을 지킨다
                if (CurrentOutfield == OUTFIELD_DEPTH.NO_DOUBLES)
                {
                    if (p.Type == PLAYER_TYPE.E_FIRST_BASE) lateralOffset += FieldMetrics.M(2.0f);
                    else if (p.Type == PLAYER_TYPE.E_THIRD_BASE) lateralOffset -= FieldMetrics.M(2.0f);
                }
            }
            else if (attr.IsOutfielder)
            {
                switch (CurrentOutfield)
                {
                    case OUTFIELD_DEPTH.NO_DOUBLES:
                        depthOffset = FieldMetrics.M(6.0f);
                        // 좌·우익수는 라인 쪽으로 이동
                        if (p.Type == PLAYER_TYPE.E_LEFT_FILED) lateralOffset = -FieldMetrics.M(4.0f);
                        else if (p.Type == PLAYER_TYPE.E_RIGHT_FILED) lateralOffset = FieldMetrics.M(4.0f);
                        break;
                    case OUTFIELD_DEPTH.SHALLOW:
                        depthOffset = -FieldMetrics.M(7.0f);
                        break;
                }
            }

            Vector3 lateralDir = Vector3.Cross(Vector3.up, outward).normalized;
            Vector3 target = basePos + outward * depthOffset + lateralDir * lateralOffset;
            target.y = basePos.y;
            p.SetFieldingPosition(target);
        }
    }

    /// <summary>
    /// 타구가 발생한 뒤의 즉각적인 보정(현재는 상태 기록만).
    /// 조율자가 배정 계산에 쓸 수 있도록 훅으로 남겨둔다.
    /// </summary>
    public static void ApplyInPlay(Team team, BallTrajectory traj, BATTED_BALL_TYPE type)
    {
        // 인플레이 중에는 포메이션을 바꾸지 않는다(역할 배정이 담당).
    }

    static bool _calibrated;

    /// <summary>
    /// 외야수가 서 있는 깊이를 재서 이 구장에 맞는 타구 비거리를 산출한다(최초 1회).
    /// </summary>
    static void Calibrate(Team team)
    {
        if (_calibrated) return;

        float deepest = 0f;
        for (int i = 0; i < team.PlayerCount; i++)
        {
            BaseballPlayer p = team.GetPlayerAt(i);
            if (p == null || !p.Attributes.IsOutfielder) continue;
            float d = FieldMetrics.DistFromHome(p.DefaultPosition);
            if (d > deepest) deepest = d;
        }

        if (deepest > 5f)
        {
            BallAero.AutoCalibrate(deepest);
            _calibrated = true;
        }
    }

    /// <summary>구장 보정을 다시 계산하도록 강제(수비 위치를 바꿨을 때).</summary>
    public static void InvalidateCalibration() { _calibrated = false; }

    static void DecideDepth()
    {
        CurrentInfield = INFIELD_DEPTH.NORMAL;
        CurrentOutfield = OUTFIELD_DEPTH.NORMAL;

        CountsProvider cp = CountsProvider.provider;
        int outs = cp != null ? cp.GetCount(COUNT_TYPE.E_OUT) : 0;

        bool runnerOnFirst = false, runnerOnThird = false, anyRunner = false;
        BasePositionProvider bp = BasePositionProvider.provider;
        if (bp != null && bp.Bases != null && bp.Bases.Count >= 3)
        {
            runnerOnFirst = bp.GetAttckerBaseState(BASE_TYPE.E_FIRST_BASE) != '_';
            runnerOnThird = bp.GetAttckerBaseState(BASE_TYPE.E_THIRD_BASE) != '_';
            anyRunner = runnerOnFirst || runnerOnThird ||
                        bp.GetAttckerBaseState(BASE_TYPE.E_SECOND_BASE) != '_';
        }

        // 3루 주자 + 2아웃 미만 → 전진 수비(홈 승부)
        if (runnerOnThird && outs < 2)
        {
            CurrentInfield = INFIELD_DEPTH.IN;
        }
        // 1루 주자 + 2아웃 미만 → 병살 뎁스
        else if (runnerOnFirst && outs < 2)
        {
            CurrentInfield = INFIELD_DEPTH.DOUBLE_PLAY;
        }

        // 주자 없이 2아웃 → 장타 방지(노 다블) 수비
        if (!anyRunner && outs >= 2)
        {
            CurrentOutfield = OUTFIELD_DEPTH.NO_DOUBLES;
        }
        // 3루 주자 + 2아웃 미만 → 외야도 전진(홈 송구 대비)
        else if (runnerOnThird && outs < 2)
        {
            CurrentOutfield = OUTFIELD_DEPTH.SHALLOW;
        }
    }
}
