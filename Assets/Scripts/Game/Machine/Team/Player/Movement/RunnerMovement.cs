using UnityEngine;

public class RunnerMovement : Movement
{
    public override bool SetTarget(Vector3 basePos, Vector3 myPos, float range, MOVEMENT_TYPE mT)
    {
        return SetTarget(basePos, myPos, range, mT, -1f);
    }

    public override bool SetTarget(Vector3 basePos, Vector3 myPos, float range, MOVEMENT_TYPE mT, float customDelay)
    {
        setTarget(basePos);
        SetMovementType(mT);
        if (customDelay > 0f)
        {
            SetTargetWithDelay(basePos, customDelay);
        }
        return isMoving();
    }
}
