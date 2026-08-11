using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BallTextManager : MonoBehaviour
{
    public TextMeshPro CurText;

    List<BallTextStatement> _list;

    int _curIndex;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _list = new List<BallTextStatement>();
        for(int i = 0; i < transform.childCount; i++)
        {
            _list.Add(transform.GetChild(i).GetComponent<BallTextStatement>());
        }
        Enable(0);
    }
    
    public void Enable(int idx) { _curIndex = idx; changeState(idx, true); }
    public void Disable() { changeState(_curIndex, false); }
    void Disable(int idx) { changeState(idx, false); }

    public void SetCurrentText(string str) { CurText.text = str; }
    public PITCHING_MODE GetCurrentIndex() { return (PITCHING_MODE)_curIndex; }

    void changeState(int idx, bool b)
    {
        if(b) _list[idx].Selected();
        else _list[idx].Dismissed();
    }
}
