using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using System.Collections;


public enum ENetChannel
{
    Reliable,
    Unreliable
}

public enum ENetworkState
{
    Closed,
    Startup,
    Running,
    Shutdown
}

[Flags]
public enum ENetworkEventReportFlags : byte
{
    None = 0x00,
    ConnectionStatus = 0x01,
    ReceivedMessage = 0x02,
    All = ConnectionStatus | ReceivedMessage
}

public struct ConnectionHandle
{
    public ConnectionHandle(ulong internalHandle)
    {
        Handle = internalHandle;
    }

    public ulong GetInternalHandle()
    {
        return Handle;
    }

    public bool IsValid()
    {
        return Handle != 0;
    }

    ulong Handle;
}

public struct ConnectionEvent
{
    public enum EType
    {
        Connected,
        Disconnected,
        ReceivedMessage
    }

    public EType EventType;
    public ConnectionHandle Connection;
}

public class NetworkService
{
    const int CHANNEL_BUFFER_SIZE = 2048;
    const int SHUTDOWN_TIMEOUT = 2000; // milliseconds

    public volatile ENetworkEventReportFlags EventFlags = ENetworkEventReportFlags.ConnectionStatus;

    volatile ENetworkState State = ENetworkState.Closed;
    volatile bool bShutdown = true;

    volatile TcpListener Server;
    volatile UdpClient UnreliableReceive;
    Thread NetThread;
    ulong NextConnectionHandle = 1;

    int PortReliable;
    int PortUnreliable;
    IPAddress RemoteAddress;

    ConcurrentDictionary<ulong, Connection> Connections = new ConcurrentDictionary<ulong, Connection>();
    ConcurrentQueue<ConnectionEvent> Events = new ConcurrentQueue<ConnectionEvent>();


    ~NetworkService()
    {
        if (State == ENetworkState.Running || State == ENetworkState.Startup)
        {
            Close();
        }
    }

    public bool StartServer(int portReliable, int portUnreliable, int maxClients)
    {
        if (State != ENetworkState.Closed)
        {
            LogQueue.LogWarning("Cannot start Server, we're already a {}!", new object[] { State });
            return false;
        }

        Debug.Assert(Server == null);
        Debug.Assert(UnreliableReceive == null);
        Debug.Assert(Connections.Count == 0);

        PortReliable = portReliable;
        PortUnreliable = portUnreliable;
        while (Events.TryDequeue(out ConnectionEvent e)) { }

        Server = new TcpListener(new IPEndPoint(IPAddress.Any, portReliable));
        Server.Start(maxClients);
        UnreliableReceive = new UdpClient(portUnreliable);

        bShutdown = false;
        NetThread = new Thread(ServerThreadFunc);
        NetThread.Name = "Lightnet - Server Thread";
        NetThread.Start();
        State = ENetworkState.Running;

        return true;
    }

    public bool StartClient(IPAddress address, int portReliable, int portUnreliable)
    {
        if (State != ENetworkState.Closed)
        {
            LogQueue.LogWarning("Cannot start Client, we're already a {}!", new object[] { State });
            return false;
        }

        Debug.Assert(Server == null);
        Debug.Assert(UnreliableReceive == null);
        Debug.Assert(Connections.Count == 0);

        State = ENetworkState.Startup;
        RemoteAddress = address;
        PortReliable = portReliable;
        PortUnreliable = portUnreliable;
        while (Events.TryDequeue(out ConnectionEvent e)) { }

        bShutdown = false;
        NetThread = new Thread(ClientThreadFunc);
        NetThread.Name = "Lightnet - Client Thread";
        NetThread.Start();

        return true;
    }

    public ENetworkState GetState()
    {
        return State;
    }

    public bool IsServer()
    {
        return Server != null;
    }

