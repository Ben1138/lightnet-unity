using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;

public class SampleNetworkManager : MonoBehaviour
{
    //assigns Server and Clients, contains the Network service
    
    private NetworkComponent net;

    private ExperimentState lastState;
    
    private byte MySlot= 0;

    private byte NextSlot = 0;    //Server only 
    
    public void StartClient()
    {
        net.StartAsClient("127.0.0.1");
        MySlot = 0;
    }

    public void  StartServer()
    {
        net.StartAsServer();
        MySlot = NextSlot++;
    }

    public void  Close()
    {
        net.Close();
    }
    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(net!=null, "Please set a Network Component");
        net.OnNetworkDataReceived += OnUserStateReceived;
        net.OnClientDisconnected += OnClientDisconected;
        net.OnClientConnected += OnClientConnected;
    }

    // Update is called once per frame
    void Update()
    {
        ENetworkState state = GetState();

        if (state==ENetworkState.Running)
        {
            if (net.IsServer())
            {
                UpdateServer();
            }
            else
            {
                UpdateClient();
            }
        }
        
        
    }

    private void UpdateClient()
    {
        Debug.Assert(GetState()==ENetworkState.Running);
        if (MySlot == 0)//if you are Client your Slot number should be higher than 0
        {
            return;    //as long as you dont have slot , dont do anything
        } 
    }

    private void UpdateServer()
    {
        Debug.Assert(GetState()==ENetworkState.Running);
    }
    
    public ENetworkState GetState()
    {
        return net.GetState();
    }
    
    void OnUserStateReceived(object sender, ReceivedNetworkDataEventArgs e)
    {
        NetworkData data = e.State;

        if (e.Type == ENetDataType.ExperimentState)
        {
            ExperimentState state =(ExperimentState) data;
            MySlot = state.socketNumber;
        }
    }
    
    void OnClientConnected(object sender, ConnectionEventArgs e)         //if you are a server than, you handle clients
    {
        if (net.IsServer())
        {
            ExperimentState state = new ExperimentState();
            state.socketNumber = NextSlot++;
            net.SendNetworkData(e.Handle,state);
        }
    }
    
     
    
    void OnClientDisconected(object sender, ConnectionEventArgs e)
    {
        ExperimentManager.Instance().ClientDisconected();
    }

    public void BroadCastExperimentStatusUpdate(ExperimentStatus experimentStatus)
    {
        lastState.experimentStatus = experimentStatus;
        net.BroadcastNetworkData(lastState);
    }
}
