using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;

public class ExampleNetworkManager : MonoBehaviour
{
    private NetworkComponent Net;
    private ExperimentState LastState;
    private byte MySlot= 0;
    private byte NextSlot = 0;    //Server only 
    
    public void StartClient()
    {
        Net.StartAsClient("127.0.0.1");
        MySlot = 0;
    }

    public void StartServer()
    {
        Net.StartAsServer();
        MySlot = NextSlot++;
    }

    public void Close()
    {
        Net.Close();
    }

    public ENetworkState GetState()
    {
        return Net.GetState();
    }

    public void BroadCastExperimentStatusUpdate(EExperimentStatus experimentStatus)
    {
        LastState.Status = experimentStatus;
        Net.BroadcastNetworkData(ENetChannel.Reliable, LastState);
    }

    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(Net!=null, "Please set a Network Component");
        Net.OnNetworkDataReceived += OnNetworkDataReceived;
        Net.OnClientDisconnected += OnClientDisconected;
        Net.OnClientConnected += OnClientConnected;
    }

    // Update is called once per frame
    void Update()
    {
        ENetworkState state = GetState();

        if (state==ENetworkState.Running)
        {
            if (Net.IsServer())
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
        if (MySlot == 0)//if you are Client your Slot number should be higher than 0
        {
            return; //as long as you dont have slot , dont do anything
        } 
    }

    void UpdateServer()
    {
        Debug.Assert(GetState()==ENetworkState.Running);
    }
    
    void OnNetworkDataReceived(object sender, ReceivedNetworkDataEventArgs e)
    {
        NetworkData data = e.State;
        if (e.Type == ENetDataType.ExperimentState)
        {
            ExperimentState state = (ExperimentState)data;
            MySlot = state.socketNumber;
        }
    }
    
    void OnClientConnected(object sender, ConnectionEventArgs e)         //if you are a server than, you handle clients
    {
        if (Net.IsServer())
        {
            ExperimentState state = new ExperimentState();
            state.socketNumber = NextSlot++;
            Net.SendNetworkData(ENetChannel.Reliable, e.Handle,state);
        }
    }
    
    void OnClientDisconected(object sender, ConnectionEventArgs e)
    {
        ExperimentManager.Instance().ClientDisconected();
    }
}