    public bool Close()
    {
        if (State == ENetworkState.Closed)
        {
            LogQueue.LogWarning("Cannot close networking, already closed!");
            return false;
        }
        if (State == ENetworkState.Shutdown)
        {
            LogQueue.LogWarning("Cannot close networking, already shutting down...");
            return false;
        }

        Debug.Assert(!bShutdown);
        Debug.Assert(NetThread != null);
        Debug.Assert(NetThread.IsAlive);

        State = ENetworkState.Shutdown;
        bShutdown = true;

        void AbortThread()
        {
            foreach (KeyValuePair<ulong, Connection> conn in Connections)
            {
                conn.Value.Shutdown();
            }
            Connections.Clear();
            if (!NetThread.Join(SHUTDOWN_TIMEOUT))
            {
                NetThread.Abort();
            }
            NetThread = null;
            State = ENetworkState.Closed;
        }

        Thread abortThread = new Thread(AbortThread);
        abortThread.Start();

        Server?.Stop();
        Server = null;
        UnreliableReceive?.Close();
        UnreliableReceive = null;

        return true;
    }

    public ConnectionHandle[] GetConnections()
    {
        ConnectionHandle[] conns = new ConnectionHandle[Connections.Count];
        int i = 0;
        foreach (KeyValuePair<ulong, Connection> conn in Connections)
        {
            conns[i++] = new ConnectionHandle(conn.Key);
        }
        return conns;
    }

    public string GetConnectionName(ConnectionHandle handle)
    {
        if (!handle.IsValid() || !Connections.ContainsKey(handle.GetInternalHandle()))
        {
            LogQueue.LogWarning("Given connection handle was invalid!");
            return "INVALID HANDLE";
        }
        return Connections[handle.GetInternalHandle()].ToString();
    }

    public bool GetNextEvent(out ConnectionEvent outEvent)
    {
        return Events.TryDequeue(out outEvent);
    }

    public bool SendMessage(ConnectionHandle connection, ENetChannel channel, byte[] message)
    {
        if (!Connections.TryGetValue(connection.GetInternalHandle(), out Connection conn))
        {
            LogQueue.LogWarning("Given connection handle is invalid!");
            return false;
        }

        conn.SendMessage(channel, message);
        return true;
    }

    public void BroadcastMessage(ENetChannel channel, byte[] message)
    {
        foreach (KeyValuePair<ulong, Connection> conn in Connections)
        {
            conn.Value.SendMessage(channel, message);
        }
    }

    /// <summary>
    /// Try to get the next message received by the given connection, on a specific channel.
    /// </summary>
    /// <param name="connection">The connection to try get a message from</param>
    /// <param name="channel">The channel to try get a message from</param>
    /// <param name="message">The received message data</param>
    /// <returns>False, if there're no messages left</returns>
    public bool GetNextMessage(ConnectionHandle connection, ENetChannel channel, out byte[] message)
    {
        if (!Connections.TryGetValue(connection.GetInternalHandle(), out Connection conn))
        {
            message = null;
            LogQueue.LogWarning("Given connection handle is invalid!");
            return false;
        }

        return conn.GetNextMessage(channel, out message);
    }

    /// <summary>
    /// Try to get the next message received by the given connection, on any channel.
    /// </summary>
    /// <param name="connection">The connection to try get a message from</param>
    /// <param name="message">The received message data</param>
    /// <param name="channel">The channel on which we received the message</param>
    /// <returns>False, if there're no messages left</returns>
    public bool GetNextMessage(ConnectionHandle connection, out byte[] message, out ENetChannel channel)
    {
        if (!Connections.TryGetValue(connection.GetInternalHandle(), out Connection conn))
        {
            channel = ENetChannel.Unreliable;
            message = null;
            LogQueue.LogWarning("Given connection handle is invalid!");
            return false;
        }

        return conn.GetNextMessage(out message, out channel);
    }

    /// <summary>
    /// Try to get the next message received by the given connection, on any channel.
    /// </summary>
    /// <param name="connection">The connection to try get a message from</param>
    /// <param name="message">The received message data</param>
    /// <returns>False, if there're no messages left</returns>
    public bool GetNextMessage(ConnectionHandle connection, out byte[] message)
    {
        if (!Connections.TryGetValue(connection.GetInternalHandle(), out Connection conn))
        {
            message = null;
            LogQueue.LogWarning("Given connection handle is invalid!");
            return false;
        }

        return conn.GetNextMessage(out message);
    }

