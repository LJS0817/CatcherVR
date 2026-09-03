using System.Collections.Generic;
using UnityEngine;

/// <summary>한 명의 주자에 대한 스냅샷.</summary>
public struct RunnerInfo
{
    public BaseballPlayer Player;
    public Attacker Role;
    public Vector3 Position;
    /// <summary>지금 향하고 있는(또는 점유 중인) 베이스.</summary>
    public BASE_TYPE TargetBase;
    /// <summary>그 베이스에 도달하기까지 남은 예상 시간(초).</summary>
    public float Eta;
    /// <summary>포스 상황인가(뒤에서 밀려서 반드시 진루해야 하는가).</summary>
    public bool IsForced;
    /// <summary>베이스 위에 안전하게 멈춰 있는가(태그 불가).</summary>
    public bool IsSafe;
    /// <summary>베이스 사이를 달리는 중인가(태그 가능).</summary>
    public bool IsRunning;
    /// <summary>최종 목표 베이스(장타 시 2·3루까지).</summary>
    public BASE_TYPE FinalBase;

    public bool IsValid { get { return Player != null && Role != null; } }
}

/// <summary>
/// 공격팀 주자들의 실시간 상태를 수집한다.
/// 송구 판단(누구를 잡을 수 있는가)의 입력이 된다.
///
/// 기존 코드는 베이스 상태를 Transform 이름 문자열("10", "_2" 등)로만 읽어서
/// "주자가 언제 도착하는가"를 전혀 알 수 없었다. 여기서는 실제 주자 오브젝트의
/// 위치와 속도로부터 도착 예정 시간(ETA)을 계산한다.
/// </summary>
public static class RunnerTracker
{
    static readonly List<RunnerInfo> _buffer = new List<RunnerInfo>(6);
    static int _lastFrame = -1;

    /// <summary>현재 프레임의 주자 목록(프레임당 1회만 실제로 갱신).</summary>
    public static List<RunnerInfo> Snapshot(bool force = false)
    {
        if (!force && _lastFrame == Time.frameCount) return _buffer;
        _lastFrame = Time.frameCount;

        _buffer.Clear();

        GamePlayerProvider gp = GamePlayerProvider.provider;
        if (gp == null) return _buffer;
        Team attack = gp.AttackTeam;
        if (attack == null) return _buffer;

        for (int i = 0; i < attack.PlayerCount; i++)
        {
            BaseballPlayer p = attack.GetPlayerAt(i);
            if (p == null || !p.gameObject.activeSelf) continue;

            Attacker att = p.GetRole() as Attacker;
            if (att == null || !att.IsOnBaseOrRunning) continue;

            RunnerInfo info = new RunnerInfo();
            info.Player = p;
            info.Role = att;
            info.Position = p.transform.position;
            info.TargetBase = att.TargetBase;
            info.FinalBase = att.FinalTargetBase;
            info.IsSafe = att.IsSafeOnBase();
            info.IsRunning = !info.IsSafe;
            info.Eta = EstimateEta(att, p.transform.position, info.TargetBase);
            _buffer.Add(info);
        }

        ResolveForceState();
        return _buffer;
    }

    /// <summary>포스 상태를 뒤(홈 쪽)에서부터 연쇄적으로 판정한다.</summary>
    static void ResolveForceState()
    {
        bool onFirst = false, onSecond = false, onThird = false;
        for (int i = 0; i < _buffer.Count; i++)
        {
            switch (_buffer[i].TargetBase)
            {
                case BASE_TYPE.E_FIRST_BASE: onFirst = true; break;
                case BASE_TYPE.E_SECOND_BASE: onSecond = true; break;
                case BASE_TYPE.E_THIRD_BASE: onThird = true; break;
            }
        }

        for (int i = 0; i < _buffer.Count; i++)
        {
            RunnerInfo r = _buffer[i];
            switch (r.TargetBase)
            {
                case BASE_TYPE.E_FIRST_BASE:
                    r.IsForced = true;                       // 타자 주자는 언제나 포스
                    break;
                case BASE_TYPE.E_SECOND_BASE:
                    r.IsForced = onFirst;
                    break;
                case BASE_TYPE.E_THIRD_BASE:
                    r.IsForced = onFirst && onSecond;
                    break;
                case BASE_TYPE.E_HOME_BASE:
                    r.IsForced = onFirst && onSecond && onThird;
                    break;
                default:
                    r.IsForced = false;
                    break;
            }
            _buffer[i] = r;
        }
    }

