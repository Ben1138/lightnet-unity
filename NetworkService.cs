using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

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

public class NetworkService
{
    const int CHANNEL_BUFFER_SIZE = 2048;
    const int SHUTDOWN_TIMEOUT    = 2000; // milliseconds

    enum EClientState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    class ReceiveBuffer
    {
        public byte[] Buffer = new byte[CHANNEL_BUFFER_SIZE];
        public int Head;
    }

    volatile bool bIsServer;
    volatile Thread TcpThread;
    volatile Thread UdpThread;

    TcpListener Server = null;
    TcpClient Client = null;
    UdpClient Unrealiable = null;
    ulong UnreliableTime = 0;

    volatile EClientState ClientStateReliable = EClientState.Disconnected;
    volatile EClientState ClientStateUnreliable = EClientState.Disconnected;
    volatile ENetworkState State = ENetworkState.Closed;
    volatile bool bShutdown = true;

    // Tcp packets will already arrive sorted (order is guaranteed), no additional sorting needed.
    // For udp packets, we're using timestamps to determine the order of the packet.
    ConcurrentQueue<byte[]> ReliableMessages = new ConcurrentQueue<byte[]>();
    ConcurrentSortedList<ulong, byte[]> UnreliableMessages = new ConcurrentSortedList<ulong, byte[]>();

    // Message layout:
    // 2 bytes (ushort) - message length (number of bytes)
    // rest of data     - actual message
    ReceiveBuffer ReliableReceiveBuffer = new ReceiveBuffer();

    // Message layout:
    // 8 bytes (ulong)  - timestamp
    // 2 bytes (ushort) - message length (number of bytes)
    // rest of data     - actual message
    ReceiveBuffer UnreliableReceiveBuffer = new ReceiveBuffer();

    ConcurrentQueue<byte[]> ReliableSendBuffer = new ConcurrentQueue<byte[]>();
    ConcurrentQueue<byte[]> UnreliableSendBuffer = new ConcurrentQueue<byte[]>();

    IPEndPoint RemoteReliable;
    IPEndPoint RemoteUnreliable;


    public bool StartServer(int portReliable, int portUnreliable)
    {
        if (State != ENetworkState.Closed)
        {
            LogQueue.LogWarning("Cannot start Server, we're already a {}!", new object[] { State });
            return false;
        }

        Debug.Assert(TcpThread == null);
        Debug.Assert(UdpThread == null);
        Debug.Assert(Server == null);
        Debug.Assert(Client == null);
        Debug.Assert(Unrealiable == null);
        Debug.Assert(ClientStateReliable == EClientState.Disconnected);
        Debug.Assert(ClientStateUnreliable == EClientState.Disconnected);
        Debug.Assert(ReliableReceiveBuffer.Head == 0);
        Debug.Assert(UnreliableReceiveBuffer.Head == 0);

        while (!ReliableSendBuffer.IsEmpty) ReliableSendBuffer.TryDequeue(out byte[] temp);
        while (!UnreliableSendBuffer.IsEmpty) UnreliableSendBuffer.TryDequeue(out byte[] temp);

        while (!ReliableMessages.IsEmpty) ReliableMessages.TryDequeue(out byte[] temp);
        UnreliableMessages.Clear();

        UnreliableTime = 0;
        Server = new TcpListener(IPAddress.Parse("127.0.0.1"), portReliable);
        Server.Start(1); // max 1 connection
        Unrealiable = new UdpClient(portUnreliable);
        RemoteUnreliable = new IPEndPoint(IPAddress.Any, 0);

        State = ENetworkState.Startup;
        ClientStateReliable = EClientState.Connecting;
        ClientStateUnreliable = EClientState.Connected;
        bIsServer = true;

        bShutdown = false;
        TcpThread = new Thread(TcpUpdate);
        TcpThread.Name = "Lightnet - TCP Update";
        TcpThread.Start();
        UdpThread = new Thread(UdpUpdate);
        UdpThread.Name = "Lightnet - UDP Update";
        UdpThread.Start();

        return true;
    }