    /// <summary>
    /// Try to get the next message received by any connection, on any channel.
    /// </summary>
    /// <param name="message">The received message data</param>
    /// <param name="connection">The connection we received the message from</param>
    /// <param name="channel">The channel on which we received the message</param>
    /// <returns>False, if there're no messages left</returns>
    public bool GetNextMessage(out byte[] message, out ConnectionHandle connection, out ENetChannel channel)
    {
        foreach (KeyValuePair<ulong, Connection> conn in Connections)
        {
            connection = new ConnectionHandle(conn.Key);
            if (conn.Value.GetNextMessage(out message, out channel))
            {
                return true;
            }
        }
        message = null;
        connection = new ConnectionHandle(0);
        channel = ENetChannel.Unreliable;
        return false;
    }

    /// <summary>
    /// Try to get the next message received by any connection, on any channel.
    /// </summary>
    /// <param name="message">The received message data</param>
    /// <param name="connection">The connection we received the message from</param>
    /// <returns>False, if there're no messages left</returns>
    public bool GetNextMessage(out byte[] message, out ConnectionHandle connection)
    {
        foreach (KeyValuePair<ulong, Connection> conn in Connections)
        {
            connection = new ConnectionHandle(conn.Key);
            if (conn.Value.GetNextMessage(out message))
            {
                return true;
            }
        }
        message = null;
        connection = new ConnectionHandle(0);
        return false;
    }

    /// <summary>
    /// Try to get the next message received by any connection, on any channel.
    /// </summary>
    /// <param name="message">The received message data</param>
    /// <returns>False, if there're no messages left</returns>
    public bool GetNextMessage(out byte[] message)
    {
        foreach (KeyValuePair<ulong, Connection> conn in Connections)
        {
            if (conn.Value.GetNextMessage(out message))
            {
                return true;
            }
        }
        message = null;
        return false;
    }

    void AddEvent(ConnectionEvent ev, ENetworkEventReportFlags flag)
    {
        if ((EventFlags & flag) != 0)
        {
            Events.Enqueue(ev);
            if (Events.Count > 1000)
            {
                LogQueue.LogWarning("NetworkServeice event queue has over {0} events queued! Do you poll them somewhere?");
            }
        }
    }

    void ServerThreadFunc()
    {
        Debug.Assert(Server != null);
        Debug.Assert(State == ENetworkState.Running);
        Debug.Assert(Connections.Count == 0);

        while (!bShutdown)
        {
            // check for incoming connections
            if (Server.Pending())
            {
                TcpClient client = null;
                try
                {
                    // this will block the thread until someone connects
                    // gladfully, we checked with Pending() beforehand if 
                    // there's someone knocking on the door
                    client = Server.AcceptTcpClient();
                }
                catch (Exception e)
                {
                    LogQueue.LogWarning(e.Message);
                    continue;
                }

                Debug.Assert(client != null);
                IPAddress address = ((IPEndPoint)client.Client.RemoteEndPoint).Address;

                // TODO: implement proper port assignment
                int clientListenPort = ++PortUnreliable;

                ulong handle = NextConnectionHandle++;
                Connection conn = new Connection(this, handle, address, PortReliable, clientListenPort, client);
                if (!Connections.TryAdd(handle, conn))
                {
                    LogQueue.LogError("Connection handles are inconsistent! This should never happen!");
                    continue;
                }

                {
                    // FIRST thing to send to our new client is the handle we are giving them.
                    // This is necessary to later resolve an incoming UDP package to a specific connection
                    int offset = 0;
                    byte[] portBytes = BitConverter.GetBytes(clientListenPort);
                    byte[] handleBytes = BitConverter.GetBytes(handle);
                    byte[] sendData = new byte[portBytes.Length + handleBytes.Length];
                    Array.Copy(portBytes, 0, sendData, 0, portBytes.Length); offset += portBytes.Length;
                    Array.Copy(handleBytes, 0, sendData, offset, handleBytes.Length);
                    conn.SendMessage(ENetChannel.Reliable, sendData);
                }

                LogQueue.LogInfo("New client '{0}' joined (handle: {1})", new object[] { address.ToString(), handle });
                AddEvent(new ConnectionEvent 
                { 
                    Connection = new ConnectionHandle(handle), 
                    EventType = ConnectionEvent.EType.Connected 
                }, ENetworkEventReportFlags.ConnectionStatus);
            }

            foreach (KeyValuePair<ulong, Connection> conn in Connections)
            {
                if (!conn.Value.IsAlive())
                {
                    LogQueue.LogInfo("Connection to '{0}' lost!", new object[] { conn.Value.GetRemoteAddress().ToString() });
                    conn.Value.Shutdown();
                    if (!Connections.TryRemove(conn.Key, out Connection c))
                    {
                        LogQueue.LogError("Inconsitent connection dictionary! This should never happen!");
                    }
                    AddEvent(new ConnectionEvent
                    {
                        Connection = new ConnectionHandle(conn.Key),
                        EventType = ConnectionEvent.EType.Disconnected
                    }, ENetworkEventReportFlags.ConnectionStatus);
                }
            }

            ReceiveUnreliable();
        }
    }

