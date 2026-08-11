using UnityEngine;

public class PlayerRole
{
    protected Movement _movement;
    protected BASE_TYPE _base;
    protected Animator _ani;
    protected PLAYER_TYPE _type;
    
    /// <summary>
    /// 움직임 없이 잡을 수 있는 범위
    /// x - 좌우
    /// y - 점프 시
    /// z - 움직임 범위
    /// </summary>
    protected Vector3 _range;

    protected Ball _ball;
    protected Transform _my;
    protected BaseballPlayer _bp; // 최적화를 위해 상위 컨트롤러를 캐싱

    protected Transform _item;

    /// <summary>
    /// <para>0 - Left</para>
    /// <para>1 - Right</para>
    /// <para>2 - Swing Offset</para>
    /// </summary>
    protected Transform _offsets;

    protected RoleController _controller;

    public virtual void init(float h, Transform tool, PLAYER_TYPE t, Ball ball, Transform player, BASE_TYPE bT=BASE_TYPE.E_SELF) {
        _range = new Vector3(h, 4f, h);
        _base = bT;
        _type = t;
        _my = player;
        
        if (_my != null)
        {
            _ani = _my.GetComponent<Animator>();
            _bp = _my.GetComponent<BaseballPlayer>();
            _offsets = _my.GetChild(0).GetChild(1).GetChild(2);
        }

        _item = tool;
        _ball = ball;
        
        _item.GetChild(0).gameObject.SetActive(false);
        if(_item.childCount > 1) _item.GetChild(1).gameObject.SetActive(false);
    }
    
    public virtual void update(float speed)
    {
        Move(speed);
    }
    public virtual void fixedUpdate()
    {
        PhysicsMoves();
    }

    protected virtual void Move(float speed) { }

    protected virtual void PhysicsMoves() { }

    public virtual void movementInit()
    {
        _movement.init();
    }

    protected virtual bool setMovementTarget(Vector3 pos, Vector3 playerPos, float range, MOVEMENT_TYPE mt)
    {
        return _movement.SetTarget(pos, playerPos, range, mt);
    }

    public virtual void BallEventListener(Vector3 playerPos, Vector3 startPos, Vector3 endPos, Vector3 inc) { }

    public Vector3 GetRange()
    {
        return _range;
    }

    protected bool isMyDirection(float value, float angle)
    {
        return value > angle;
    }

    protected float getDistanceBall()
    {
        return Vector3.Distance(getCenter(), _ball.GetPosition());
    }

    Vector3 getCenter()
    {
        return new Vector3(_my.position.x, _my.position.y + _range.x, _my.position.z);
    }

    public virtual void Increase(int i) { }

    public virtual void SetController(Transform pool, Transform point, Transform target, float speed=0f) { }

    public virtual void ResetRole()
    {
        if (_movement != null)
        {
            _movement.SetMovementType(MOVEMENT_TYPE.E_STAY);
        }
        if (_item != null)
        {
            _item.localPosition = Vector3.zero;
        }
    }

    public virtual RoleController GetController() { return _controller; }

    public Transform GetItemTransform() { return _item; }

    protected Movement getMovement() { return _movement; }

    public void SetRange(float z) {
        _range.z = z;
    }

    public virtual void OnTriggerEnter(Collider collider) { }
}
