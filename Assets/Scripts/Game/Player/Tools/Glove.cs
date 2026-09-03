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
            //
            // [수정] 예전에는 아웃 카운트를 올린 뒤 PlayerOut() 이 내부에서 또 타자를 교체해
            //        아웃이 한 번인데 타순은 두 칸 넘어가는 문제가 있었다.
            //        아웃 기록은 RegisterBatterOut() 한 번으로 끝내고,
            //        타자 교체는 플레이가 마무리될 때(Team.CompletePlay) 한 번만 수행한다.
            if (_ball.CanBeDirectOut() && _ball._isBattedBall)
            {
                _ball.SetBattedBall(false);
                _ball.IsFoul = false; // 파울 플라이 아웃이면 데드볼 처리 취소
                if (GamePlayerProvider.provider != null) GamePlayerProvider.provider.RegisterBatterOut();
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
            // 글러브에서 바로 공을 떨어뜨려 던지는 것을 방지 (다른 손으로 빼서 던지도록 강제)
            // _ball.LostBall();
            // _ball.ResetVelocity();  
            // _ball = null;
        }
        InsideGloveCollider.SetActive(grab);
        _ani.SetBool("Grab", grab);
    }

    bool isGrab()
    {
        return _ani.GetBool("Grab");
    }
}