    void ClientThreadFunc()
    {
        void Close()
        {
            UnreliableReceive?.Close();
            UnreliableReceive = null;
            State = ENetworkState.Closed;
        }

        const ulong handle = 1;

        Debug.Assert(Server == null);
        Debug.Assert(UnreliableReceive == null);
        Debug.Assert(State == ENetworkState.Startup);
        Debug.Assert(Connections.Count == 0);

        TcpClient client = null;
        try
        {
            // try connect to specified remote
            client = new TcpClient();
            client.Connect(new IPEndPoint(RemoteAddress, PortReliable));
            if (!client.Connected)
            {
                Connections.Clear();
                LogQueue.LogWarning("Couldn't connect to Server '{0}'!", new object[] { RemoteAddress.ToString() });
                Close();
                return;
            }
        }
        catch (Exception e)
        {
            LogQueue.LogWarning(e.Message);
            Close();
            return;
        }

        Connections.TryAdd(handle, new Connection(this, handle, RemoteAddress, PortReliable, PortUnreliable, client));

        Debug.Assert(Connections.Count == 1);
        State = ENetworkState.Running;

        while (!bShutdown)
        {
            if (!Connections[handle].IsAlive())
            {
                LogQueue.LogWarning("Connection to Server '{0}' lost!", new object[] { Connections[handle].GetRemoteAddress().ToString() });
                Connections[handle].Shutdown();
                Connections.Clear();

                AddEvent(new ConnectionEvent
                {
                    Connection = new ConnectionHandle(handle),
                    EventType = ConnectionEvent.EType.Disconnected
                }, ENetworkEventReportFlags.ConnectionStatus);

                Close();
                return;
            }

            ReceiveUnreliable();
        }
    }

    void ReceiveUnreliable()
    {
        if (UnreliableReceive == null)
        {
            return;
        }

        // RECEIVING - UDP
        while (UnreliableReceive.Available > 0)
        {
            IPEndPoint sender = null;
            byte[] data = null;
            try
            {
                // using the sender as identification is unreliable,
                // especially when dealing with local connections
                data = UnreliableReceive.Receive(ref sender);
            }
            catch (Exception e)
            {
                LogQueue.LogWarning("UDP Receiving failed: " + e.Message);
                return;
            }

            Connection connection = null;
            Debug.Assert(data != null);
            Debug.Assert(sender != null);

            LogQueue.LogInfo("Received UDP package: {0}", new object[] { data.Length });

            int offset = 0;
            if (Server != null)
            {
                // for servers, we expect the clients to always send a ulong
                // beforehand, specifying the connection handle we assigned to them
                ulong handle = BitConverter.ToUInt64(data, 0);
                offset += sizeof(ulong);
                connection = Connections[handle];
            }
            else
            {
                // for clients, there's always just one connection
                // and it's handle is always 1
                connection = Connections[1];
            }
            Debug.Assert(connection != null);

            lock (connection.GetUnreliableReceiveLock())
            {
                Connection.ReceiveBuffer buffer = connection.GetUnreliableReceiveBuffer();

                int maxReadSize = CHANNEL_BUFFER_SIZE - buffer.Head;
                if (data.Length > maxReadSize)
                {
                    LogQueue.LogError("Ran out of unreliable receive buffer memory! Message too large?");
                    return;
                }

                Array.Copy(data, offset, buffer.Buffer, buffer.Head, data.Length - offset);
                buffer.Head += data.Length;
            }
        }
    }