    /// <summary>가속을 고려한 주자의 베이스 도착 예정 시간.</summary>
    public static float EstimateEta(Attacker att, Vector3 pos, BASE_TYPE target)
    {
        Vector3 basePos = FieldMetrics.BasePos(target);
        float d = FieldMetrics.FlatDistance(pos, basePos);
        if (d <= 0.05f) return 0f;

        float vMax = FieldMetrics.M(DefenseTuning.RUNNER_TOP_SPEED);
        float a = FieldMetrics.M(DefenseTuning.RUNNER_ACCEL);
        float v0 = att != null ? Mathf.Clamp(att.RunSpeed, 0f, vMax) : vMax * 0.6f;
        float startDelay = (att != null && att.IsStarting) ? att.StartDelayRemaining : 0f;

        float tToMax = (vMax - v0) / Mathf.Max(0.01f, a);
        float dAcc = v0 * tToMax + 0.5f * a * tToMax * tToMax;

        float t;
        if (d <= dAcc)
        {
            // 0.5*a*t^2 + v0*t - d = 0
            float disc = v0 * v0 + 2f * a * d;
            t = (-v0 + Mathf.Sqrt(Mathf.Max(0f, disc))) / Mathf.Max(0.01f, a);
        }
        else
        {
            t = tToMax + (d - dAcc) / vMax;
        }

        return Mathf.Max(0f, t) + startDelay;
    }

    /// <summary>해당 베이스를 향해 오는 주자(포스/태그 대상). 없으면 IsValid=false.</summary>
    public static RunnerInfo RunnerHeadingTo(BASE_TYPE b)
    {
        List<RunnerInfo> list = Snapshot();
        RunnerInfo best = new RunnerInfo();
        float bestEta = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].TargetBase != b) continue;
            if (list[i].Eta < bestEta) { bestEta = list[i].Eta; best = list[i]; }
        }
        return best;
    }

    /// <summary>선행 주자(홈에 가장 가까운 주자).</summary>
    public static RunnerInfo LeadRunner()
    {
        List<RunnerInfo> list = Snapshot();
        RunnerInfo best = new RunnerInfo();
        int bestRank = -1;
        for (int i = 0; i < list.Count; i++)
        {
            int rank = BaseRank(list[i].TargetBase);
            if (rank > bestRank) { bestRank = rank; best = list[i]; }
        }
        return best;
    }

    /// <summary>홈에 가까울수록 큰 값.</summary>
    public static int BaseRank(BASE_TYPE b)
    {
        switch (b)
        {
            case BASE_TYPE.E_FIRST_BASE: return 1;
            case BASE_TYPE.E_SECOND_BASE: return 2;
            case BASE_TYPE.E_THIRD_BASE: return 3;
            case BASE_TYPE.E_HOME_BASE: return 4;
            default: return 0;
        }
    }

    /// <summary>어떤 베이스든 주자가 향하고 있는가.</summary>
    public static bool AnyRunnerActive()
    {
        return Snapshot().Count > 0;
    }

    /// <summary>주자가 점유(또는 진입 예정)한 베이스 집합을 비트마스크로.</summary>
    public static int OccupiedMask()
    {
        List<RunnerInfo> list = Snapshot();
        int mask = 0;
        for (int i = 0; i < list.Count; i++) mask |= 1 << BaseRank(list[i].TargetBase);
        return mask;
    }

    /// <summary>주자에게 태그를 시도할 수 있는 거리인지.</summary>
    public static bool IsTaggable(RunnerInfo r, Vector3 fielderPos)
    {
        if (!r.IsValid) return false;
        if (!r.Role.IsOnBaseOrRunning) return false;
        if (r.IsSafe) return false;
        if (!r.Player.gameObject.activeSelf) return false;
        return FieldMetrics.FlatDistance(r.Player.transform.position, fielderPos) <= FieldMetrics.M(DefenseTuning.TAG_RANGE);
    }
}
