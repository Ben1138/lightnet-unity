using System;
using System.Net;
using UnityEngine;


public class ConnectionEventArgs : EventArgs
{
    public ConnectionHandle Handle;

    public ConnectionEventArgs(ConnectionHandle handle)
    {
        Handle = handle;
    }
}

public class ReceivedNetworkDataEventArgs : EventArgs
{
    public ENetDataType Type;
    public NetworkData State;

    public ReceivedNetworkDataEventArgs(NetworkData state, ENetDataType type)
    {
        State = state;
        Type = type;
    }
}

public class NetworkComponent : MonoBehaviour
{
    public EventHandler<ConnectionEventArgs> OnClientConnected;
    public EventHandler<ConnectionEventArgs> OnClientDisconnected;
    public EventHandler<ReceivedNetworkDataEventArgs> OnNetworkDataReceived;
    NetworkService Net = new NetworkService();


    void Start()
    {
        //Net.SetNetworkErrorEmulation(90, 1000);
    }

    public ENetworkState GetState()
    {
        return Net.GetState();
    }

    public bool IsServer()
    {
        return Net.IsServer();
    }

    public ConnectionHandle[] GetConnections()
    {
        return Net.GetConnections();
    }
    
    public string[] GetConnectionNames()
    {
        ConnectionHandle[] conns = Net.GetConnections();
        string[] names = new string[conns.Length];
        for (int i = 0; i < conns.Length; ++i)
        {
            names[i] = Net.GetConnectionName(conns[i]);
        }
        return names;
    }

    public bool StartAsServer()
    {
        return Net.StartServer(42069, 42169, 10);
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
    
    public void BroadcastNetworkData(NetworkData networkData)
    {
        if (Net.GetState() != ENetworkState.Running)
        {
            Debug.LogError("Do not try to send data if network is not running!");
            return;
        }

        Net.BroadcastMessage( ENetChannel.Unreliable, networkData.Serialize());
    }

    public void SendNetworkData(ConnectionHandle handle, NetworkData networkData)
    {
        if (Net.GetState() != ENetworkState.Running)
        {
            Debug.LogError("Do not try to send data if network is not running!");
            return;
        }

        Net.SendMessage(handle, ENetChannel.Unreliable, networkData.Serialize());
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

        while (Net.GetNextEvent(out ConnectionEvent e))
        {
            switch (e.EventType)
            {
                case ConnectionEvent.EType.Connected:
                    OnClientConnected?.Invoke(this, new ConnectionEventArgs(e.Connection));
                    break;
                case ConnectionEvent.EType.Disconnected:
                    OnClientDisconnected?.Invoke(this, new ConnectionEventArgs(e.Connection));
                    break;
            }
        }

        if (Net.GetState() == ENetworkState.Running)
        {
            byte[] last = null;

            // we're only interested in the most recent one
            while (Net.GetNextMessage(out byte[] message, out ConnectionHandle connection, out ENetChannel channel)) 
            {
                if (channel == ENetChannel.Unreliable)
                {
                    last = message;
                }
            }

            if (last != null && last.Length > 0)
            {
                NetworkData networkData = null;
                switch ((ENetDataType) last[0])
                {
                    case ENetDataType.UserState: 
                        networkData= new UserState();
                        break;
                    case ENetDataType.ExperimentState:
                        networkData =new  ExperimentState();
                        break;
                    default: Debug.LogError("Unknown NetDataType " + last[0]);
                        break;
                }
                if(networkData!=null)
                {
                    networkData.Deserialize(last);
                    ReceivedNetworkDataEventArgs args = new ReceivedNetworkDataEventArgs(networkData,(ENetDataType)last[0]);
                    OnNetworkDataReceived?.Invoke(this, args);
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        Net.Close();
    }
}
