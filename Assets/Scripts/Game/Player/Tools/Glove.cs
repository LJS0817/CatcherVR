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
