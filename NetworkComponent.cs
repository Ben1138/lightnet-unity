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
    public ConnectionHandle Connection;
    public ENetDataType Type;
    public NetworkData Data;

    public ReceivedNetworkDataEventArgs(ConnectionHandle connection, NetworkData data, ENetDataType type)
    {
        Connection = connection;
        Data = data;
        Type = type;
    }
}

public class NetworkComponent : MonoBehaviour
{
    /// <summary>
    /// Will fire in both roles, server and client.<br/>
    /// Server: Fire each time a new client connected.<br/>
    /// Client: Fire when connecting to the Server was successfull.<br/>
    /// </summary>
    public EventHandler<ConnectionEventArgs> OnClientConnected;

    /// <summary>
    /// Will fire in both roles, server and client.<br/>
    /// Server: Fire each time a client disconnected, either intentional or due to connection loss.<br/>
    /// Client: Fire when disconnecting from the server, either intentional or due to connection loss.<br/>
    /// </summary>
    public EventHandler<ConnectionEventArgs> OnClientDisconnected;

    /// <summary>
    /// Server: Will fire for each incoming message from any client.<br/>
    /// Client: Will fire for each incoming message from the server.<br/>
    /// You can differentiate by whom this message was sent by looking at the <see cref="ReceivedNetworkDataEventArgs.Connection"/> property.
    /// </summary>
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

    /// <summary>
    /// Get handles for all the currently established connections at this exact point in time.<br/>
    /// For clients, there should always be 0 (disconnected) or 1 (connected) connections.<br/>
    /// For servers, this can vary from 0 to maxClients.
    /// </summary>
    /// <returns>
    /// A list of connection handles. A connection handle can be seen as a unique identifier of
    /// a single connection.<br/>
    /// A handle:<br/>
    /// - is always unique<br/>
    /// - will never be re-assigned<br/>
    /// - can become invalid over time
    /// </returns>
    public ConnectionHandle[] GetConnections()
    {
        return Net.GetConnections();
    }

    /// <summary>
    /// Returns the 'names' of all established connections. This usually contains the remote address, 
    /// the connected ports, and whether that connection is still alive or not.
    /// </summary>
    /// <returns>Connection names / information</returns>
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

    /// <summary>
    /// Start a server, listening to incoming clients
    /// </summary>
    /// <param name="portReliable">Port to build up the reliable (TCP) connection on</param>
    /// <param name="portUnreliable">
    /// Port to build the unreliable (UDP) connection on.<br/>
    /// Beware that, with the current implementation, each new client will listen on a subsequent
    /// port after the given one! So for example, for portUnreliable=69, the first client will listen
    /// on UDP port 70, the next on 71 and so on...
    /// </param>
    /// <param name="maxClients">Maximum of connections to accept</param>
    /// <returns>False, if already running or starting the server failed (port already in use?)</returns>
    public bool StartServer(int portReliable, int portUnreliable, int maxClients)
    {
        //return Net.StartServer(42069, 42169, 1);
        return Net.StartServer(portReliable, portUnreliable, maxClients);
    }

    /// <summary>
    /// Connect as a client to a already listening server
    /// </summary>
    /// <param name="address">The address to connect to</param>
    /// <param name="portReliable">Port of the reliable (TCP) connection the server listens on</param>
    /// <param name="portUnreliable">Port of the unreliable (UDP) connection the server listens on</param>
    /// <returns>False, if already running</returns>
    public bool StartClient(string ip, int portReliable, int portUnreliable)
    {
        IPAddress address;
        if (!IPAddress.TryParse(ip, out address))
        {
            Debug.LogError("Invalid IP-Address!");
            return false;
        }
        return Net.StartClient(address, portReliable, portUnreliable);
    }

    /// <summary>
    /// Close all open connections and shut down all sockets.<br/>
    /// This is for both roles, server and client.
    /// </summary>
    /// <returns>False, if we're already in the process of shutting down or already closed altogether.</returns>
    public bool Close()
    {
        return Net.Close();
    }
   
    /// <summary>
    /// Send a message through a specific connection / to a specific destination.
    /// </summary>
    /// <param name="connection">Destination of this message</param>
    /// <param name="channel">Choose whether you want this message to be transmitted reliable or not</param>
    /// <param name="message">The actual message</param>
    /// <returns>False, if the given connection handle is (no longer) valid.</returns>
    public void SendNetworkData(ConnectionHandle connection, ENetChannel channel, NetworkData message)
    {
        if (Net.GetState() != ENetworkState.Running)
        {
            Debug.LogError("Do not try to send data if network is not running!");
            return;
        }

        Net.SendMessage(connection, channel, message.Serialize());
    }

    /// <summary>
    /// Send a message to everyone listening.
    /// </summary>
    /// <param name="channel">Choose whether you want this message to be transmitted reliable or not</param>
    /// <param name="message">The actual message you want to spread</param>
    public void BroadcastNetworkData(ENetChannel channel, NetworkData message)
    {
        if (Net.GetState() != ENetworkState.Running)
        {
            Debug.LogError("Do not try to send data if network is not running!");
            return;
        }

        Net.BroadcastMessage(channel, message.Serialize());
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
            while (Net.GetNextMessage(out byte[] message, out ConnectionHandle connection, out ENetChannel channel)) 
            {
                NetworkData networkData = null;
                ENetDataType type = (ENetDataType)message[0];
                switch (type)
                {
                    case ENetDataType.UserState:
                        networkData = new UserState();
                        break;
                    case ENetDataType.ExperimentState:
                        networkData = new ExperimentState();
                        break;
                    default:
                        Debug.LogError("Unknown NetDataType " + (int)type);
                        break;
                }
                if (networkData != null)
                {
                    networkData.Deserialize(message);
                    ReceivedNetworkDataEventArgs args = new ReceivedNetworkDataEventArgs(connection, networkData, type);
                    OnNetworkDataReceived?.Invoke(this, args);
                    //Debug.LogFormat("Received Network data of type '{0}'", type.ToString(), networkData);
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        Net.Close();
    }
}
