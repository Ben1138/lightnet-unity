﻿using System;
using System.Collections;
using System.Net;
using UnityEngine;


public enum ENetDataType : byte
{
    UserState = 1,
    ExperimentState = 2
}

public static class SerHelper
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


    public float      LeverPosition;
    public Vector3    CannonPosition;
    public Quaternion CannonRotation;

    public Vector3    HeadPosition;
    public Quaternion HeadRotation;
    public Vector3    HandLeftPosition;
    public Quaternion HandLeftRotation;
    public Vector3    HandRightPosition;
    public Quaternion HandRightRotation;


    public void Deserialize(byte[] data)
    {
        int head = 0;
        Debug.Assert(data.Length >= SIZE);
        Debug.Assert((ENetDataType)data[head] == ENetDataType.UserState);
        head += sizeof(byte);

        LeverPosition = SerHelper.FromBytes(data, ref head);
        SerHelper.FromBytes(data, ref head, ref CannonPosition);
        SerHelper.FromBytes(data, ref head, ref CannonRotation);
        SerHelper.FromBytes(data, ref head, ref HeadPosition);
        SerHelper.FromBytes(data, ref head, ref HeadRotation);
        SerHelper.FromBytes(data, ref head, ref HandLeftPosition);
        SerHelper.FromBytes(data, ref head, ref HandLeftRotation);
        SerHelper.FromBytes(data, ref head, ref HandRightPosition);
        SerHelper.FromBytes(data, ref head, ref HandRightRotation);
    }

    public byte[] Serialize()
    {
        byte[] data = new byte[SIZE];

        int head = 0;
        data[head] = (byte)ENetDataType.UserState; head += sizeof(byte);

        SerHelper.ToBytes(LeverPosition,         data, ref head);
        SerHelper.ToBytes(ref CannonPosition,    data, ref head);
        SerHelper.ToBytes(ref CannonRotation,    data, ref head);
        SerHelper.ToBytes(ref HeadPosition,      data, ref head);
        SerHelper.ToBytes(ref HeadRotation,      data, ref head);
        SerHelper.ToBytes(ref HandLeftPosition,  data, ref head);
        SerHelper.ToBytes(ref HandLeftRotation,  data, ref head);
        SerHelper.ToBytes(ref HandRightPosition, data, ref head);
        SerHelper.ToBytes(ref HandRightRotation, data, ref head);

        return data;
    }
}

public class ReceivedUserStateEventArgs : EventArgs
{
    public UserState State;

    public ReceivedUserStateEventArgs(UserState state)
    {
        State = state;
    }
}

public class Networking : MonoBehaviour
{
    public EventHandler<ReceivedUserStateEventArgs> OnUserStateReceived;
    public bool IsServer { get { return Net.IsServer; } }

    NetworkService Net = new NetworkService();


    public ENetworkState GetState()
    {
        return Net.GetState();
    }
    
    public bool StartAsServer()
    {
        return Net.StartServer(42069, 42169);
    }

    public bool StartAsClient(string ip)
    {
        IPAddress address;
        if (!IPAddress.TryParse(ip, out address))
        {
            Debug.LogError("Invalid IP-Address!");
            return false;
        }
        return Net.StartClient(address, 42069, 42169);
    }

    public bool Close()
    {
        return Net.Close();
    }

    public void SendUserState(UserState userState)
    {
        if (Net.GetState() != ENetworkState.Running)
        {
            Debug.LogError("Do not try to send data if network is not running!");
            return;
        }

        Net.SendMessage(ENetChannel.Unreliable, userState.Serialize());
    }

    void Update()
    {
        LogMessage msg;
        while (ThreadLogging.GetNext(out msg))
        {
            switch (msg.Type)
            {
                case ELogType.Info:
                    Debug.Log(msg.Message);
                    break;
                case ELogType.Warning:
                    Debug.LogWarning(msg.Message);
                    break;
                case ELogType.Error:
                    Debug.LogError(msg.Message);
                    break;
            }
        }

        if (Net.GetState() == ENetworkState.Running)
        {
            byte[] last = null;

            // we're only interested in the most recent one
            while (Net.GetNextMessage(ENetChannel.Unreliable, out byte[] data)) 
            {
                last = data;
            }

            if (last != null && last.Length > 0)
            {
                if ((ENetDataType)last[0] == ENetDataType.UserState)
                {
                    UserState userState = new UserState();
                    userState.Deserialize(last);
                    ReceivedUserStateEventArgs args = new ReceivedUserStateEventArgs(userState);
                    OnUserStateReceived?.Invoke(this, args);
                }
            }
        }
    }
}
