using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;

public class ExampleNetworkManager : MonoBehaviour
{
    [Header("Network Settings")]
    public int PortReliable = 42069;
    public int PortUnreliable = 42169;
    public int MaxClients = 1;

    [Header("Required References")]
    public NetworkComponent NetComp;

    private ExperimentState LastState = new ExperimentState();
    private UserState SendingUserState = new UserState();


    // use this to distinguish between multiplen etwork agents
    // slot '0' is ALWAYS the server
    // the first client will get slot '1', the next slot '2', and so on
    private byte MySlot= 0;

    // server only variable. used to assign slots to incoming clients
    private byte NextSlot = 0;


    public bool StartServer()
    {
        return NetComp.StartServer(PortReliable, PortUnreliable, MaxClients);
    }

    public bool StartClient(string address)
    {
        return NetComp.StartClient(address, PortReliable, PortUnreliable);
    }

    public bool Close()
    {
        return NetComp.Close();
    }

    public ENetworkState GetState()
    {
        return NetComp.GetState();
    }

    public int GetNumConnections()
    {
        return NetComp.GetConnections().Length;
    }

    public bool IsServer()
    {
        return NetComp.IsServer();
    }

    public void BroadCastExperimentStatusUpdate(EExperimentStatus status)
    {
        Debug.LogFormat("Broadcasting experiment status update: {0}", status.ToString());
        NetComp.BroadcastNetworkData(ENetChannel.Reliable, new ExperimentState { Status = status });
    }

    public void BroadcastVRAvatarUpdate(Transform VRHead, Transform VRHandLeft, Transform VRHandRight)
    {
        //Debug.Log("Broadcasting VR avatar update");
        SendingUserState.HeadPosition = VRHead.position;
        SendingUserState.HeadRotation = VRHead.rotation;
        SendingUserState.HandLeftPosition = VRHandLeft.position;
        SendingUserState.HandLeftRotation = VRHandLeft.rotation;
        SendingUserState.HandRightPosition = VRHandRight.position;
        SendingUserState.HandRightRotation = VRHandRight.rotation;
        NetComp.BroadcastNetworkData(ENetChannel.Unreliable, SendingUserState);
    }


    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(NetComp!=null, "Please set a reference to a Network Component");
        NetComp.OnNetworkDataReceived += OnNetworkDataReceived;
        NetComp.OnClientDisconnected += OnClientDisconected;
        NetComp.OnClientConnected += OnClientConnected;
    }

    // Update is called once per frame
    void Update()
    {
        ENetworkState state = GetState();

        if (state==ENetworkState.Running)
        {
            if (NetComp.IsServer())
            {
                UpdateServer();
            }
            else
            {
                UpdateClient();
            }
        }
    }

    void UpdateClient()
    {
        Debug.Assert(GetState()==ENetworkState.Running);

        //if you are Client your Slot number should be higher than 0
        if (MySlot == 0)
        {
            //as long as you dont have slot , dont do anything
            return;
        } 

        // TODO: put your client code here
    }

    void UpdateServer()
    {
        Debug.Assert(GetState()==ENetworkState.Running);

        // TODO: put your server code here
    }
    
    void OnNetworkDataReceived(object sender, ReceivedNetworkDataEventArgs e)
    {
        NetworkData data = e.Data;
        if (e.Type == ENetDataType.ExperimentState)
        {
            ExperimentState state = (ExperimentState)data;
            MySlot = state.SocketNumber;
        }
    }
    
    void OnClientConnected(object sender, ConnectionEventArgs e)
    {
        if (NetComp.IsServer())
        {
            ExperimentState state = new ExperimentState();
            state.SocketNumber = NextSlot++;
            NetComp.SendNetworkData(e.Handle, ENetChannel.Reliable, state);
        }
    }
    
    void OnClientDisconected(object sender, ConnectionEventArgs e)
    {
        
    }
}
