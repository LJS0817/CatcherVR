using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class Player : MonoBehaviour
{
    Vector3 _initPos;

    private void Start()
    {
        _initPos = transform.position;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            transform.position = _initPos;
        }
    }

    public void ReleaseObject(SelectExitEventArgs args)
    {
        if(args.interactableObject.transform.tag.Contains("Ball"))
        {
            args.interactableObject.transform.GetComponent<Ball>().ThrowBallByPlayer();
        }
    }
}
