using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Dynamic;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class BallType
{
    //private Vector3 _gravity;
    private float _movementDistance;
    private Vector3 _additional_force;
    
    public void init(float dist, Vector3 add)
    {
        _movementDistance = dist;
        _additional_force = add;
    }

    public void reset()
    {
        _movementDistance = -1;
        _additional_force = Vector3.zero;
    }

    public Vector3 GetSpeed(float dist) {
        if (IsNeedGravity(dist)) return Vector3.zero;
        else return GetForce();
    }

    public Vector3 GetForce()
    {
        return _additional_force;
    }
    public bool Do_not_AddForce()
    {
        return _additional_force == Vector3.zero || _movementDistance < 0;
    }

    public bool IsNeedGravity(float dist)
    {
        return dist > _movementDistance || dist < 0;
    }
}