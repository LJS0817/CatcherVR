using UnityEngine;

/// <summary>런다운 한 틱의 지시.</summary>
public struct RundownOrder
{
    /// <summary>런다운 상황이 성립하는가.</summary>
    public bool Valid;

    /// <summary>공을 쥔 야수가 달려갈 지점(주자를 하위 베이스 쪽으로 몰아붙이는 위치).</summary>
    public Vector3 ChasePoint;

    /// <summary>주자를 몰아붙이는 목표(하위) 베이스.</summary>
    public BASE_TYPE DriveToBase;

    /// <summary>주자가 진루하려는(상위) 베이스.</summary>
    public BASE_TYPE AdvanceBase;

    /// <summary>지금 토스를 넘겨야 하는가.</summary>
    public bool ShouldFlip;

    /// <summary>토스를 받을 야수.</summary>
    public BaseballPlayer FlipReceiver;

    /// <summary>토스를 받을 야수가 지키고 있는 베이스.</summary>
    public BASE_TYPE FlipBase;

    /// <summary>토스 도착 지점.</summary>
    public Vector3 FlipAim;

    /// <summary>펌프 페이크를 해야 하는 구간인가(주자 감속 유도).</summary>
    public bool PumpFake;
}

/// <summary>
/// 런다운(협살 / Pickle) 협력 수비 알고리즘. 설계도 9장.
///
///  ① 하위 베이스 몰이 원칙   : 진루하려는 베이스가 아니라 <b>이전 베이스</b> 쪽으로 몬다.
///  ② 거리 압축 후 송구       : 4~5m 이내로 좁힐 때까지 롱토스 없이 전력 질주,
///                              반대편 야수는 베이스 앞 2~3m 까지 마중 나온다.
///  ③ 송구 2회 이내 종결      : 펌프 페이크 1회로 감속을 유도하고, 주자가 역방향으로
///                              돌아서는 순간 플립 토스. 3회째부터는 던지지 않고 태그로 끝낸다.
/// </summary>
public static class RundownPlay
{
    /// <summary>이 거리 안으로 좁혀야 토스를 허용한다(실측 m).</summary>
    public const float COMPRESS_DISTANCE = 5.0f;

    /// <summary>수신 야수가 베이스 앞으로 마중 나오는 거리(실측 m).</summary>
    public const float RECEIVER_STEP_OUT = 2.5f;

    /// <summary>이 횟수를 넘기면 더 이상 송구하지 않는다.</summary>
    public const int MAX_THROWS = 2;

    /// <summary>주자가 베이스에서 이 거리 이상 떨어져 있어야 런다운으로 본다(실측 m).</summary>
    public const float MIN_GAP_FROM_BASE = 1.6f;

    /// <summary>
    /// 런다운 지시를 계산한다.
    /// </summary>
    /// <param name="me">공을 쥔 야수</param>
    /// <param name="runner">가둔 주자</param>
    /// <param name="team">수비 팀</param>
    /// <param name="throwsSoFar">이번 런다운에서 이미 던진 횟수</param>
    public static RundownOrder Solve(BaseballPlayer me, RunnerInfo runner, Team team, int throwsSoFar)
    {
        RundownOrder o = new RundownOrder();
        o.Valid = false;
        if (me == null || !runner.IsValid || team == null) return o;
        if (runner.IsSafe) return o;

        BASE_TYPE advance = runner.TargetBase;
        BASE_TYPE behind = PreviousBase(advance);
        if (advance == BASE_TYPE.E_SELF || behind == BASE_TYPE.E_SELF) return o;

        Vector3 runnerPos = runner.Player.transform.position;
        Vector3 advancePos = FieldMetrics.BasePos(advance);
        Vector3 behindPos = FieldMetrics.BasePos(behind);

        // 주자가 두 베이스 사이에 충분히 떠 있어야 런다운이다
        float dAdvance = FieldMetrics.FlatDistance(runnerPos, advancePos);
        float dBehind = FieldMetrics.FlatDistance(runnerPos, behindPos);
        float gap = FieldMetrics.M(MIN_GAP_FROM_BASE);
        if (dAdvance < gap || dBehind < gap) return o;

        o.Valid = true;
        o.AdvanceBase = advance;
        o.DriveToBase = behind;

        // 진행 방향 축 : 하위 베이스 → 상위 베이스
        Vector3 axis = advancePos - behindPos;
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.01f) axis = FieldMetrics.Forward;
        axis.Normalize();

