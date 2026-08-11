using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

public class PlayerInputManager : MonoBehaviour
{
    public InputActionReference TargetButton;
    public InputActionReference ShootButton;
    public Transform Target;
    public Transform Glove_Player;

    public PitchingMachine Pitcher;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        TargetButton.action.started += setTarget;
        ShootButton.action.started += throwBall;
        //resetButton.action.performed += test;
        //resetButton.action.canceled += cTest;
    }

    private void OnDestroy()
    {
        TargetButton.action.started -= setTarget;
        ShootButton.action.started -= throwBall;
        //resetButton.action.performed -= test;
        //resetButton.action.canceled -= cTest;
    }

    // Update is called once per frame
    void Update()
    {

    }

    void setTarget(InputAction.CallbackContext cxt) 
    {
        Target.position = new Vector3(Glove_Player.position.x, Glove_Player.position.y + 0.1f, Target.position.z);
    }

    void throwBall(InputAction.CallbackContext cxt)
    {
        Pitcher.ThrowBall();
    }
}
