using TMPro;
using UnityEngine;

public class BallTextStatement : MonoBehaviour
{
    Animator _ani;
    TextMeshPro _text;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        _ani = GetComponent<Animator>();
        _text = transform.GetChild(0).GetComponent<TextMeshPro>();
    }

    public void init(string text, Vector3 rot)
    {
        setText(text);
        setRotate(rot);
    }

    public void Selected() { changeState(true); }
    public void Dismissed() { changeState(false); }
    void setText(string text) { _text.text = text; }
    void setRotate(Vector3 value)
    {
        transform.Rotate(value);
        _text.transform.Rotate(value);
    }

    void changeState(bool b) { _ani.SetBool("Selected", b); }
}
