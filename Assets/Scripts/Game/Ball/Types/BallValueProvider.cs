using UnityEngine;

public class BallValueProvider
{
    Transform _transform;
    public BallValueProvider(Transform trans)
    {
        _transform = trans;
    }

    public float GetSpeed(PITCHING_MODE mode, float defaultSpeed)
    {
        switch (mode)
        {
            case PITCHING_MODE.E_TWO_SEAM:
            case PITCHING_MODE.E_CUTTER: return defaultSpeed * 0.95f;

            case PITCHING_MODE.E_SLIDER:
            case PITCHING_MODE.E_CURVE: return defaultSpeed * 0.9f;

            case PITCHING_MODE.E_SLOW_CURVE: return defaultSpeed * 0.65f;

            case PITCHING_MODE.E_CHANGE_UP:
            case PITCHING_MODE.E_SPLITTER: return defaultSpeed * 0.8f;

            //case PITCHING_MODE.E_KNUCKLE: return defaultBallSpeed;

            default: return defaultSpeed;
        }
    }

    public float GetMomentTime(PITCHING_MODE mode)
    {
        switch (mode)
        {
            case PITCHING_MODE.E_FOUR_SEAM: return 0f;

            case PITCHING_MODE.E_TWO_SEAM: return 0.55f;
            case PITCHING_MODE.E_CUTTER:
            case PITCHING_MODE.E_SLIDER: return 0.6f;

            case PITCHING_MODE.E_CHANGE_UP: return 0.7f;
            case PITCHING_MODE.E_CURVE: return 0.5f;
            case PITCHING_MODE.E_SLOW_CURVE: return 0.375f;
            //case PITCHING_MODE.E_KNUCKLE: return knuckle();
            default: return 0.5f;
        }
    }

    Vector3 down()
    {
        return _transform.up * -1;
    }
    
    Vector3 left()
    {
        return _transform.right * -1;
    }

    Vector3 defaultYAxisDirection(PITCHING_MODE type)
    {
        switch (type)
        {
            case PITCHING_MODE.E_RISING_FOUR_SEAM: return _transform.up;

            case PITCHING_MODE.E_SINKER:
            case PITCHING_MODE.E_CURVE:
            case PITCHING_MODE.E_SLOW_CURVE:
            case PITCHING_MODE.E_CHANGE_UP:
            case PITCHING_MODE.E_SLIDER:
            case PITCHING_MODE.E_SPLITTER: return down();

            default: return Vector3.zero;
        }
    }

    Vector3 defaultXAxisDirection(PITCHING_MODE type)
    {
        switch (type)
        {
            case PITCHING_MODE.E_SINKER:
            case PITCHING_MODE.E_TWO_SEAM:
            case PITCHING_MODE.E_CHANGE_UP: return _transform.right;

            case PITCHING_MODE.E_CUTTER: 
            case PITCHING_MODE.E_SLIDER: return left();

            //case PITCHING_MODE.E_KNUCKLE: return Vector3.zero;

            default: return Vector3.zero;
        }
    }

    float defaultYAxisForce(PITCHING_MODE type)
    {
        switch (type)
        {
            case PITCHING_MODE.E_RISING_FOUR_SEAM: return 20f;

            case PITCHING_MODE.E_SINKER: return 15f;
            case PITCHING_MODE.E_TWO_SEAM: return 9f;

            case PITCHING_MODE.E_CUTTER: return 6f;

            case PITCHING_MODE.E_CURVE:
            case PITCHING_MODE.E_SLOW_CURVE: return 30f;

            case PITCHING_MODE.E_CHANGE_UP: return 12f;

            case PITCHING_MODE.E_SLIDER: return 15f;

            case PITCHING_MODE.E_SPLITTER: return 10f;
            default: return 0;
        }
    }

    float defaultXAxisForce(PITCHING_MODE type)
    {
        switch (type)
        {
            case PITCHING_MODE.E_SINKER: 
            case PITCHING_MODE.E_TWO_SEAM:
            case PITCHING_MODE.E_CUTTER:
            case PITCHING_MODE.E_SLIDER: return 40f;

            case PITCHING_MODE.E_CHANGE_UP: return 4f;


            default: return 0f;
        }
    }

    Vector3 defaultAngle(PITCHING_MODE type)
    {
        return defaultXAxisDirection(type) * defaultXAxisForce(type)
            + defaultYAxisDirection(type) * defaultYAxisForce(type);
    }

    //Vector3 defaultAngle(PITCHING_MODE type)
    //{

