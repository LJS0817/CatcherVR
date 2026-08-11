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
            setTarget(ballPos);
            SetMovementType(mT);
            //Debug.Log(mT);
        }
        else
        {
            init();
        }

        return isMoving();
    }
}