    class Connection
    {
        public class ReceiveBuffer
        {
            public byte[] Buffer = new byte[CHANNEL_BUFFER_SIZE];
            public int Head;
        }

        NetworkService Owner = null;
        ulong Handle = 0;
        ulong RemoteHandle = 0;

        volatile bool bShutdown = false;
        volatile Thread ConnectionThread;
        volatile TcpClient Reliable;
        UdpClient UnrealiableSend;
        ulong UnreliableTime = 0;

        object AliveLock = new object();
        object UnreliableReceiveLock = new object();

        // Tcp packets will already arrive sorted (order is guaranteed), no additional sorting needed.
        // For udp packets, we're using timestamps to determine the order of the packet.
        ConcurrentQueue<byte[]> ReceivedReliableMessages = new ConcurrentQueue<byte[]>();
        ConcurrentSortedList<ulong, byte[]> ReceivedUnreliableMessages = new ConcurrentSortedList<ulong, byte[]>();

        // Message layout:
        // 2 bytes (ushort) - message length (number of bytes)
        // rest of data     - actual message
        ReceiveBuffer ReliableReceiveBuffer = new ReceiveBuffer();

        // Message layout:
        // 8 bytes (ulong)  - timestamp
        // 2 bytes (ushort) - message length (number of bytes)
        // rest of data     - actual message
        ReceiveBuffer UnreliableReceiveBuffer = new ReceiveBuffer();

        // these contain just message data, without header (a.k.a. timestamp and message length)
        ConcurrentQueue<byte[]> ReliableSendBuffer = new ConcurrentQueue<byte[]>();
        ConcurrentQueue<byte[]> UnreliableSendBuffer = new ConcurrentQueue<byte[]>();

        IPEndPoint RemoteReliable;
        IPEndPoint RemoteUnreliable;
        IPEndPoint LocalUnreliable;

        public Connection(NetworkService owner, ulong handle, IPAddress address, int portReliable, int portUnreliable, TcpClient client)
        {
            Debug.Assert(owner != null);
            Debug.Assert(handle != 0);
            Debug.Assert(client != null);

            Owner = owner;
            Handle = handle;
            RemoteReliable = new IPEndPoint(address, portReliable);
            RemoteUnreliable = new IPEndPoint(address, portUnreliable);

            Reliable = client;

            ConnectionThread = new Thread(ConnectionThreadFunc);
            ConnectionThread.Name = "Lightnet - " + ToString();
            ConnectionThread.Start();
        }

        ~Connection()
        {
            if (!bShutdown)
            {
                Shutdown();
            }
        }

        public IPAddress GetRemoteAddress()
        {
            return RemoteReliable.Address;
        }

        public bool IsAlive()
        {
            bool bAlive = false;
            lock (AliveLock)
            {
                bAlive = Reliable.Connected && ConnectionThread.IsAlive;
            }
            return bAlive;
        }

        public ReceiveBuffer GetUnreliableReceiveBuffer()
        {
            return UnreliableReceiveBuffer;
        }

        public object GetUnreliableReceiveLock()
        {
            return UnreliableReceiveLock;
        }

        public void Shutdown()
        {
            if (bShutdown)
            {
                LogQueue.LogWarning("Connection is already shut down!");
                return;
            }

            bShutdown = true;
            if (!ConnectionThread.Join(SHUTDOWN_TIMEOUT))
            {
                LogQueue.LogWarning("Shutdown timeout, aborting connection thread...");
                ConnectionThread.Abort();
            }
            Reliable.Close();
            UnrealiableSend.Close();
        }

