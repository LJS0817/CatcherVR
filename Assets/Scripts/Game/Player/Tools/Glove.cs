using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class Glove : MonoBehaviour
{
    public Transform BallOffset;
    public InputActionReference CatchInput;
    public GameObject InsideGloveCollider;

    Animator _ani;
    Ball _ball;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _ani = GetComponent<Animator>();
        _ball = null;
    }

    public void Get()
    {
        CatchInput.action.started += changeStateGlove;
        CatchInput.action.canceled += changeStateGlove;
    }

    public void Lost()
    {
        CatchInput.action.started -= changeStateGlove;
        CatchInput.action.canceled -= changeStateGlove;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.name.Contains("ball"))
        {
            _ball = other.GetComponent<Ball>();
            
            // 플레이어가 노바운드로 잡으면 아웃 처리 (플라이 아웃 / 파울 플라이 아웃)
            if (_ball.CanBeDirectOut() && _ball._isBattedBall)
            {
                _ball.IsFoul = false; // 파울 플라이 아웃이면 데드볼 처리 취소
                CountsProvider.provider.IncreaseCount(COUNT_TYPE.E_OUT, null);
                GamePlayerProvider.provider.PlayerOut(PLAYER_TYPE.E_PITCHER); // 현재 타자 아웃
            }
            
            _ball.GrabBall(BallOffset);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        
    }

    void changeStateGlove(InputAction.CallbackContext ctx)
    {
        bool grab = !isGrab();
        if (_ball != null && !grab) { 
            _ball.LostBall();
            _ball.ResetVelocity();  
            _ball = null;
        }
        InsideGloveCollider.SetActive(grab);
        _ani.SetBool("Grab", grab);
    }

    bool isGrab()
    {
        return _ani.GetBool("Grab");
    }
}