    public bool StartClient(IPAddress address, int portReliable, int portUnreliable)
    {
        if (State != ENetworkState.Closed)
        {
            LogQueue.LogWarning("Cannot start Client, we're already a {}!", new object[] { State });
            return false;
        }

        Debug.Assert(TcpThread == null);
        Debug.Assert(UdpThread == null);
        Debug.Assert(Server == null);
        Debug.Assert(Client == null);
        Debug.Assert(Unrealiable == null);
        Debug.Assert(ClientStateReliable == EClientState.Disconnected);
        Debug.Assert(ClientStateUnreliable == EClientState.Disconnected);
        Debug.Assert(ReliableReceiveBuffer.Head == 0);
        Debug.Assert(UnreliableReceiveBuffer.Head == 0);

        while (!ReliableSendBuffer.IsEmpty) ReliableSendBuffer.TryDequeue(out byte[] temp);
        while (!UnreliableSendBuffer.IsEmpty) UnreliableSendBuffer.TryDequeue(out byte[] temp);

        while (!ReliableMessages.IsEmpty) ReliableMessages.TryDequeue(out byte[] temp);
        UnreliableMessages.Clear();

        bShutdown = false;
        ClientStateReliable = EClientState.Connecting;
        ClientStateUnreliable = EClientState.Connecting;
        Client = new TcpClient();
        Unrealiable = new UdpClient();

        bIsServer = false;
        State = ENetworkState.Startup;
        RemoteReliable = new IPEndPoint(address, portReliable);
        RemoteUnreliable = new IPEndPoint(address, portUnreliable);

        TcpThread = new Thread(TcpUpdate);
        TcpThread.Name = "Lightnet - TCP Update";
        TcpThread.Start();
        UdpThread = new Thread(UdpUpdate);
        UdpThread.Name = "Lightnet - UDP Update";
        UdpThread.Start();

        return true;
    }

    public ENetworkState GetState()
    {
        return State;
    }

    public bool IsServer()
    {
        return bIsServer;
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
        Debug.Assert(TcpThread != null);
        Debug.Assert(TcpThread.IsAlive);
        Debug.Assert(UdpThread != null);
        Debug.Assert(UdpThread.IsAlive);
        Debug.Assert(Unrealiable != null);

        bIsServer = false;
        State = ENetworkState.Shutdown;
        bShutdown = true;

        void AbortThreads()
        {
            if (!TcpThread.Join(SHUTDOWN_TIMEOUT))
            {
                TcpThread.Abort();
            }

            // might have been already killed by TcpThread
            if (UdpThread != null && !UdpThread.Join(SHUTDOWN_TIMEOUT))
            {
                UdpThread.Abort();
            }

            TcpThread = null;
            UdpThread = null;
            State = ENetworkState.Closed;
        }

        Thread abortThread = new Thread(AbortThreads);
        abortThread.Start();

        Server?.Stop();
        Server = null;
        Client?.Close();
        Client = null;
        Unrealiable?.Close();
        Unrealiable = null;

        ClientStateReliable = EClientState.Disconnected;
        ClientStateUnreliable = EClientState.Disconnected;
        return true;
    }

    public bool GetNextMessage(ENetChannel channel, out byte[] data)
    {
        if (channel == ENetChannel.Reliable)
        {
            return ReliableMessages.TryDequeue(out data);
        }
        else if (channel == ENetChannel.Unreliable)
        {
            return UnreliableMessages.TryDequeue(out data);
        }
        data = null;
        return false;
    }