        public bool GetNextMessage(ENetChannel channel, out byte[] data)
        {
            if (channel == ENetChannel.Reliable)
            {
                return ReceivedReliableMessages.TryDequeue(out data);
            }
            else if (channel == ENetChannel.Unreliable)
            {
                return ReceivedUnreliableMessages.TryDequeue(out data);
            }
            LogQueue.LogError("Unknown ENetChannel '{0}'!", new object[] { (int)channel });
            data = null;
            return false;
        }

        public bool GetNextMessage(out byte[] data, out ENetChannel channel)
        {
            if (ReceivedReliableMessages.TryDequeue(out data))
            {
                channel = ENetChannel.Reliable;
                return true;
            }
            else if (ReceivedUnreliableMessages.TryDequeue(out data))
            {
                channel = ENetChannel.Unreliable;
                return true;
            }
            channel = ENetChannel.Unreliable;
            return false;
        }

        public bool GetNextMessage(out byte[] data)
        {
            return ReceivedReliableMessages.TryDequeue(out data) || ReceivedUnreliableMessages.TryDequeue(out data);
        }

        public void SendMessage(ENetChannel channel, byte[] data)
        {
            if (!IsAlive())
            {
                LogQueue.LogWarning("Cannot send message on a dead connection!");
                return;
            }
            if (data.Length == 0)
            {
                LogQueue.LogWarning("Cannot send empty message!");
                return;
            }

            if (channel == ENetChannel.Reliable)
            {
                const ushort MaxPaketSize =
                    65535 -                 // TCP max packet size
                    sizeof(ushort);         // message size

                if (data.Length > MaxPaketSize)
                {
                    // TODO: if data is too large, split it up
                    LogQueue.LogError("Given message data of {0} bytes exceeds max message size of {1}", new object[] { data.Length, MaxPaketSize });
                    return;
                }

                if (data.Length > CHANNEL_BUFFER_SIZE)
                {
                    LogQueue.LogWarning("Given message data of {0} bytes potentially exceeds receiving buffer size of {1}", new object[] { data.Length, MaxPaketSize });
                }

                ReliableSendBuffer.Enqueue(data);
            }
            else if (channel == ENetChannel.Unreliable)
            {
                const ushort MaxPaketSize =
                    65535 -                 // UDP max packet size
                    sizeof(ulong) -         // timestamp
                    sizeof(ushort);         // message size

                if (data.Length > MaxPaketSize)
                {
                    // TODO: if data is too large, split it up
                    LogQueue.LogError("Given message data of {0} bytes exceeds max message size of {1}", new object[] { data.Length, MaxPaketSize });
                    return;
                }

                if (data.Length > CHANNEL_BUFFER_SIZE)
                {
                    LogQueue.LogWarning("Given message data of {0} bytes potentially exceeds receiving buffer size of {1}", new object[] { data.Length, MaxPaketSize });
                }

                UnreliableSendBuffer.Enqueue(data);
            }
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}:{2} - {3}", RemoteReliable.Address.ToString(), RemoteReliable.Port, RemoteUnreliable.Port, IsAlive() ? "Alive" : "Dead");
        }

