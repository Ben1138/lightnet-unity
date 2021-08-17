using System;
using System.Net;
using System.Reflection;
using UnityEngine;


namespace LightNet
{
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
        /// <summary>
        /// The connection from whom this package was received
        /// </summary>
        public ConnectionHandle Connection;

        /// <summary>
        /// Custom message type to distinguish between different types of messages.<br/>
        /// By convention, the first byte of each message always corresponds to the 'type' of a message.<br/>
        /// This is user-defined!
        /// </summary>
        public byte Type;

        /// <summary>
        /// Instance of the custom de-serialized struct data.
        /// </summary>
        public NetworkData Data;

        public ReceivedNetworkDataEventArgs(ConnectionHandle connection, NetworkData data, byte type)
        {
            Connection = connection;
            Data = data;
            Type = type;
        }
    }

    /// <summary>
    /// Unity wrapper for NetworkService. Provides handy events for easy connection and package handling.
    /// </summary>
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


        // Indexing arrays is hell of alot faster than retrieving values from Dictionaries
        // Why is the size 256? Because the type number used for indexing is a byte
        Type[] NetworkDataTypes = new Type[256];

        NetworkService Net = new NetworkService();


        public void RegisterNetworkDataClass<T>(byte ID) where T : NetworkData
        {
            if (NetworkDataTypes[ID] != null)
            {
                Debug.LogWarningFormat("Network data type '{0}' is already registered to: {1}", ID, NetworkDataTypes[ID].ToString());
                return;
            }
            NetworkDataTypes[ID] = typeof(T);
        }

        /// <summary>
        /// Emulate bad network via loosing some packages intentionally and delaying their delivery
        /// </summary>
        /// <param name="looseLevel">0 = no packages get lost, 100 = all packages get lost</param>
        /// <param name="maxSendDelay">
        /// maximum delay to wait before sending/receiving a package in milliseconds
        /// (will be chosen randomly for each package between 0 and maxSendDelay)
        /// </param>
        public void SetNetworkErrorEmulation(int looseLevel, int maxSendDelay)
        {
            Net.SetNetworkErrorEmulation(looseLevel, maxSendDelay);
        }

        public ENetworkState GetState()
        {
            return Net.GetState();
        }

        /// <summary>
        /// Note that this method will return false as default, meaning it also does when the networking is not running at all.<br/>
        /// </summary>
        /// <returns>TRUE if and only if we are running networking as Server!</returns>
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
        /// Intentionally disconnect from one specific peer.<br/>
        /// If we're a running client, this call is synonymous with calling <see cref="Close"/>.
        /// </summary>
        /// <param name="connection">The connection you want to close.</param>
        /// <returns>False, if the given connection is unknown, e.g. due to not longer being alive (already closed).</returns>
        public bool Disconnect(ConnectionHandle connection)
        {
            return Net.Disconnect(connection);
        }

        /// <summary>
        /// Here you can check whether a connection, you polled at some point in the past using <see cref="GetConnections"/>,
        /// is still alive (connected).
        /// </summary>
        public bool IsAlive(ConnectionHandle connection)
        {
            return Net.IsAlive(connection);
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
                    byte type = message[0];
                    if (NetworkDataTypes[type] != null)
                    {
                        NetworkData networkData = (NetworkData)Activator.CreateInstance(NetworkDataTypes[type]);
                        networkData.Deserialize(message);
                        ReceivedNetworkDataEventArgs args = new ReceivedNetworkDataEventArgs(connection, networkData, message[0]);
                        OnNetworkDataReceived?.Invoke(this, args);

                        //Debug.LogFormat("Received Network data of type '{0}'", type.ToString(), networkData);
                    }
                    else
                    {
                        Debug.LogWarningFormat("A received package of type '{0}' got not de-serialized! Did you forget to register a appropriate NetworkData class?", type.ToString());
                    }
                }
            }
        }

        void OnApplicationQuit()
        {
            Net.Close();
        }
    }
}
