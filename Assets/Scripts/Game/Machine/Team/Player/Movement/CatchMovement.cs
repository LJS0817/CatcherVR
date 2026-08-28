using UnityEngine;


public class CatchMovement : Movement
{
    public CatchMovement()
    {
        init();
    }

    public override bool SetTarget(Vector3 ballPos, Vector3 myPos, float range, MOVEMENT_TYPE mT)
    {
        return SetTarget(ballPos, myPos, range, mT, -1f);
    }

    public override bool SetTarget(Vector3 ballPos, Vector3 myPos, float range, MOVEMENT_TYPE mT, float customDelay)
    {
        if (Vector3.Distance(ballPos, myPos) < range || range < 0f) {
            
            // 타구 추적 또는 이동 시작 시 딜레이 적용
            if ((mT == MOVEMENT_TYPE.E_PREDICT_PATH || mT == MOVEMENT_TYPE.E_FOLLOW_BALL || mT == MOVEMENT_TYPE.E_BASE) && !isMoving())
            {
                float delay = customDelay >= 0f ? customDelay : Random.Range(0.2f, 0.35f);
                SetTargetWithDelay(ballPos, delay);
            }
            else
            {
                setTarget(ballPos);
            }
            
            SetMovementType(mT);
        }
        else
        {
            init();
        }

        return isMoving();
    }
}
