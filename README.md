

# lightnet-unity
Lightweight C# networking library for Unity.

## Brief
This small library aims to provide a minimum, low-level networking layer, without additional abstraction layers like "component synchronization", or "remote procedure calls". You're just sending and receiving data (raw bytes) however you like, when you like. This library is only handling:

 - Server / Client mode
 - Connection handling (connect, disconnect)
 - Events (on connected, on disconnected, received message, etc.)
 - Simple message serialization, without using reflection
 - Reliable / unreliable data transmission


## Structure
### NetworkService
The core, `NetworkService`, is pretty much platform independent (except for two methods), and implements all network functionalities by wrapping around basic sockets, implementing server and client behaviour respectively, providing thread safe events (event queue), etc. It does so in a multi threaded way, meaning that every connection willl be handled by a dedicated thread and has it's dedicated receive buffer.

You'll never have to use this class manually in Unity! See next:

### NetworkComponent
Wraps around `NetworkService` as a Unity component (`MonoBehaviour`), bringing everything together into the main thread (the only thread where it's legal to call the Unity API), providing events like `OnClientConnected` or `OnNetworkDataReceived`, which can thus be handled from anywhere within Unity. It also provides a `NetworkData` interface, which generalizes data struct serilization. See "How to use" below.

## How to use
### Setup

 1. Clone this repository into your Unity projects "*Assets*" folder (or download the source zip)
 2. Implement a data struct you'd like to send over the network (see *implementation example*), implementing the `NetworkData` interface. By **convention**, the **first byte** of every message always represents the message type ID! This ID is used to distinguish between messages on a low level. So make sure every implementation of `NetworkData` serializes it's unique ID! (for further explanation, see "*On the `NetworkData` convention*" below)
 3. Place an empty GameObject into your scene and attach the `NetworkComponent` to it.
 4. Call it's `RegisterNetworkDataClass` method to register your `NetworkData` implementation.
 5. Handle it's `OnNetworkDataReceived` event somewhere in your code.
 6. Call `StartServer` or `StartClient` on it, depending on the role.
 7. Create an instance of your custom `NetworkData` struct and send it via `SendNetworkData` or `BroadcastNetworkData`, choosing an appropriate channel (reliable, unreliable).
 8. That's it! The receiving side will fire the `OnNetworkDataReceived` event, providing an instance of your `NetworkData` type (you'll have to cast it appropriately).

### Implementation Example
in the subfolder "*example*" within this repository, there's a simple implementation example you can look at or use as a template for your own project.

### Connection Handles
A `ConnectionHandle` is representing a single connection to another peer. Where Servers usually maintain mutiple connections, a Client only has one at most: The one to the Server. You can grab a list of `ConnectionHandle`'s by calling `GetConnections`. Using a ConnectionHandle, you can inspect that specific connection whether it's still alive, close it, or send a message to just that peer. See *Available Component Methods* below.
A ConnectionHandle is always unique and never ambiguous. Meaning, you can store these wherever you want and use them later.

### Channels
There are two distinct channels you can send messages through:
| Mode | Internal Protocol | Description |
|---|---|---|
| Reliable | TCP | Always receive **all** packages, in **order**.<br/><br/>It is advised to **not** use the reliable channel for everything being transmitted very frequently, like position or rotation updates. These are updates where loosing a package usually doesn't break anything critical, and waiting for a lost package to be resend, although newer packages might have already arrived, can actually harm performance and increase network latency significantly.Use it for things like important, unfrequent state updates only, like "next round" or "game ended".|
| Unreliable | UDP | Might **not** receive all packages, but **order is still guaranteed** through internal implementation.<br/><br/>It is advised to **not** send any important game state updates through this channel! Since it's transmission is not guaranteed, your game state can be easily broken in such a case. Use it for less important, non breaking or frequent updates.|

### On the `NetworkData` convention
We could potentially make the process of serializing struct data way more generic, crawling over struct member fields using reflection, automatically assign message IDs for every type and ensure their transmission in the data buffer, etc. But first, this comes at a significant performance cost, which at this point I don't deem adequate, and secondly this takes away serialization authority from the user, which was one of the main points to write this library. So as long as you just follow the "*First byte of the message denotes the message type*" convention, you'll be fine.

## Available Component Methods
|Method Name|Description|
|--|--|
|`StartServer`|Start as a Server on the given TCP/UDP ports and number of maximum clients.|
|`StartClient`|Start as a Client, connecting to the given Server address (ip, ports).|
|`Close`|Closes **all** connections and shuts down Networking overall (both roles).|
|`Disconnect`|Close the given **specific** connection. Returns `false` if the connection is unknown, e.g. due to being already closed.<br/>If we're a running client, this call is synonymous with calling `Close()`.|
|`IsServer`|Will return `true` **only if** we're a running Server, `false` in **all** other cases.|
|`IsAlive`|Returns `true` if the given connection is indeed still alive (connected)|
|`GetState`|Returns the current network state. Can be:<br/>- Closed<br/>- Startup<br/>- Running<br/>- Shutdown|
|`GetConnections`|Will return `ConnectionHandle`'s for all open connections.|
|`GetConnectionNames`|Will return a string representation (ip, port, etc.) for all open connections.|
|`RegisterNetworkDataClass`|Register a NetworkData implementation and accociate it with a unique ID.|
|`SendNetworkData`|Send a message to a **specific** peer only, using the given `ConnectionHandle`.|
|`BroadcastNetworkData`|Send a message to **everyone**.|
|`SetNetworkErrorEmulation`|This is usefull to emulate a bad network connection, by randomly dropping or delaying packages. Call this with `(0, 0)`, to deactivate the emulation (default).|

### Hard coded settings
There are some hard coded settings, which you usually don't need to change. If you indeed do, for whatever reason, you can do so by modifying these constants in `NetworkService`:
|Constant|Default Value|Description|
|--|--|--|
|`PING_INTERVAL`|1000|How frequently pings are send in **milliseonds** to the peer of a connection, to signal being still *alive*.|
|`PING_TIMEOUT`|5.0|After how much **seconds** a connection is considered being lost after not receiving any ping signals from the other side.|
|`SHUTDOWN_TIMEOUT`|2000|The maximum time in **milliseconds** to wait for each thread to shut down, before killing it forcefully.|
|`CHANNEL_BUFFER_SIZE`|2048|Size of the receive buffer of **each** connection. This buffer can overflow if you're sending very large messages very frequently and will print a warning in such a case. Increase it only if you really have to, or even decrease it to reduce memory consumption.|
|`QUEUE_WARN_THRESHOLD`|1000|If any of the internal queues (messages, events) exceeds this number of entries, print warnings as a reminder. This usually occours, when the `NetworkComponent` (or another user) is not frequently asking for new messages or events. This could happen if you, for example, destroy the instance on `NetworkComponent` after networking has already been started.|

### Port forwarding
When running as Server, the library uses subsequent UDP ports for every new connection, while still listening on the original UDP port for incoming connection requests. This means, that with e.g. a Server started at unreliable port 1000 and a maximum of 32 connections, UDP ports 1001 - 1032 will be used for individual UDP transmissions. As such, UDP ports 1000-1032 need to be forwarded.

At this point, there are no intentions to implement neither a proper Port management, nor features like NAT traversal or P2P connections. Keep it as simple as possible.
