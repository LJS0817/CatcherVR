using UnityEngine;
using UnityEngine.InputSystem;

public class BallSelector : MonoBehaviour
{
    public InputActionReference SelectStick;

    public Transform SelectorUI;
    public Transform SelectorPointer;

    public GameObject TextPrefab;
    public BallTextManager TextMng;

    BallData _ballInfo;
    private void Awake()
    {
        _ballInfo = GetComponent<BallData>();
        setSelectorText();
        setAction();
    }


    void setAction()
    {
        SelectStick.action.started += (ctx) =>
        {
            //setSelectorPosition(ctx);
        };
        SelectStick.action.performed += (ctx) =>
        {
            setSelectorPosition(ctx);
        };
        SelectStick.action.canceled += (ctx) =>
        {
            //_ballInfo.PositionToType(SelectorPointer.localPosition);
            _ballInfo.setType(TextMng.GetCurrentIndex());
            TextMng.SetCurrentText(_ballInfo.getText((int)_ballInfo.getType()));
        };
    }

    void setSelectorPosition(InputAction.CallbackContext ctx)
    {
        Vector2 pos = (Vector2)ctx.ReadValueAsObject();
        pos.x *= -1;
        PITCHING_MODE newType = _ballInfo.PointToType(pos);
        if (_ballInfo.isValidType(newType))
        {
            TextMng.Disable();
            TextMng.Enable((int)newType);
            TextMng.SetCurrentText(_ballInfo.getText((int)newType));
        }
    }

    void setSelectorText()
    {
        for(int i = 0; i < (int)PITCHING_MODE.E_LENGTH; i++)
        {
            BallTextStatement text = Instantiate(TextPrefab, SelectorUI.GetChild(1)).GetComponent< BallTextStatement>();
            text.init(_ballInfo.getText(i), getTextRotationValue(i));
        }
    }

    Vector3 getTextRotationValue(int idx)
    {
        return transform.forward * 36 * idx;
    }
}
