using System;
using System.Collections;
using System.Net;
using UnityEngine;


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
    NetworkService Net = new NetworkService();


    public ENetworkState GetState()
    {
        return Net.GetState();
    }

    public bool IsServer()
    {
        return Net.IsServer();
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
        while (LogQueue.GetNext(out msg))
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
