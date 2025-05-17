using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.PoseDetection;
using Oculus.Interaction.Input;
using System.Text;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Net.WebSockets;
using CandyCoded.env;

public class UGDataExtractorScript : MonoBehaviour
{
    // Data sources
    [Header("Specify Data Sources")]
    public Hand leftHand;
    public Hand rightHand;
    public OVRHand leftOVRHand;
    public OVRHand rightOVRHand;
    private FingerFeatureStateProvider leftFingerFeatureStateProvider;
    private FingerFeatureStateProvider rightFingerFeatureStateProvider;

    // WebSocket connection
    private ClientWebSocket webSocket;
    private bool isConnected = false;
    private CancellationTokenSource cts;
    private string wsUrl;
    
    // Delegate for receiving messages (will be used for robot diagnostics)
    public delegate void MessageReceivedHandler(string message);
    public event MessageReceivedHandler OnMessageReceived;

    // Hand data output arrays - used by other scripts
    [HideInInspector]
    public float[] leftHandData;
    [HideInInspector]
    public float[] rightHandData;
    [HideInInspector]
    public float[] twoHandsData;

    // Constants
    public const int ONE_HAND_NUM_FEATURES = 17;
    public const int TWO_HAND_NUM_FEATURES = 44;
    

    void Start()
    {
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
            
            // Start WebSocket connection
            ConnectWebSocket();
        }
        else
        {
            Debug.LogError("Failed to get WebSocket URL from environment variable. Please ensure the URL environment variable is set.");
        }
    }

    private async void ConnectWebSocket()
    {
        try
        {
            webSocket = new ClientWebSocket();
            cts = new CancellationTokenSource();
            
            Debug.Log($"Connecting to WebSocket server at {wsUrl}...");
            await webSocket.ConnectAsync(new System.Uri(wsUrl), cts.Token);
            
            isConnected = true;
            Debug.Log("WebSocket connected successfully!");
            
            // Start listening for messages
            StartCoroutine(StartReceiveLoop());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WebSocket connection error: {e.Message}");
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
        byte[] buffer = new byte[4096];
        
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
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Debug.Log($"Received message: {message}");
                    OnMessageReceived?.Invoke(message);
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
        // Update all hand data arrays
        leftHandData = GetOneHandData(leftFingerFeatureStateProvider);
        rightHandData = GetOneHandData(rightFingerFeatureStateProvider);
        twoHandsData = GetTwoHandsData();

        // Send right hand data to WebSocket server
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
        // Ensure all required data sources are provided
        if (leftHand == null || rightHand == null || leftOVRHand == null || rightOVRHand == null)
        {
            Debug.LogError("UGDataExtractorScript: Data source setup failed. Ensure left hand, right hand, left OVR hand, and right OVR hand are provided.");
            return false;
        }

        // Get finger feature state providers
        leftFingerFeatureStateProvider = leftHand.GetComponentInChildren<FingerFeatureStateProvider>();
        rightFingerFeatureStateProvider = rightHand.GetComponentInChildren<FingerFeatureStateProvider>();
        if (leftFingerFeatureStateProvider == null || rightFingerFeatureStateProvider == null)
        {
            Debug.LogError("UGDataExtractorScript: Data source setup failed. Ensure left hand and right hand have children with FingerFeatureStateProvider components.");
            return false;
        }
        return true;
    }

    private float[] GetOneHandData(FingerFeatureStateProvider fingersFeatureProvider)
    {
        float indexFingerCurl = fingersFeatureProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Curl) ?? 0.0f;
        float indexFingerAbduction = fingersFeatureProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Abduction) ?? 0.0f;
        float indexFingerFlexion = fingersFeatureProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Flexion) ?? 0.0f;
        float indexFingerOpposition = fingersFeatureProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Opposition) ?? 0.0f;

        float thumbFingerCurl = fingersFeatureProvider.GetFeatureValue(HandFinger.Thumb, FingerFeature.Curl) ?? 0.0f;
        float thumbFingerAbduction = fingersFeatureProvider.GetFeatureValue(HandFinger.Thumb, FingerFeature.Abduction) ?? 0.0f;
        // Flexion, Opposition not available on thumb

        float middleFingerCurl = fingersFeatureProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Curl) ?? 0.0f;
        float middleFingerAbduction = fingersFeatureProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Abduction) ?? 0.0f;
        float middleFingerFlexion = fingersFeatureProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Flexion) ?? 0.0f;
        float middleFingerOpposition = fingersFeatureProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Opposition) ?? 0.0f;

        float ringFingerCurl = fingersFeatureProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Curl) ?? 0.0f;
        float ringFingerAbduction = fingersFeatureProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Abduction) ?? 0.0f;
        float ringFingerFlexion = fingersFeatureProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Flexion) ?? 0.0f;
        float ringFingerOpposition = fingersFeatureProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Opposition) ?? 0.0f;

        float pinkyFingerCurl = fingersFeatureProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Curl) ?? 0.0f;
        // Pinky does not support abduction
        float pinkyFingerFlexion = fingersFeatureProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Flexion) ?? 0.0f;
        float pinkyFingerOpposition = fingersFeatureProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Opposition) ?? 0.0f;

        float[] handData = new float[] {
            thumbFingerCurl,
            thumbFingerAbduction,
            indexFingerCurl,
            indexFingerAbduction,
            indexFingerFlexion,
            indexFingerOpposition,
            middleFingerCurl,
            middleFingerAbduction,
            middleFingerFlexion,
            middleFingerOpposition,
            ringFingerCurl,
            ringFingerAbduction,
            ringFingerFlexion,
            ringFingerOpposition,
            pinkyFingerCurl,
            pinkyFingerFlexion,
            pinkyFingerOpposition
        };
        return handData;

    }

    private float[] GetTwoHandsData()
    {
        // LEFT HAND FEATURES
        float leftIndexFingerCurl = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Curl) ?? 0.0f;
        float leftIndexFingerAbduction = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Abduction) ?? 0.0f;
        float leftIndexFingerFlexion = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Flexion) ?? 0.0f;
        float leftIndexFingerOpposition = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Opposition) ?? 0.0f;

        float leftThumbFingerCurl = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Thumb, FingerFeature.Curl) ?? 0.0f;
        float leftThumbFingerAbduction = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Thumb, FingerFeature.Abduction) ?? 0.0f;
        // Flexion, Opposition not available on thumb

        float leftMiddleFingerCurl = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Curl) ?? 0.0f;
        float leftMiddleFingerAbduction = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Abduction) ?? 0.0f;
        float leftMiddleFingerFlexion = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Flexion) ?? 0.0f;
        float leftMiddleFingerOpposition = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Opposition) ?? 0.0f;

        float leftRingFingerCurl = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Curl) ?? 0.0f;
        float leftRingFingerAbduction = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Abduction) ?? 0.0f;
        float leftRingFingerFlexion = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Flexion) ?? 0.0f;
        float leftRingFingerOpposition = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Opposition) ?? 0.0f;

        float leftPinkyFingerCurl = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Curl) ?? 0.0f;
        float leftPinkyFingerFlexion = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Flexion) ?? 0.0f;
        float leftPinkyFingerOpposition = leftFingerFeatureStateProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Opposition) ?? 0.0f;

        // RIGHT HAND FEATURES
        float rightIndexFingerCurl = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Curl) ?? 0.0f;
        float rightIndexFingerAbduction = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Abduction) ?? 0.0f;
        float rightIndexFingerFlexion = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Flexion) ?? 0.0f;
        float rightIndexFingerOpposition = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Index, FingerFeature.Opposition) ?? 0.0f;

        float rightThumbFingerCurl = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Thumb, FingerFeature.Curl) ?? 0.0f;
        float rightThumbFingerAbduction = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Thumb, FingerFeature.Abduction) ?? 0.0f;
        // Flexion, Opposition not available on thumb

        float rightMiddleFingerCurl = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Curl) ?? 0.0f;
        float rightMiddleFingerAbduction = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Abduction) ?? 0.0f;
        float rightMiddleFingerFlexion = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Flexion) ?? 0.0f;
        float rightMiddleFingerOpposition = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Middle, FingerFeature.Opposition) ?? 0.0f;

        float rightRingFingerCurl = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Curl) ?? 0.0f;
        float rightRingFingerAbduction = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Abduction) ?? 0.0f;
        float rightRingFingerFlexion = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Flexion) ?? 0.0f;
        float rightRingFingerOpposition = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Ring, FingerFeature.Opposition) ?? 0.0f;

        float rightPinkyFingerCurl = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Curl) ?? 0.0f;
        float rightPinkyFingerFlexion = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Flexion) ?? 0.0f;
        float rightPinkyFingerOpposition = rightFingerFeatureStateProvider.GetFeatureValue(HandFinger.Pinky, FingerFeature.Opposition) ?? 0.0f;

        // TWO-HAND RELATIVE FEATURES
        float leftX = leftOVRHand.transform.position[0];
        float leftY = leftOVRHand.transform.position[1];
        float leftZ = leftOVRHand.transform.position[2];
        float rightX = rightOVRHand.transform.position[0];
        float rightY = rightOVRHand.transform.position[1];
        float rightZ = rightOVRHand.transform.position[2];
        float xDiff = rightX - leftX;
        float yDiff = rightY - leftY;
        float zDiff = rightZ - leftZ;
        float distance = Mathf.Sqrt(xDiff * xDiff + yDiff * yDiff + zDiff * zDiff);

        float leftRotationX = leftOVRHand.transform.rotation.eulerAngles[0];
        float leftRotationY = leftOVRHand.transform.rotation.eulerAngles[1];
        float leftRotationZ = leftOVRHand.transform.rotation.eulerAngles[2];
        float rightRotationX = rightOVRHand.transform.rotation.eulerAngles[0];
        float rightRotationY = rightOVRHand.transform.rotation.eulerAngles[1];
        float rightRotationZ = rightOVRHand.transform.rotation.eulerAngles[2];
        float rotationXDiff = rightRotationX - leftRotationX;
        float rotationYDiff = rightRotationY - leftRotationY;
        float rotationZDiff = rightRotationZ - leftRotationZ;
        float rotationXSin = Mathf.Sin(rotationXDiff);
        float rotationXCos = Mathf.Cos(rotationXDiff);
        float rotationYSin = Mathf.Sin(rotationYDiff);
        float rotationYCos = Mathf.Cos(rotationYDiff);
        float rotationZSin = Mathf.Sin(rotationZDiff);
        float rotationZCos = Mathf.Cos(rotationZDiff);

        float[] handData = new float[] {
            rightThumbFingerCurl, rightThumbFingerAbduction, rightIndexFingerCurl, rightIndexFingerAbduction, rightIndexFingerFlexion, rightIndexFingerOpposition,
            rightMiddleFingerCurl, rightMiddleFingerAbduction, rightMiddleFingerFlexion, rightMiddleFingerOpposition,
            rightRingFingerCurl, rightRingFingerAbduction, rightRingFingerFlexion, rightRingFingerOpposition,
            rightPinkyFingerCurl, rightPinkyFingerFlexion, rightPinkyFingerOpposition,
            leftThumbFingerCurl, leftThumbFingerAbduction, leftIndexFingerCurl, leftIndexFingerAbduction, leftIndexFingerFlexion, leftIndexFingerOpposition,
            leftMiddleFingerCurl, leftMiddleFingerAbduction, leftMiddleFingerFlexion, leftMiddleFingerOpposition,
            leftRingFingerCurl, leftRingFingerAbduction, leftRingFingerFlexion, leftRingFingerOpposition,
            leftPinkyFingerCurl, leftPinkyFingerFlexion, leftPinkyFingerOpposition,
            xDiff, yDiff, zDiff, distance,
            rotationXSin, rotationXCos, rotationYSin, rotationYCos, rotationZSin, rotationZCos
        };

        return handData;
    }
}
