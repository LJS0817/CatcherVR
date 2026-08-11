using UnityEngine;

public enum PLAYER_TYPE
{
    E_FIRST_BASE,
    E_SECOND_BASE,
    E_SHORT_STOP,
    E_THIRD_BASE,
    
    E_RIGHT_FILED,
    E_CENTER_FIELD,
    E_LEFT_FILED,

    E_PITCHER,
}

public enum BASE_TYPE
{
    E_SELF,
    E_BALL,
    E_FIRST_BASE,
    E_SECOND_BASE,
    E_THIRD_BASE,
    E_HOME_BASE,
}

public class BaseballPlayer : MonoBehaviour
{
    protected PLAYER_TYPE _type;

    protected Ball _ball;

    protected float _moveSpeed;
    protected float _height = 1.8f;

    Vector3 _initPos;

    /// <summary>
    /// Defencer class | Attacker class
    /// </summary>
    protected PlayerRole _role;
    protected Animator _ani;

    Transform _ballPool;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        update();
    }

    private void FixedUpdate()
    {
        fixedUpdate();
    }

    protected virtual void init(BASE_TYPE bT=BASE_TYPE.E_SELF)
    {
        _initPos = transform.position;
        _moveSpeed = 4f;
        _ball = _ball != null ? _ball : null;
        _role.init(_height, transform.GetChild(0).GetChild(1).GetChild(0), _type, _ball, transform, bT);
        setRoleController();
    }

    protected virtual void setRoleController(Transform trans=null)
    {
        _role.SetController(_ballPool, trans == null ? _role.GetItemTransform() : trans, _ball.transform);
    }
    
    protected virtual void update() { 
        if(Input.GetKeyDown(KeyCode.A))
        {
            transform.position = _initPos;
            _role.movementInit();
        }
        _role.update(_moveSpeed);
    }

    protected virtual void fixedUpdate()
    {
        _role.fixedUpdate();
    }

    public Transform GetItem()
    {
        return _role.GetItemTransform();
    }

    public void EnterTheGame(Transform pool, Ball ball, bool isAttacker)
    {
        _ani = GetComponent<Animator>();
        _ballPool = pool;
        focusBall(ball, isAttacker ? new Attacker() : new Defencer());
        setSide(isAttacker);

        init(); 
    }

    public void JoinGame()
    {
        gameObject.SetActive(true);
        transform.position = BasePositionProvider.provider.HitterBox.position;
        transform.rotation = BasePositionProvider.provider.HitterBox.rotation;
        _initPos = transform.position;
        setSide(true);
    }

    public void ExitGame()
    {
        gameObject.SetActive(false);
    }

    void focusBall(Ball ball, PlayerRole role)
    {
        _ball = ball;
        _role = role;
        _ball.AddListener((Vector3 startPos, Vector3 endPos, Vector3 inc) =>
        {
            _role.BallEventListener(transform.position, startPos, endPos, inc);
        });
    }

    void setSide(bool isAttackSide)
    {
        _ani.SetBool("isAttacker", isAttackSide);
    }

    public void Increase(int i)
    {
        _role.Increase(i);
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log(transform.name + "      " + collision.transform.name);
    }

    private void OnTriggerEnter(Collider other)
    {
        _role.OnTriggerEnter(other);
    }
}