        void ConnectionThreadFunc()
        {
            Debug.Assert(UnrealiableSend == null);
            Debug.Assert(ReliableReceiveBuffer.Head == 0);
            Debug.Assert(UnreliableReceiveBuffer.Head == 0);

            UnrealiableSend = new UdpClient();
            UnrealiableSend.Connect(RemoteUnreliable);
            
            // If we're a client, we expect a special message as first message
            // This message (8 bytes) contains the connection handle we've
            // been assigned from the server.
            bool bFirstMessage = Owner.Server == null;

            while (!bShutdown)
            {
                // check every frame whether our connection is still alive
                if (!Reliable.Connected)
                {
                    return;
                }

                if (!UnrealiableSend.Client.Connected)
                {
                    LogQueue.LogWarning("UDP connection lost");
                    Reliable.Close();
                    return;
                }

                // RELIABLE CHANNEL
                {
                    // RECEIVING - TCP
                    NetworkStream stream = null;
                    try
                    {
                        stream = Reliable.GetStream();
                    }
                    catch (Exception e)
                    {
                        LogQueue.LogWarning(e.Message);
                        Reliable.Close();
                        return;
                    }
                    while (stream.DataAvailable)
                    {
                        int maxReadSize = CHANNEL_BUFFER_SIZE - ReliableReceiveBuffer.Head;
                        if (maxReadSize == 0)
                        {
                            LogQueue.LogError("Ran out of reliable receive buffer memory! Message too large?");
                            ReliableReceiveBuffer.Head = 0;
                            Reliable.Close();
                            return;
                        }

                        int bytesRead;
                        try
                        {
                            bytesRead = stream.Read(ReliableReceiveBuffer.Buffer, ReliableReceiveBuffer.Head, maxReadSize);
                            //LogQueue.LogInfo("Received {0} TCP bytes", new object[] { bytesRead });
                        }
                        catch (Exception e)
                        {
                            LogQueue.LogWarning(e.Message);
                            Reliable.Close();
                            return;
                        }
                        ReliableReceiveBuffer.Head += bytesRead;

                        ushort msgSize;
                        if (ReliableReceiveBuffer.Head >= sizeof(ushort))
                        {
                            int offset = 0;
                            msgSize = BitConverter.ToUInt16(ReliableReceiveBuffer.Buffer, 0); offset += sizeof(ushort);
                            if (ReliableReceiveBuffer.Head >= sizeof(ushort) + msgSize)
                            {
                                // The first message is ALWAYS a ulong specifying our connection handle on the other end. 
                                // This is necessary to differentiate between multiple UDP senders.
                                if (bFirstMessage)
                                {
                                    int localUnreliablePort = BitConverter.ToInt32(ReliableReceiveBuffer.Buffer, offset);
                                    offset += sizeof(int);
                                    RemoteHandle = BitConverter.ToUInt64(ReliableReceiveBuffer.Buffer, offset);
                                    Debug.Assert(RemoteHandle != 0);
                                    ReliableReceiveBuffer.Head = 0;
                                    Owner.UnreliableReceive = new UdpClient(localUnreliablePort);
                                    bFirstMessage = false;
                                }
                                else
                                {
                                    // copy message from receive buffer into message queue
                                    byte[] message = new byte[msgSize];
                                    Array.Copy(ReliableReceiveBuffer.Buffer, offset, message, 0, msgSize);
                                    ReceivedReliableMessages.Enqueue(message);

                                    if ((Owner.EventFlags & ENetworkEventReportFlags.ReceivedMessage) != 0)
                                    {
                                        Owner.AddEvent(new ConnectionEvent
                                        {
                                            Connection = new ConnectionHandle(Handle),
                                            EventType = ConnectionEvent.EType.ReceivedMessage
                                        }, ENetworkEventReportFlags.ReceivedMessage);
                                    }

                                    // from https://docs.microsoft.com/en-us/dotnet/api/system.array.copy?view=net-5.0 :
                                    // If sourceArray and destinationArray overlap, this method behaves as if the original values of sourceArray 
                                    // were preserved in a temporary location before destinationArray is overwritten.
                                    int remanining = CHANNEL_BUFFER_SIZE - ReliableReceiveBuffer.Head;
                                    if (remanining > 0)
                                    {
                                        // shift the buffer to the left
                                        // TODO: maybe do a ringbuffer instead? Although, we'd also need two memcpy operations in case of edge overlap
                                        Array.Copy(ReliableReceiveBuffer.Buffer, ReliableReceiveBuffer.Head, ReliableReceiveBuffer.Buffer, 0, remanining);
                                    }
                                    ReliableReceiveBuffer.Head = 0;
                                }
                            }                            
                        }
                    }

                    // SENDING - TCP
                    try
                    {
                        stream = Reliable.GetStream();
                    }
                    catch (Exception e)
                    {
                        LogQueue.LogWarning(e.Message);
                        Reliable.Close();
                        return;
                    }

                    byte[] messageData;
                    while (ReliableSendBuffer.TryDequeue(out messageData))
                    {
                        byte[] msgLength = BitConverter.GetBytes((ushort)messageData.Length);
                        byte[] sendData = new byte[sizeof(ushort) + messageData.Length];

                        int offset = 0;
                        Array.Copy(msgLength, 0, sendData, offset, msgLength.Length); offset += msgLength.Length;
                        Array.Copy(messageData, 0, sendData, offset, messageData.Length);

                        try
                        {
                            stream.Write(sendData, 0, sendData.Length);
                        }
                        catch (Exception e)
                        {
                            LogQueue.LogWarning(e.Message);
                            Reliable.Close();
                            return;
                        }
                    }
                }

                // UNRELIABLE CHANNEL
                {
                    // Received UDP data
                    lock (UnreliableReceiveLock)
                    {
                        ulong timestamp;
                        ushort msgSize;
                        if (UnreliableReceiveBuffer.Head >= sizeof(ulong) + sizeof(ushort))
                        {
                            int offset = 0;
                            timestamp = BitConverter.ToUInt64(UnreliableReceiveBuffer.Buffer, offset); offset += sizeof(ulong);
                            msgSize = BitConverter.ToUInt16(UnreliableReceiveBuffer.Buffer, offset); offset += sizeof(ushort);

                            if (UnreliableReceiveBuffer.Head >= offset + msgSize)
                            {
                                byte[] message = new byte[msgSize];
                                Array.Copy(UnreliableReceiveBuffer.Buffer, offset, message, 0, msgSize);
                                ReceivedUnreliableMessages.Enqueue(timestamp, message);

                                if ((Owner.EventFlags & ENetworkEventReportFlags.ReceivedMessage) != 0)
                                {
                                    Owner.AddEvent(new ConnectionEvent
                                    {
                                        Connection = new ConnectionHandle(Handle),
                                        EventType = ConnectionEvent.EType.ReceivedMessage
                                    }, ENetworkEventReportFlags.ReceivedMessage);
                                }

                                int remanining = CHANNEL_BUFFER_SIZE - UnreliableReceiveBuffer.Head;
                                if (remanining > 0)
                                {
                                    Array.Copy(UnreliableReceiveBuffer.Buffer, UnreliableReceiveBuffer.Head, UnreliableReceiveBuffer.Buffer, 0, remanining);
                                }
                                UnreliableReceiveBuffer.Head = 0;
                            }
                        }
                    }

                    // SENDING - UDP
                    byte[] messageData;
                    while (UnreliableSendBuffer.TryDequeue(out messageData))
                    {
                        byte[] timestamp = BitConverter.GetBytes(UnreliableTime++);
                        byte[] msgLength = BitConverter.GetBytes((ushort)messageData.Length);
                        byte[] sendData = new byte[(Owner.Server == null ? sizeof(ulong) : 0) + sizeof(ulong) + sizeof(ushort) + messageData.Length];

                        int offset = 0;
                        if (Owner.Server == null)
                        {
                            // if we're a client, always send the connection handle we've been assigned to by the server
                            byte[] handle = BitConverter.GetBytes(RemoteHandle);
                            Array.Copy(handle, 0, sendData, offset, handle.Length); offset += handle.Length;
                        }
                        Array.Copy(timestamp, 0, sendData, offset, timestamp.Length); offset += timestamp.Length;
                        Array.Copy(msgLength, 0, sendData, offset, msgLength.Length); offset += msgLength.Length;
                        Array.Copy(messageData, 0, sendData, offset, messageData.Length);

                        try
                        {
                            UnrealiableSend.Send(sendData, sendData.Length);
                            LogQueue.LogInfo("Sending UDP package: {0}", new object[] { sendData.Length });
                        }
                        catch (Exception e)
                        {
                            LogQueue.LogWarning(e.Message);
                            Reliable.Close();
                            return;
                        }
                    }
                }
            }
        }
    }
}