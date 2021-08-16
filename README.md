

# lightnet-unity
Lightweight C# networking library for Unity.

## Brief
This small library aims to provide a minimum, low-level networking layer, without additional abstraction layers like "component synchronization", or "remote procedure calls". You're just sending and receiving data (raw bytes) however you like, when you like. This library is only handling:

 - Server / Client mode
 - Connection handling (connect, disconnect)
 - Events (on connected, on disconnected, received message, etc.)
 - Simple message serialization, without using reflection

While providing two channels for each connection:

| Mode | Protocol | Description |
|---|---|---|
| Reliable | TCP | Always receive all packages, in order. |
| Unreliable | UDP | Might not receive all packages, but order is still guaranteed through internal implementation. |


## Structure
### NetworkService
The core, `NetworkService`, is pretty much platform independent (except for two methods), and implements all network functionalities by wrapping around basic sockets, implementing server and client behaviour respectively, providing thread safe events (event queue), etc. It does so in a multi threaded way, meaning that every connection willl be handled by a dedicated thread and has it's dedicated receive buffer.

You'll never have to use this class manually in Unity! See next:

### NetworkComponent
Wraps around `NetworkService` as a Unity component (`MonoBehaviour`), bringing everything together into the main thread (the only thread where it's legal to call the Unity API), providing events like `OnClientConnected` or `OnNetworkDataReceived`, which can thus be handled from anywhere within Unity. It also provides a `NetworkData` interface, which generalizes data struct serilization. See "How to use" below.

## How to use
### Setup

 1. Clone this repository into your Unity projects "*Assets*" folder (or download the source zip)
 2. Implement a data struct you'd like to send over the network (see `TransformMessage` example below), implementing the `NetworkData` interface. By **convention**, the **first byte** of every message always represents the message type ID! This ID is used to distinguish between messages on a low level. So make sure every implementation of `NetworkData` has it's unique ID! (see example below)
 3. Place an empty GameObject into your scene and attach the `NetworkComponent` to it.
 4. Call it's `RegisterNetworkDataClass` method to register your `NetworkData` implementation.
 5. Handle it's `OnNetworkDataReceived` event somewhere in your code.
 6. Call `StartServer` or `StartClient` on it, depending on the role.
 7. Create a `NetworkData` struct, like our `TransformMessage` example below, and send it via `SendNetworkData` or `BroadcastNetworkData`, choosing an appropriate channel (reliable, unreliable).
 8. That's it! The receiving side will fire the `OnNetworkDataReceived` event, providing an instance of your `NetworkData` type (you'll have to cast it appropriately).

### Channels
#### Reliable
It is advised to **not** use the reliable channel for everything being transmitted very frequently, like position or rotation updates. These are updates where loosing a package usually doesn't break anything critical, and waiting for a lost package to be resend, although newer packages might have already arrived, can actually harm performance significantly.
Use it for things like important, unfrequent state updates only, like "next round" or "game ended".

#### Unreliable
It is advised to **not** send any important game state updates through this channel! Since it's transmission is not guaranteed, your game state can be easily broken in such a case. Use it for less important, non breaking or frequent updates.

### NetworkData implementation example
```csharp
    struct TransformMessage : LightNet.NetworkData
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public byte[] Serialize()
        {
            int offset = 0;
            byte[] data = new byte[1 + sizeof(float) * 7];
            data[offset++] = 1; // message ID, arbitrarily chosen
            LightNet.SerializationHelper.ToBytes(in Position, data, ref offset);
            LightNet.SerializationHelper.ToBytes(in Rotation, data, ref offset);
            return data;
        }

        public void Deserialize(byte[] data)
        {
            int offset = 0;
            Debug.Assert(data[offset++] == 1); // assert correct message ID
            LightNet.SerializationHelper.FromBytes(data, ref offset, out Position);
            LightNet.SerializationHelper.FromBytes(data, ref offset, out Rotation);
        }
    }
```
Where the returned `byte[]` array returned could also be cached of course.
```csharp
// Register our NetworkData implementation and associate it with ID '1'
networkComponentInstance.RegisterNetworkDataClass(1, typeof(TransformMessage));
```


## Available Component Methods
|Method Name|Description|
|--|--|
|StartServer|Start as a Server on the given TCP/UDP ports and number of maximum clients.|
|StartClient|Start as a Client, connecting to the given Server address (ip, ports).|
|Close|Closes all Networking (both roles).|
|IsServer|Will return `true` **only if** we're a running Server, `false` in **all** other cases.|
|GetState|Returns the current network state. Can be:<br/>- Closed<br/>- Startup<br/>- Running<br/>- Shutdown|
|GetConnections|Will return `ConnectionHandle`'s for all open connections.<br/>A `ConnectionHandle` can be used to send data to this specific connection.<br/>As a client, this will at most return one connection: The one to the Server.|
|GetConnectionNames|Will return a string representation (ip, port, etc.) for all open connections.<br/>As a client, this will at most return one connection: The one to the Server.|
|RegisterNetworkDataClass|Register a NetworkData implementation and accociate it with a unique ID.|
|SendNetworkData|Send a message to a specific peer only, using the given `ConnectionHandle`.|
|BroadcastNetworkData|Send a message to everyone.|
|SetNetworkErrorEmulation|This is usefull to emulate a bad network connection, by randomly dropping or delaying packages. Call this with `(0, 0)`, to deactivate the emulation.|
