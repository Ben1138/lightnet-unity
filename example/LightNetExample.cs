using LightNet;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightNetExample : MonoBehaviour
{
    public const int PORT_RELIABLE = 42069;
    public const int PORT_UNRELIABLE = 42169;

    public NetworkComponent Net;
    public Transform CubeServer;
    public Transform CubeClient;

    CubeTransformData Data;

    void Awake()
    {
        Debug.Assert(Net != null);
        Debug.Assert(CubeServer != null);
        Debug.Assert(CubeClient != null);

        Data = new CubeTransformData();
        Net.RegisterNetworkDataClass<CubeTransformData>(CubeTransformData.ID);

        Net.OnNetworkDataReceived += OnDataReceive;
    }

    void Update()
    {
        if (Net.GetState() == ENetworkState.Running)
        {
            float inputX = Input.GetAxis("Horizontal");
            float inputY = Input.GetAxis("Vertical");

            if (Net.IsServer())
            {
                CubeServer.position = CubeServer.position + new Vector3(inputX, 0f, inputY) * Time.deltaTime;

                Data.Position = CubeServer.position;
                Data.Rotation = CubeServer.rotation;
            }
            else
            {
                CubeClient.position = CubeClient.position + new Vector3(inputX, 0f, inputY) * Time.deltaTime;

                Data.Position = CubeClient.position;
                Data.Rotation = CubeClient.rotation;
            }

            Net.BroadcastNetworkData(ENetChannel.Unreliable, Data);
        }
    }

    void OnDataReceive(object sender, ReceivedNetworkDataEventArgs args)
    {
        if (args.Type == CubeTransformData.ID)
        {
            CubeTransformData data = (CubeTransformData)args.Data;
            if (Net.IsServer())
            {
                CubeClient.position = data.Position;
                CubeClient.rotation = data.Rotation;
            }
            else
            {
                CubeServer.position = data.Position;
                CubeServer.rotation = data.Rotation;
            }
        }
    }
}

class CubeTransformData : NetworkData
{
    // arbitrarily chosen '1' here
    public const int ID = 1;

    public Vector3 Position;
    public Quaternion Rotation;

    const int SIZE = 
        sizeof(byte) +          // ID
        sizeof(float) * 3 +     // Position
        sizeof(float) * 4;      // Rotation

    // Cache the data, so we don't reallocate for each serialization.
    // This is not mandatory though.
    byte[] Data;

    public CubeTransformData()
    {
        Data = new byte[SIZE];
    }

    public byte[] Serialize()
    {
        int offset = 0;
        Data[offset++] = ID; 
        SerializationHelper.ToBytes(in Position, Data, ref offset);
        SerializationHelper.ToBytes(in Rotation, Data, ref offset);
        return Data;
    }

    public void Deserialize(byte[] data)
    {
        int offset = 0;
        Debug.Assert(data[offset++] == ID);
        SerializationHelper.FromBytes(data, ref offset, out Position);
    }
}