    public void SendMessage(ENetChannel channel, byte[] data)
    {
        if (State != ENetworkState.Running)
        {
            LogQueue.LogWarning("Cannot send message while not Running!");
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

    // responsible for shutdown in case of disconnect / fail
    // TODO: maybe have a third thread for this? may be overpowered though...
    void TcpUpdate()
    {
        void Shutdown()
        {
            State = ENetworkState.Shutdown;

            bShutdown = true;
            if (!UdpThread.Join(SHUTDOWN_TIMEOUT))
            {
                UdpThread.Abort();
            }

            Server?.Stop();
            Server = null;
            Client?.Close();
            Client = null;
            Unrealiable?.Close();
            Unrealiable = null;
            ClientStateReliable = EClientState.Disconnected;
            ClientStateUnreliable = EClientState.Disconnected;
            bIsServer = false;
            Server = null;
            Client = null;
            TcpThread = null;
            UdpThread = null;
            State = ENetworkState.Closed;
        }

        void ConnectionLost()
        {
             LogQueue.LogWarning("Connection lost");
             Shutdown();
        }

        while (!bShutdown)
        {
            if (State == ENetworkState.Startup)
            {
                // if we're server, wait for incoming connection
                if (bIsServer)
                {
                    Debug.Assert(Client == null);
                    Debug.Assert(ClientStateReliable == EClientState.Connecting);
                    try
                    {
                         // this will block the thread until someone connects
                        Client = Server.AcceptTcpClient();
                    }
                    catch (Exception e) 
                    {
                        LogQueue.LogWarning(e.Message);
                        Shutdown();
                        return;
                    }
                }

                // if we're client, try connect to specified remote
                if (!bIsServer && ClientStateReliable == EClientState.Connecting)
                {
                    try
                    {
                        Client.Connect(RemoteReliable);
                    }
                    catch (Exception e)
                    {
                        LogQueue.LogWarning(e.Message);
                        Shutdown();
                        return;
                    }
                }

                // check whether connecting (server or client) was indeed successfull
                if (Client.Connected)
                {
                    ClientStateReliable = EClientState.Connected;
                }

                // everything is connected, we're officially running!
                if (ClientStateReliable == EClientState.Connected && ClientStateUnreliable == EClientState.Connected)
                {
                    State = ENetworkState.Running;
                }

                continue;
            }

            // check every frame whether our connection is still alive
            if (!Client.Connected)
            {
                ClientStateReliable = EClientState.Disconnected;
            }

            // when either one connection failed / is lost, shutdown
            if (ClientStateUnreliable == EClientState.Disconnected || ClientStateUnreliable == EClientState.Disconnected)
            {
                ConnectionLost();
                return;
            }

            // RECEIVING
            NetworkStream stream = null;
            try
            {
                stream = Client.GetStream();
            }
            catch (Exception e)
            {
                LogQueue.LogWarning(e.Message);
                ConnectionLost();
                return;
            }
            while (stream.DataAvailable)
            {
                int maxReadSize = CHANNEL_BUFFER_SIZE - ReliableReceiveBuffer.Head;
                if (maxReadSize == 0)
                {
                    LogQueue.LogError("Ran out of receive buffer memory! Message too large?");
                    ReliableReceiveBuffer.Head = 0;
                    Shutdown();
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
                    ConnectionLost();
                    return;
                }
                ReliableReceiveBuffer.Head += bytesRead;

                ushort msgSize;
                if (ReliableReceiveBuffer.Head < sizeof(ushort))
                {
                    continue;
                }

                int offset = 0;
                msgSize = BitConverter.ToUInt16(ReliableReceiveBuffer.Buffer, 0); offset += sizeof(ushort);
                if (ReliableReceiveBuffer.Head >= sizeof(ushort) + msgSize)
                {
                    // copy message from receive buffer into message queue
                    byte[] message = new byte[msgSize];
                    Array.Copy(ReliableReceiveBuffer.Buffer, offset, message, 0, msgSize);
                    ReliableMessages.Enqueue(message);

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

            // SENDING
            try
            {
                stream = Client.GetStream();
            }
            catch (Exception e)
            {
                LogQueue.LogWarning(e.Message);
                ConnectionLost();
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
                    ConnectionLost();
                    return;
                }
            }
        }
    }

    void UdpUpdate()
    {
        while (!bShutdown)
        {
            if (State == ENetworkState.Startup)
            {
                if (!bIsServer && ClientStateUnreliable == EClientState.Connecting)
                {
                    try
                    {
                        Unrealiable.Connect(RemoteUnreliable);
                    }
                    catch (SocketException e)
                    {
                        LogQueue.LogWarning(e.Message);
                        ClientStateUnreliable = EClientState.Disconnected;
                        return;
                    }

                    if (Unrealiable.Client.Connected)
                    {
                        ClientStateUnreliable = EClientState.Connected;
                    }
                }

                continue;
            }

            if (!bIsServer && !Unrealiable.Client.Connected)
            {
                ClientStateUnreliable = EClientState.Disconnected;
                continue;
            }

            int maxReadSize = CHANNEL_BUFFER_SIZE - UnreliableReceiveBuffer.Head;
            if (maxReadSize == 0)
            {
                LogQueue.LogError("Ran out of receive buffer memory! Message too large?");
                ReliableReceiveBuffer.Head = 0;
                ClientStateUnreliable = EClientState.Disconnected;
                return;
            }

            // RECEIVING
            while (Unrealiable.Available > 0)
            {
                byte[] data = null;
                try
                {
                    data = Unrealiable.Receive(ref RemoteUnreliable);
                }
                catch (Exception e) 
                {
                    LogQueue.LogWarning("UDP Receiving failed: " + e.Message);
                    ClientStateUnreliable = EClientState.Disconnected;
                    return;
                }

                int bytesToRead = Math.Min(data.Length, maxReadSize);
                Array.Copy(data, 0, UnreliableReceiveBuffer.Buffer, UnreliableReceiveBuffer.Head, bytesToRead);
                UnreliableReceiveBuffer.Head += bytesToRead;

                ulong timestamp;
                ushort msgSize;
                if (UnreliableReceiveBuffer.Head < sizeof(ulong) + sizeof(ushort))
                {
                    continue;
                }

                int offset = 0;
                timestamp = BitConverter.ToUInt64(UnreliableReceiveBuffer.Buffer, offset); offset += sizeof(ulong);
                msgSize = BitConverter.ToUInt16(UnreliableReceiveBuffer.Buffer, offset);   offset += sizeof(ushort);
                if (UnreliableReceiveBuffer.Head >= offset + msgSize)
                {
                    byte[] message = new byte[msgSize];
                    Array.Copy(UnreliableReceiveBuffer.Buffer, offset, message, 0, msgSize);
                    UnreliableMessages.Enqueue(timestamp, message);

                    int remanining = CHANNEL_BUFFER_SIZE - UnreliableReceiveBuffer.Head;
                    if (remanining > 0)
                    {
                        Array.Copy(UnreliableReceiveBuffer.Buffer, UnreliableReceiveBuffer.Head, UnreliableReceiveBuffer.Buffer, 0, remanining);
                    }
                    UnreliableReceiveBuffer.Head = 0;
                }
            }

            // SENDING
            byte[] messageData;
            while (UnreliableSendBuffer.TryDequeue(out messageData))
            {
                byte[] timestamp = BitConverter.GetBytes(UnreliableTime++);
                byte[] msgLength = BitConverter.GetBytes((ushort)messageData.Length);
                byte[] sendData = new byte[sizeof(ulong) + sizeof(ushort) + messageData.Length];

                int offset = 0;
                Array.Copy(timestamp, 0, sendData, offset, timestamp.Length); offset += timestamp.Length;
                Array.Copy(msgLength, 0, sendData, offset, msgLength.Length); offset += msgLength.Length;
                Array.Copy(messageData, 0, sendData, offset, messageData.Length);

                try
                {
                    Unrealiable.Send(sendData, sendData.Length);
                }
                catch (Exception e)
                {
                    LogQueue.LogWarning(e.Message);
                    ClientStateUnreliable = EClientState.Disconnected;
                    return;
                }
            }
        }
    }
}