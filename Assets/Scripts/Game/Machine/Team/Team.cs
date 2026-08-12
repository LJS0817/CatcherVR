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
        float minDist = float.MaxValue;
        
        bool isOutfieldHit = landPos.z > 25f;

        for(int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            // 공격수인 경우 제외 (투수는 예외적으로 투구 후 수비하므로 포함)
            if (_isAttack && i == _attackNum) continue;
            if (!_isAttack && i == _attackNum) continue;

            // 외야 타구면 외야수만, 내야 타구면 내야수 및 투수만 고려
            PLAYER_TYPE pType = _players[i].Type;
            bool isOutfielder = (pType == PLAYER_TYPE.E_LEFT_FILED || pType == PLAYER_TYPE.E_CENTER_FIELD || pType == PLAYER_TYPE.E_RIGHT_FILED);

            // 거리 기반으로 최적의 수비수를 찾도록 제한을 완화 (내/외야 경계에서 애매한 타구 처리)
            float dist = Vector3.Distance(_players[i].transform.position, landPos);
            
            // 외야수인 경우 거리에 어드밴티지를 주어 우선권을 가짐 (유저 피드백: "완전 애매하다면 외야수")
            if (isOutfielder) dist -= 3.0f; 

            if(dist < minDist) {
                minDist = dist;
                best = _players[i];
            }
        }
        return best;
    }

    public void AssignDefensiveRoles(Vector3 landPos)
    {
        if (_isAttack) return; // 수비 팀일 때만 동작

        // 1. 초기화
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (i == _attackNum) continue;
            _players[i].DefRole = DEFENSIVE_ROLE.IDLE;
        }

        // 2. Chaser 결정
        BaseballPlayer chaser = GetBestFielderForCatch(landPos);
        if (chaser != null)
        {
            chaser.DefRole = DEFENSIVE_ROLE.CHASER;
            chaser.RoleTargetPosition = landPos;
        }

        bool isOutfieldHit = landPos.z > 25f;

        // 3. 역할 분배
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i] == null || !_players[i].gameObject.activeSelf) continue;
            if (i == _attackNum || _players[i] == chaser) continue;

            // 각 포지션 클래스(ShortStop, SecondBase 등)에서 오버라이드한 고유 역할(예: 베이스 커버) 우선 할당
            if (_players[i].AssignSpecialRole(chaser, landPos)) continue;

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
        setActivePlayer(_attackNum, false);
        _attackNum = (_attackNum + 1) % 9;
        setActivePlayer(_attackNum, true);
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
        if ((int)t == _attackNum || t == PLAYER_TYPE.E_PITCHER) ChangeAttacker();
        else setActivePlayer((int)t, false);
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
