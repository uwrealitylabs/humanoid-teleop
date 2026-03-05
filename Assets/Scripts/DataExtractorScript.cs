using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Net.WebSockets;
using CandyCoded.env;

public class UGDataExtractorScript : MonoBehaviour
{
    // Data sources - OVRSkeleton is on the same GameObject as OVRHand in Oculus Integration
    [Header("Specify Data Sources")]
    public OVRHand leftOVRHand;
    public OVRHand rightOVRHand;
    private OVRSkeleton leftSkeleton;
    private OVRSkeleton rightSkeleton;

    // WebSocket connection
    private ClientWebSocket webSocket;
    private bool isConnected = false;
    private CancellationTokenSource cts;
    private string wsUrl;
    private bool _isReceiving = false;

    // Delegate for receiving messages (will be used for robot diagnostics)
    public delegate void MessageReceivedHandler(string message);
    public event MessageReceivedHandler OnMessageReceived;

    // Hand data: 3D joint positions (x, y, z) per joint name, e.g. "right_wrist", "right_thumb_knuckle"
    [HideInInspector]
    public Dictionary<string, Vector3> leftHandData;
    [HideInInspector]
    public Dictionary<string, Vector3> rightHandData;
    [HideInInspector]
    public Dictionary<string, Vector3> twoHandsData;

    // BoneId to friendly joint name (no hand prefix)
    private static readonly Dictionary<OVRSkeleton.BoneId, string> BoneIdToName = new Dictionary<OVRSkeleton.BoneId, string>
    {
        { OVRSkeleton.BoneId.Hand_WristRoot, "wrist" },
        { OVRSkeleton.BoneId.Hand_ForearmStub, "forearm_stub" },
        { OVRSkeleton.BoneId.Hand_Thumb0, "thumb_knuckle" },
        { OVRSkeleton.BoneId.Hand_Thumb1, "thumb_1" },
        { OVRSkeleton.BoneId.Hand_Thumb2, "thumb_2" },
        { OVRSkeleton.BoneId.Hand_Thumb3, "thumb_3" },
        { OVRSkeleton.BoneId.Hand_Index1, "index_knuckle" },
        { OVRSkeleton.BoneId.Hand_Index2, "index_2" },
        { OVRSkeleton.BoneId.Hand_Index3, "index_tip" },
        { OVRSkeleton.BoneId.Hand_Middle1, "middle_knuckle" },
        { OVRSkeleton.BoneId.Hand_Middle2, "middle_2" },
        { OVRSkeleton.BoneId.Hand_Middle3, "middle_tip" },
        { OVRSkeleton.BoneId.Hand_Ring1, "ring_knuckle" },
        { OVRSkeleton.BoneId.Hand_Ring2, "ring_2" },
        { OVRSkeleton.BoneId.Hand_Ring3, "ring_tip" },
        { OVRSkeleton.BoneId.Hand_Pinky0, "pinky_knuckle" },
        { OVRSkeleton.BoneId.Hand_Pinky1, "pinky_1" },
        { OVRSkeleton.BoneId.Hand_Pinky2, "pinky_2" },
        { OVRSkeleton.BoneId.Hand_Pinky3, "pinky_tip" },
        { OVRSkeleton.BoneId.Hand_ThumbTip, "thumb_tip" },
        { OVRSkeleton.BoneId.Hand_IndexTip, "index_tip_end" },
        { OVRSkeleton.BoneId.Hand_MiddleTip, "middle_tip_end" },
        { OVRSkeleton.BoneId.Hand_RingTip, "ring_tip_end" },
        { OVRSkeleton.BoneId.Hand_PinkyTip, "pinky_tip_end" },
    };

    //UI
    public GameObject startMenu;
    public GameObject socketConnectedText;
    public GameObject cantConnectSocketText;

    void Start()
    {
        //deactivate
        socketConnectedText.SetActive(false);
        cantConnectSocketText.SetActive(false);
        // Set up data sources
        bool setupSuccess = SetupAndValidateConfiguration();
        if (!setupSuccess)
        {
            Debug.LogError("UGDataExtractorScript: Data source setup failed. Disabling script.");
            gameObject.SetActive(false);
            return;
        }

        // Get WebSocket URL from environment variable
        if (env.TryParseEnvironmentVariable("URL", out string url))
        {
            wsUrl = url;
            Debug.Log($"WebSocket URL from environment: {wsUrl}");
            
            
        }
        else
        {
            Debug.LogError("Failed to get WebSocket URL from environment variable. Please ensure the URL environment variable is set.");
        }

    }

