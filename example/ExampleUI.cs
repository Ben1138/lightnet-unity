using LightNet;
using UnityEngine;
using UnityEngine.UI;

public class ExampleUI : MonoBehaviour
{
    public LightNet.NetworkComponent Net;
    public InputField FldRemoteAddress;
    public Button BtnStartServer;
    public Button BtnStartClient;
    public Button BtnClose;
    public Text TxtState;

    ENetworkState PrevState;

    public void OnStartServerClick()
    {
        Net.StartServer(LightNetExample.PORT_RELIABLE, LightNetExample.PORT_UNRELIABLE, 2);
    }

    public void OnStartClientClick()
    {
        Net.StartClient(FldRemoteAddress.text, LightNetExample.PORT_RELIABLE, LightNetExample.PORT_UNRELIABLE);
    }

    public void OnCloseClick()
    {
        Net.Close();
    }

    void UpdateUI()
    {
        ENetworkState state = Net.GetState();
        if (state == ENetworkState.Closed)
        {
            FldRemoteAddress.gameObject.SetActive(true);
            BtnStartServer.gameObject.SetActive(true);
            BtnStartClient.gameObject.SetActive(true);
            BtnClose.gameObject.SetActive(false);
        }
        else if (state == ENetworkState.Startup || state == ENetworkState.Running)
        {
            FldRemoteAddress.gameObject.SetActive(false);
            BtnStartServer.gameObject.SetActive(false);
            BtnStartClient.gameObject.SetActive(false);
            BtnClose.gameObject.SetActive(true);
        }
        else
        {
            FldRemoteAddress.gameObject.SetActive(false);
            BtnStartServer.gameObject.SetActive(false);
            BtnStartClient.gameObject.SetActive(false);
            BtnClose.gameObject.SetActive(false);
        }

        TxtState.text = state.ToString();
    }

    void Awake()
    {
        Debug.Assert(FldRemoteAddress != null);
        Debug.Assert(BtnStartServer != null);
        Debug.Assert(BtnStartClient != null);
        Debug.Assert(BtnClose != null);
        Debug.Assert(TxtState != null);

        PrevState = ENetworkState.Closed;
        UpdateUI();
    }

    void Update()
    {
        ENetworkState state = Net.GetState();
        if (state != PrevState)
        {
            UpdateUI();
            PrevState = state;
        }
    }
}
