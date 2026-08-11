using UnityEngine;

public enum MOVEMENT_TYPE
{
    E_STAY,

    E_END_POINT,
    E_JUMP,
    E_PREDICT_PATH,
    E_FOLLOW_BALL,

    E_BASE,
    E_PRE_RUN,
    E_RUN,
}

public class Movement
{
    Vector3 _target;
    MOVEMENT_TYPE _type;

    public void init()
    {
        _target = Vector3.zero;
        _type = MOVEMENT_TYPE.E_STAY;
    }

    public void SetMovementType(MOVEMENT_TYPE mT) { _type = mT; }
    public bool CompareMovementType(MOVEMENT_TYPE t) { return _type == t; }
    public MOVEMENT_TYPE GetMovementType() { return _type; }

    public Vector3 GetTarget() { return _target; }

    public virtual bool SetTarget(Vector3 target, Vector3 myPos, float range, MOVEMENT_TYPE mT) { return false; }

    protected void setTarget(Vector3 vec) { _target = vec; }

    public bool isMoving() { return _target != Vector3.zero && !CompareMovementType(MOVEMENT_TYPE.E_STAY); }

    public Vector3 GetMovementPosition(Vector3 myPos, Vector3 target, float speed) {
        return Vector3.MoveTowards(myPos, target, speed * Time.deltaTime);
    }
}
