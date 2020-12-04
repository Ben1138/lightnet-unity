using System;
using UnityEngine;

public static class SerializationHelper
{
    public static float FromBytes(byte[] data, ref int offset)
    {
        Debug.Assert(offset + sizeof(float) <= data.Length);
        float value = BitConverter.ToSingle(data, offset);
        offset += sizeof(float);
        return value;
    }

    public static void FromBytes(byte[] data, ref int offset, ref Vector3 vector)
    {
        vector.x = FromBytes(data, ref offset);
        vector.y = FromBytes(data, ref offset);
        vector.z = FromBytes(data, ref offset);
    }

    public static void FromBytes(byte[] data, ref int offset, ref Quaternion quat)
    {
        quat.x = FromBytes(data, ref offset);
        quat.y = FromBytes(data, ref offset);
        quat.z = FromBytes(data, ref offset);
        quat.w = FromBytes(data, ref offset);
    }

    public static void ToBytes(ulong value, byte[] data, ref int offset)
    {
        Debug.Assert(offset + sizeof(float) < data.Length);
        byte[] buffer = BitConverter.GetBytes(value);
        Array.Copy(buffer, 0, data, offset, buffer.Length);
        offset += buffer.Length;
    }

    public static void ToBytes(float value, byte[] data, ref int offset)
    {
        Debug.Assert(offset + sizeof(float) <= data.Length);
        byte[] buffer = BitConverter.GetBytes(value);
        Array.Copy(buffer, 0, data, offset, buffer.Length);
        offset += buffer.Length;
    }

    public static void ToBytes(ref Vector3 vector, byte[] data, ref int offset)
    {
        ToBytes(vector.x, data, ref offset);
        ToBytes(vector.y, data, ref offset);
        ToBytes(vector.z, data, ref offset);
    }

    public static void ToBytes(ref Quaternion quat, byte[] data, ref int offset)
    {
        ToBytes(quat.x, data, ref offset);
        ToBytes(quat.y, data, ref offset);
        ToBytes(quat.z, data, ref offset);
        ToBytes(quat.w, data, ref offset);
    }
}


public enum ENetDataType : byte
{
    UserState = 1,
    ExperimentState = 2
}

public class UserState
{
    const int SIZE =
        // NOTE: using e.g. sizeof(Vector3) is not allowed...
        // so we need to always use sizeof(float) instead

        sizeof(byte) +          // header     (ENetDataType.UserState)
        sizeof(float) +         // float      LeverPosition;
        sizeof(float) * 3 +     // Vector3    CannonPosition;
        sizeof(float) * 4 +     // Quaternion CannonRotation;
                                // 
        sizeof(float) * 3 +     // Vector3    HeadPosition;
        sizeof(float) * 4 +     // Quaternion HeadRotation;
        sizeof(float) * 3 +     // Vector3    HandLeftPosition;
        sizeof(float) * 4 +     // Quaternion HandLeftRotation;
        sizeof(float) * 3 +     // Vector3    HandRightPosition;
        sizeof(float) * 4;      // Quaternion HandRightRotation;


    public float LeverPosition;
    public Vector3 CannonPosition;
    public Quaternion CannonRotation;

    public Vector3 HeadPosition;
    public Quaternion HeadRotation;
    public Vector3 HandLeftPosition;
    public Quaternion HandLeftRotation;
    public Vector3 HandRightPosition;
    public Quaternion HandRightRotation;


    public void Deserialize(byte[] data)
    {
        int head = 0;
        Debug.Assert(data.Length >= SIZE);
        Debug.Assert((ENetDataType)data[head] == ENetDataType.UserState);
        head += sizeof(byte);

        LeverPosition = SerializationHelper.FromBytes(data, ref head);
        SerializationHelper.FromBytes(data, ref head, ref CannonPosition);
        SerializationHelper.FromBytes(data, ref head, ref CannonRotation);
        SerializationHelper.FromBytes(data, ref head, ref HeadPosition);
        SerializationHelper.FromBytes(data, ref head, ref HeadRotation);
        SerializationHelper.FromBytes(data, ref head, ref HandLeftPosition);
        SerializationHelper.FromBytes(data, ref head, ref HandLeftRotation);
        SerializationHelper.FromBytes(data, ref head, ref HandRightPosition);
        SerializationHelper.FromBytes(data, ref head, ref HandRightRotation);
    }

    public byte[] Serialize()
    {
        byte[] data = new byte[SIZE];

        int head = 0;
        data[head] = (byte)ENetDataType.UserState; head += sizeof(byte);

        SerializationHelper.ToBytes(LeverPosition, data, ref head);
        SerializationHelper.ToBytes(ref CannonPosition, data, ref head);
        SerializationHelper.ToBytes(ref CannonRotation, data, ref head);
        SerializationHelper.ToBytes(ref HeadPosition, data, ref head);
        SerializationHelper.ToBytes(ref HeadRotation, data, ref head);
        SerializationHelper.ToBytes(ref HandLeftPosition, data, ref head);
        SerializationHelper.ToBytes(ref HandLeftRotation, data, ref head);
        SerializationHelper.ToBytes(ref HandRightPosition, data, ref head);
        SerializationHelper.ToBytes(ref HandRightRotation, data, ref head);

        return data;
    }
}