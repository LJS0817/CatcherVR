using UnityEngine;


public class CatchMovement : Movement
{
    public CatchMovement()
    {
        init();
    }

    public override bool SetTarget(Vector3 ballPos, Vector3 myPos, float range, MOVEMENT_TYPE mT)
    {
        if (Vector3.Distance(ballPos, myPos) < range || range < 0f) {
            
            // 타구 추적일 경우 첫 발 딜레이 적용 (0.2초 ~ 0.35초 랜덤성 부여)
            if ((mT == MOVEMENT_TYPE.E_PREDICT_PATH || mT == MOVEMENT_TYPE.E_FOLLOW_BALL) && !isMoving())
            {
                float delay = Random.Range(0.2f, 0.35f);
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
