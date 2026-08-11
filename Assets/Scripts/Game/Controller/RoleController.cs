using UnityEngine;

public class RoleController
{
    protected Transform _pool;
    protected PITCHING_MODE _mode;
    protected Transform _itemPoint;
    protected float _defaultSpeed;

    public virtual void Init(Transform pool, Transform _point, Transform target, float speed) {
        _mode = PITCHING_MODE.E_FOUR_SEAM;
        _itemPoint = _point;
        _defaultSpeed = speed;
        _pool = pool;
    }

    public virtual void Action(Transform tran) { }

    public virtual void ResetValue(int i=0) { }
}