        //switch (type)
        //{
        //    case PITCHING_MODE.E_RISING_FOUR_SEAM: return _transform.up * 0.7f;
        //    case PITCHING_MODE.E_TWO_SEAM: return _transform.right * (isRightHanded ? 1 : -1) * 2.8f;
        //    case PITCHING_MODE.E_SINKER: return down() * 3f;
        //    case PITCHING_MODE.E_CUTTER: return _transform.right * (isRightHanded ? -1 : 1) * 0.8f;

        //    case PITCHING_MODE.E_CURVE: 
        //    case PITCHING_MODE.E_SLOW_CURVE: return down() * 2.25f;

        //    case PITCHING_MODE.E_CHANGE_UP: return down() * 2.25f + _transform.right * (isRightHanded ? 1 : -1) * 1.25f;

        //    case PITCHING_MODE.E_SLIDER: return down() * 3.25f + _transform.right * (isRightHanded ? -1 : 1) * 2.15f;

        //    //case PITCHING_MODE.E_KNUCKLE: return Vector3.zero;

        //    case PITCHING_MODE.E_SPLITTER: return down() * 4.25f;
        //    default: return Vector3.zero;
        //}
    //}

    //public Vector3 GetAngle(PITCHING_MODE type, bool isRightHanded)
    //{
    //    switch (type)
    //    {
    //        case PITCHING_MODE.E_RISING_FOUR_SEAM: return _transform.up * Random.Range(0.6f, 0.8f);
    //        case PITCHING_MODE.E_TWO_SEAM: return _transform.right * (isRightHanded ? 1 : -1) * Random.Range(2.6f, 3f);
    //        case PITCHING_MODE.E_SINKER: return down() * Random.Range(2f, 3.5f);
    //        case PITCHING_MODE.E_CUTTER: return _transform.right * (isRightHanded ? -1 : 1) * Random.Range(0.6f, 1f);

    //        case PITCHING_MODE.E_CURVE:
    //        case PITCHING_MODE.E_SLOW_CURVE: return down() * Random.Range(2f, 2.5f);

    //        case PITCHING_MODE.E_CHANGE_UP: return down() * Random.Range(2f, 2.5f) + _transform.right * (isRightHanded ? 1 : -1) * Random.Range(1f, 1.5f);

    //        case PITCHING_MODE.E_SLIDER: return down() * Random.Range(3f, 3.5f) + _transform.right * (isRightHanded ? -1 : 1) * Random.Range(2f, 2.3f);

    //        //case PITCHING_MODE.E_KNUCKLE: return Vector3.zero;

    //        case PITCHING_MODE.E_SPLITTER: return down() * Random.Range(4f, 4.5f);
    //        default: return Vector3.zero;
    //    }
    //}

    float getRandomRange(float mid, float range)
    {
        return Random.Range(mid - range, mid + range);
    }

    float intendedErrorYAxisForce(PITCHING_MODE type)
    {
        float dForce = defaultYAxisForce(type);
        switch (type)
        {
            case PITCHING_MODE.E_RISING_FOUR_SEAM: return getRandomRange(dForce, 0.15f);
            case PITCHING_MODE.E_TWO_SEAM: return getRandomRange(dForce, 0.25f);
            case PITCHING_MODE.E_SINKER: return getRandomRange(dForce, 0.25f);

            case PITCHING_MODE.E_CURVE:
            case PITCHING_MODE.E_SLOW_CURVE: return getRandomRange(dForce, 0.25f);

            case PITCHING_MODE.E_CHANGE_UP: return getRandomRange(dForce, 0.25f);

            case PITCHING_MODE.E_SLIDER: return getRandomRange(dForce, 0.25f);

            case PITCHING_MODE.E_SPLITTER: return getRandomRange(dForce, 0.25f);
            default: return 0;
        }
    }

    float intendedErrorXAxisForce(PITCHING_MODE type)
    {
        float dForce = defaultXAxisForce(type);
        switch (type)
        {
            case PITCHING_MODE.E_SINKER:
            case PITCHING_MODE.E_TWO_SEAM: return getRandomRange(dForce, 0.25f);

            case PITCHING_MODE.E_CUTTER: return getRandomRange(dForce, 0.2f);

            case PITCHING_MODE.E_CHANGE_UP: return getRandomRange(dForce, 0.25f);

            case PITCHING_MODE.E_SLIDER: return getRandomRange(dForce, 0.15f);

            //case PITCHING_MODE.E_KNUCKLE: return Vector3.zero;

            default: return 0;
        }
    }

    public Vector3 GetAngle(PITCHING_MODE type)
    {
        return defaultXAxisDirection(type) * intendedErrorXAxisForce(type)
            + defaultYAxisDirection(type) * intendedErrorYAxisForce(type);
    }

    public Vector3 GetOffsetAngle(PITCHING_MODE type, Vector3 angle)
    {
        return angle - defaultAngle(type);
    }
}