    public async void ConnectWebSocket()
    {
        try
        {
            webSocket = new ClientWebSocket();
            cts = new CancellationTokenSource();
            
            Debug.Log($"Connecting to WebSocket server at {wsUrl}...");
            await webSocket.ConnectAsync(new System.Uri(wsUrl), cts.Token);

            //Remove start menu
            startMenu.SetActive(false);

            //indicate successful connection
            socketConnectedText.SetActive(true);
            cantConnectSocketText.SetActive(false);

            isConnected = true;
            Debug.Log("WebSocket connected successfully!");



            // Start listening for messages
            StartCoroutine(StartReceiveLoop());




        }
        catch (System.Exception e)
        {
            Debug.LogError($"WebSocket connection error: {e.Message}");

            //indicate unsuccessful connection
            cantConnectSocketText.SetActive(true);
            // Attempt reconnection after delay
            StartCoroutine(ReconnectAfterDelay());
        }
    }

    private IEnumerator ReconnectAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        if (!isConnected)
        {
            Debug.Log("Attempting to reconnect...");
            ConnectWebSocket();
        }
    }

    private async Task ReceiveLoop()
    {
        if (_isReceiving)
        {
            Debug.LogWarning("Receive loop already running");
            return;
        }

        _isReceiving = true;

        byte[] buffer = new byte[4096];

        MemoryStream messageStream = new MemoryStream();
        
        while (isConnected && webSocket.State == WebSocketState.Open)
        {
            try
            {
                ArraySegment<byte> segment = new ArraySegment<byte>(buffer);
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(segment, cts.Token);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    isConnected = false;
                    Debug.Log("WebSocket connection closed by server");
                    break;
                }
                //ewfm
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageStream.Write(buffer, 0, result.Count);

                    // Check if this is the final message fragment
                    if (result.EndOfMessage)
                    {
                        string message = Encoding.UTF8.GetString(messageStream.ToArray());
                        Debug.Log($"Received message: {message}");
                        OnMessageReceived?.Invoke(message);

                        // Reset the stream for the next message
                        messageStream.SetLength(0);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Handle binary messages if needed
                    messageStream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        // Process binary message
                        messageStream.SetLength(0);
                    }
                }
            }
            catch (System.Exception e)
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    Debug.LogError($"Error in WebSocket receive loop: {e.Message}");
                    isConnected = false;
                }
                break;
            }
        }
    }

    private IEnumerator StartReceiveLoop()
    {
        Task receiveTask = ReceiveLoop();
        while (!receiveTask.IsCompleted)
        {
            yield return null;
        }
        
        if (receiveTask.IsFaulted)
        {
            Debug.LogError($"ReceiveLoop failed: {receiveTask.Exception}");
        }
    }

    private async Task SendMessage(string message)
    {
        if (!isConnected || webSocket.State != WebSocketState.Open)
            return;
            
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Send message error: {e.Message}");
            isConnected = false;
        }
    }

    void Update()
    {
        leftHandData = GetOneHandData(leftSkeleton, "left_");
        rightHandData = GetOneHandData(rightSkeleton, "right_");
        twoHandsData = GetTwoHandsData();

        if (isConnected && webSocket != null)
        {
            var data = new
            {
                type = "rightHandData",
                handData = rightHandData
            };
            string jsonData = JsonConvert.SerializeObject(data);
            SendMessage(jsonData).ConfigureAwait(false);
        }
    }

    void OnDestroy()
    {
        if (isConnected && webSocket != null)
        {
            isConnected = false;
            cts.Cancel();
            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Application closing", CancellationToken.None);
            cts.Dispose();
        }
    }

    bool SetupAndValidateConfiguration()
    {
        if (leftOVRHand == null || rightOVRHand == null)
        {
            Debug.LogError("UGDataExtractorScript: Data source setup failed. Ensure left OVR hand and right OVR hand are assigned.");
            return false;
        }

        leftSkeleton = leftOVRHand.GetComponent<OVRSkeleton>();
        rightSkeleton = rightOVRHand.GetComponent<OVRSkeleton>();
        if (leftSkeleton == null || rightSkeleton == null)
        {
            Debug.LogError("UGDataExtractorScript: OVRSkeleton not found on OVRHand GameObjects. Ensure Oculus Integration hand prefabs include OVRSkeleton.");
            return false;
        }

        return true;
    }

    private Dictionary<string, Vector3> GetOneHandData(OVRSkeleton skeleton, string handPrefix)
    {
        var handData = new Dictionary<string, Vector3>();
        if (skeleton == null || !skeleton.IsDataValid || skeleton.Bones == null)
            return handData;

        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            OVRBone bone = skeleton.Bones[i];
            if (bone.Transform == null) continue;
            if (!BoneIdToName.TryGetValue(bone.Id, out string name)) continue;

            string key = handPrefix + name;
            handData[key] = bone.Transform.position;
        }
        return handData;
    }

    private Dictionary<string, Vector3> GetTwoHandsData()
    {
        var combined = new Dictionary<string, Vector3>();
        Dictionary<string, Vector3> left = GetOneHandData(leftSkeleton, "left_");
        Dictionary<string, Vector3> right = GetOneHandData(rightSkeleton, "right_");
        foreach (var kv in left) combined[kv.Key] = kv.Value;
        foreach (var kv in right) combined[kv.Key] = kv.Value;
        return combined;
    }
}