        // 나는 주자보다 상위 베이스 쪽에 있는가?
        //  ▸ 그렇다면 주자를 향해 달려가면 자연히 하위 베이스로 밀린다(원칙 ①).
        //  ▸ 아니라면 상위 베이스를 지키는 동료에게 넘겨서 몰이 방향을 바로잡는다.
        float mySide = Vector3.Dot(me.transform.position - runnerPos, axis);
        bool onAdvanceSide = mySide > 0f;

        BaseballPlayer advanceCover = team.GetBaseCoverFielder(advance, me);
        BaseballPlayer behindCover = team.GetBaseCoverFielder(behind, me);

        float distToRunner = FieldMetrics.FlatDistance(me.transform.position, runnerPos);
        float compress = FieldMetrics.M(COMPRESS_DISTANCE);

        if (!onAdvanceSide)
        {
            // 몰이 방향이 잘못됨 — 상위 베이스 쪽 동료에게 즉시 넘긴다.
            if (throwsSoFar < MAX_THROWS && advanceCover != null && advanceCover != me)
            {
                o.ShouldFlip = true;
                o.FlipReceiver = advanceCover;
                o.FlipBase = advance;
                o.FlipAim = StepOutPoint(advancePos, runnerPos, advanceCover);
                o.ChasePoint = runnerPos;
                return o;
            }

            // 넘길 사람이 없으면(또는 송구 한도 초과) 그냥 태그하러 간다.
            o.ChasePoint = runnerPos;
            return o;
        }

        // 원칙 ② : 거리를 4~5m 로 압축할 때까지는 무조건 전력 질주(토스 금지)
        o.ChasePoint = runnerPos;

        if (distToRunner > compress)
        {
            o.ShouldFlip = false;
            return o;
        }

        // 압축 완료 — 펌프 페이크로 감속을 유도한다
        o.PumpFake = true;

        // 원칙 ③ : 주자가 역방향(하위 베이스 쪽)으로 돌아서면 그 순간 플립 토스
        bool runnerReversing = Vector3.Dot(RunnerVelocity(runner), axis) < -0.2f;
        if (runnerReversing && throwsSoFar < MAX_THROWS && behindCover != null && behindCover != me)
        {
            o.ShouldFlip = true;
            o.FlipReceiver = behindCover;
            o.FlipBase = behind;
            o.FlipAim = StepOutPoint(behindPos, runnerPos, behindCover);
        }

        return o;
    }

    /// <summary>수신 야수가 마중 나와 있어야 하는 지점(베이스 앞 2~3m).</summary>
    public static Vector3 StepOutPoint(Vector3 basePos, Vector3 runnerPos, BaseballPlayer receiver)
    {
        Vector3 toRunner = runnerPos - basePos;
        toRunner.y = 0f;
        if (toRunner.sqrMagnitude < 0.01f) return basePos + Vector3.up * FieldMetrics.M(DefenseTuning.THROW_TARGET_HEIGHT);

        Vector3 spot = basePos + toRunner.normalized * FieldMetrics.M(RECEIVER_STEP_OUT);

        if (receiver != null)
        {
            Transform glove = receiver.GetItem();
            // 이미 마중 나와 있으면 글러브를 직접 겨냥한다
            if (glove != null && FieldMetrics.FlatDistance(receiver.transform.position, spot) < FieldMetrics.M(1.5f))
            {
                return glove.position;
            }
        }

        spot.y = FieldMetrics.GroundY + FieldMetrics.M(DefenseTuning.THROW_TARGET_HEIGHT);
        return spot;
    }

    /// <summary>주자의 현재 진행 속도 벡터(근사).</summary>
    static Vector3 RunnerVelocity(RunnerInfo runner)
    {
        if (!runner.IsValid) return Vector3.zero;
        Vector3 target = FieldMetrics.BasePos(runner.TargetBase);
        Vector3 dir = target - runner.Player.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;
        return dir.normalized * Mathf.Max(0.1f, runner.Role != null ? runner.Role.RunSpeed : 0.1f);
    }

    /// <summary>주자가 방금 떠나온(하위) 베이스.</summary>
    public static BASE_TYPE PreviousBase(BASE_TYPE b)
    {
        switch (b)
        {
            case BASE_TYPE.E_SECOND_BASE: return BASE_TYPE.E_FIRST_BASE;
            case BASE_TYPE.E_THIRD_BASE: return BASE_TYPE.E_SECOND_BASE;
            case BASE_TYPE.E_HOME_BASE: return BASE_TYPE.E_THIRD_BASE;
            default: return BASE_TYPE.E_SELF;   // 1루로 향하는 타자 주자는 런다운 대상이 아니다
        }
    }
}
