using System.Collections.Generic;
using UnityEngine;

public class Team : MonoBehaviour
{
    List<BaseballPlayer> _players;
    int _attackNum;
    bool _isAttack;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _attackNum = 0;
    }

    // Update is called once per frame
    void Update()
    {
     
    }

    public Transform GetPlayerItem(PLAYER_TYPE type)
    {
        return _players[(int)type].GetItem();
    }

    public BaseballPlayer GetCurrentBatter()
    {
        if (_players != null && _attackNum >= 0 && _attackNum < _players.Count)
        {
            return _players[_attackNum];
        }
        return null;
    }

    public BaseballPlayer GetFielderCoveringBase(Transform targetBase)
    {
        if (targetBase == null) return null;
        
        BaseballPlayer bestFielder = null;
        float minDist = float.MaxValue;
        
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].gameObject.activeSelf)
            {
                // 베이스를 목표로 이동 중이거나 대기 중인 선수 찾기
                if ((_players[i].DefRole == DEFENSIVE_ROLE.BASE_COVER || _players[i].DefRole == DEFENSIVE_ROLE.IDLE) &&
                    Vector3.Distance(_players[i].RoleTargetPosition, targetBase.position) < 1.0f)
                {
                    return _players[i];
                }
                
                // 보험용: 베이스에 가장 가까운 선수 찾기
                float dist = Vector3.Distance(_players[i].transform.position, targetBase.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    bestFielder = _players[i];
                }
            }
        }
        
        if (minDist < 6.0f && bestFielder != null)
        {
            return bestFielder;
        }
        
        return null; // 못 찾으면 null 리턴
    }

    public BaseballPlayer GetBestFielderForCatch(Vector3 landPos)
    {
        BaseballPlayer best = null;
        float bestScore = float.MaxValue;
        
        Vector3 homePos = (BasePositionProvider.provider != null) ? BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE) : Vector3.zero;

        for(int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (i == _attackNum) continue;

            PLAYER_TYPE pType = _players[i].Type;
            bool isOutfielder = (pType == PLAYER_TYPE.E_LEFT_FILED || pType == PLAYER_TYPE.E_CENTER_FIELD || pType == PLAYER_TYPE.E_RIGHT_FILED);

            float dist = Vector3.Distance(_players[i].transform.position, landPos);
            
            // 존 기반 우선도 계산
            DefenseZone zone = _players[i].GetDefenseZone();
            float zonePriority = (zone != null) ? zone.GetPriority(landPos, homePos) : 0f;

            // 스코어 = 거리 - (존 우선도 보너스)
            // 존 안에 있으면 큰 보너스를 받아 스코어가 낮아짐 (낮을수록 좋음)
            float score = dist - (zonePriority * 30f);
            
            // 외야수 어드밴티지 (경계 타구 시 외야수 우선)
            if (isOutfielder) score -= 3.0f;

            if(score < bestScore) {
                bestScore = score;
                best = _players[i];
            }
        }
        return best;
    }

    /// <summary>기존 호환용 (BATTED_BALL_TYPE 없이 호출)</summary>
    public void AssignDefensiveRoles(Vector3 landPos)
    {
        AssignDefensiveRoles(landPos, BATTED_BALL_TYPE.GROUND_BALL);
    }

    /// <summary>
    /// 존 기반 역할 분배 (타구 유형 고려)
    /// </summary>
    public void AssignDefensiveRoles(Vector3 landPos, BATTED_BALL_TYPE ballType)
    {
        if (_isAttack) return; // 수비 팀일 때만 동작

        // 1. 초기화
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (i == _attackNum) continue;
            _players[i].DefRole = DEFENSIVE_ROLE.IDLE;
        }

        // 2. Chaser 결정 (존 기반)
        BaseballPlayer chaser = GetBestFielderForCatch(landPos);
        if (chaser != null)
        {
            chaser.DefRole = DEFENSIVE_ROLE.CHASER;
            chaser.RoleTargetPosition = landPos;
        }

        bool isOutfieldHit = landPos.z > 25f;

        // 3. 역할 분배 (타구 유형 전달)
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (i == _attackNum || _players[i] == chaser) continue;

            // 각 포지션 클래스에서 타구 유형별 고유 역할 우선 할당
            if (_players[i].AssignSpecialRole(chaser, landPos, ballType)) continue;

            PLAYER_TYPE pType = _players[i].Type;
            bool isOutfielder = (pType == PLAYER_TYPE.E_LEFT_FILED || pType == PLAYER_TYPE.E_CENTER_FIELD || pType == PLAYER_TYPE.E_RIGHT_FILED);

            // 외야 타구 시 컷오프 맨 지정 (더블 컷오프)
            if (isOutfieldHit)
            {
                bool isLeftHit = landPos.x < 0;
                
                if (pType == PLAYER_TYPE.E_SHORT_STOP || pType == PLAYER_TYPE.E_SECOND_BASE)
                {
                    _players[i].DefRole = DEFENSIVE_ROLE.CUTOFF;
                    if (chaser != null) 
                    {
                        Transform throwTarget = BasePositionProvider.provider.GetThrowTarget(chaser.transform, chaser.Type);
                        Vector3 dirToChaser = (landPos - throwTarget.position).normalized;
                        float totalDist = Vector3.Distance(landPos, throwTarget.position);
                        
                        Vector3 baseCutoffPos = throwTarget.position + dirToChaser * (totalDist * 0.4f);
                        
                        if ((isLeftHit && pType == PLAYER_TYPE.E_SHORT_STOP) || (!isLeftHit && pType == PLAYER_TYPE.E_SECOND_BASE))
                        {
                            // 메인 컷오프
                            _players[i].RoleTargetPosition = baseCutoffPos;
                        }
                        else
                        {
                            // 보조 컷오프 (더블 컷오프: 메인 뒤쪽 8m)
                            _players[i].RoleTargetPosition = baseCutoffPos - dirToChaser * 8.0f;
                        }
                    }
                    continue;
                }
            }
            
            // 근접 야수 백업 (외야 타구 시)
            if (chaser != null && _players[i].DefRole == DEFENSIVE_ROLE.IDLE)
            {
                float distToChaser = Vector3.Distance(_players[i].transform.position, chaser.transform.position);
                if (distToChaser < 20f && isOutfieldHit) 
                {
                    _players[i].DefRole = DEFENSIVE_ROLE.BACKUP;
                    // 체이서 뒤쪽으로 8m 깊은 백업
                    Vector3 backupDir = (chaser.transform.position - BasePositionProvider.provider.GetBasePosition(BASE_TYPE.E_HOME_BASE)).normalized;
                    _players[i].RoleTargetPosition = chaser.transform.position + backupDir * 8f;
                }
            }

            // 내야 타구일 때 잉여 내야수 백업
            if (chaser != null && !isOutfieldHit && _players[i].DefRole == DEFENSIVE_ROLE.IDLE && !isOutfielder)
            {
                _players[i].DefRole = DEFENSIVE_ROLE.BACKUP;
                // 송구 타겟 연장선 뒤쪽 8m (악송구 대비 뎁스 백업)
                Transform throwTarget = BasePositionProvider.provider.GetThrowTarget(chaser.transform, chaser.Type);
                if (throwTarget != chaser.transform)
                {
                    Vector3 throwDir = (throwTarget.position - chaser.transform.position).normalized;
                    _players[i].RoleTargetPosition = throwTarget.position + throwDir * 8.0f;
                }
                else
                {
                    _players[i].RoleTargetPosition = chaser.transform.position + (chaser.transform.position - landPos).normalized * 5f;
                }
            }
        }
    }

    public void ChangeAttacker()
    {
        // 1. 이전 타자들 중 아웃되거나 득점한 선수만 끄고, 출루한 주자는 베이스 위에 그대로 남겨둠!
        if (_players != null)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (i != _attackNum && _players[i] != null && _players[i].gameObject.activeSelf)
                {
                    // 출루/주루 중이 아닌 선수만 비활성화
                    if (!_players[i].IsActiveRunner)
                    {
                        _players[i].ExitGame();
                    }
                }
            }

            // 직전 타자 체크
            if (_attackNum >= 0 && _attackNum < _players.Count)
            {
                BaseballPlayer prevBatter = _players[_attackNum];
                if (prevBatter != null && !prevBatter.IsActiveRunner)
                {
                    prevBatter.ExitGame();
                }
            }
        }

        // 2. 다음 타순으로 전환하여 새 타자 타석 등장
        _attackNum = (_attackNum + 1) % 9;
        if (_players != null && _attackNum >= 0 && _attackNum < _players.Count)
        {
            _players[_attackNum].JoinGame();
        }
    }

    /// <summary>
    /// 새 타자가 타격했을 때, 이미 출루해 있던 주자들에게 진루 명령
    /// </summary>
    public void NotifyRunnersToAdvance(Vector3 hitLandPos, BaseballPlayer currentBatter)
    {
        if (_players == null) return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i] != currentBatter && _players[i].gameObject.activeSelf)
            {
                if (_players[i].GetRole() is Attacker runner)
                {
                    runner.AdvanceRunnerOnNextHit(hitLandPos);
                }
            }
        }
    }

    /// <summary>
    /// 특정 베이스를 향해 달려가고 있는 공격팀 주자 검색 (포스 아웃 판정용)
    /// </summary>
    public BaseballPlayer GetRunnerHeadingToBase(BASE_TYPE targetBase)
    {
        if (_players == null) return null;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].gameObject.activeSelf)
            {
                if (_players[i].GetRole() is Attacker runner)
                {
                    if (runner.IsOnBaseOrRunning && runner.TargetBase == targetBase)
                    {
                        return _players[i];
                    }
                }
            }
        }
        return null;
    }

    public BaseballPlayer GetPlayer(PLAYER_TYPE type)
    {
        if (_players != null && (int)type >= 0 && (int)type < _players.Count)
        {
            return _players[(int)type];
        }
        return null;
    }

    public void ChangeRole(bool isAttacker)
    {
        _isAttack = isAttacker;
        
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null)
            {
                if (isAttacker)
                {
                    // 공격팀이 되면 현재 타자만 Active하고 나머지는 Exit
                    if (i == _attackNum) _players[i].JoinGame();
                    else _players[i].ExitGame();
                }
                else
                {
                    // 수비팀이 되면 모든 선수가 Active (단, 타격 로직 비활성화는 내부적으로 _isAttack 등으로 구분됨)
                    _players[i].JoinGame();
                    _players[i].ResetPosition();
                }
            }
        }
    }

    public void ResetDefense()
    {
        // ★ 중요: 공격팀인 경우 수비수 위치를 리셋하지 않음 (출루 주자 위치 보존)
        if (_isAttack) return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] != null && _players[i].gameObject.activeSelf)
            {
                _players[i].ResetPosition();
            }
        }
    }

    void setActivePlayer(int idx, bool active)
    {
        if(active) _players[idx].JoinGame();
        else _players[idx].ExitGame();
    }

    public void PlayerOut(PLAYER_TYPE t)
    {
        // 아웃된 선수는 즉시 비활성화(ExitGame) 및 플래그 해제
        if (_players != null && (int)t >= 0 && (int)t < _players.Count)
        {
            BaseballPlayer outPlayer = _players[(int)t];
            if (outPlayer != null)
            {
                if (outPlayer.GetRole() is Attacker att)
                {
                    att.OnOut();
                }
                else
                {
                    outPlayer.ExitGame();
                }
            }
        }

        // 아웃된 선수가 있던 베이스 상태 정리
        if (BasePositionProvider.provider != null)
        {
            for (int i = 0; i < 3; i++)
            {
                BASE_TYPE bType = (BASE_TYPE)(i + 2);
                char stateChar = BasePositionProvider.provider.GetAttckerBaseState(bType);
                if (stateChar != '_' && BasePositionProvider.provider.GetAttackerPlayerType(stateChar) == t)
                {
                    BasePositionProvider.provider.SetAttackerBaseState(bType, -1);
                }
            }
        }

        // 현재 타자나 투수 아웃 시 다음 타자로 교체
        if ((int)t == _attackNum || t == PLAYER_TYPE.E_PITCHER)
        {
            ChangeAttacker();
        }
    }

    public void init(Transform pool, Ball ball, bool isAttacker)
    {
        //Debug.Log(transform.name + "  " + role);
        _players = new List<BaseballPlayer>();
        _isAttack = isAttacker;
        for (int i = 0; i < transform.childCount; i++)
        {
            BaseballPlayer bp = transform.GetChild(i).GetComponent<BaseballPlayer>();
            bp.SetPlayerType((PLAYER_TYPE)i);
            _players.Add(bp);
            //_players[i].FocusBall(ball, role);
            _players[i].EnterTheGame(pool, ball, isAttacker);
            if (isAttacker && _attackNum != i) _players[i].ExitGame();
        }
    }
}
