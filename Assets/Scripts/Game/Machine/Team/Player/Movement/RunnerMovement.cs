using UnityEngine;

public class RunnerMovement : Movement
{
    public override bool SetTarget(Vector3 basePos, Vector3 myPos, float range, MOVEMENT_TYPE mT)
    {
        setTarget(basePos);
        SetMovementType(mT);
        return false;
    }
}
