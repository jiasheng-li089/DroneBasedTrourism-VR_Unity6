using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

public class OperationDetector : MonoBehaviour, IOnEventListener
{
    private Vector3 lastPosition;
    private Vector3 initialRoation;

    private WebRtcManager webRTCManager;

    private long lastSampleTime = 0;

    private long captureInterval = 200L;

    [SerializeField]
    public XRInputValueReader<Vector2> m_LeftThumbStickReader = new XRInputValueReader<Vector2>("Thumbstick");

    [SerializeField]
    public XRInputValueReader<Vector2> m_RightThumbStickReader = new XRInputValueReader<Vector2>("Thumbstick");

    private ISchedulableAction<OperationDetector> _schedulableAction;

    private void Awake()
    {
        // while only subscribing the video, disable this script 
        gameObject.GetComponent<OperationDetector>().enabled = !ConfigManager.ONLY_VIDEO;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        lastPosition = transform.position;
        initialRoation = transform.rotation.eulerAngles;

        Debug.Log("Initial Position: " + lastPosition + ", Initial Rotation: " + initialRoation);
        webRTCManager = GetComponentInParent<WebRtcManager>();
        _schedulableAction = new MovementDetectorAction(this, 100);

        EventManager.Instance.Observe(EventManager.CHANNEL_MSG, this);
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 currentPosition = transform.position;
        Vector3 currentRotation = transform.rotation.eulerAngles;

        // Debug.Log("Current Position: " + currentPosition + ", Current Rotation: " + currentRotation);

        // TODO: calculate the difference between the last and current position and rotation,
        // generate commands to control the drone based on the difference,
        // and send the commands to the drone
        if (null == webRTCManager) return;

        long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (currentTimestamp - lastSampleTime >= captureInterval)
        {
            lastSampleTime = currentTimestamp;
            // webRTCManager.Send("Ping " + currentTimestamp, "data");
        }

        Vector2 leftInput = m_LeftThumbStickReader.ReadValue();
        Vector2 rightInput = m_RightThumbStickReader.ReadValue();

        // limit the frequency to read the data

        if (leftInput != Vector2.zero || rightInput != Vector2.zero)
        {
            Debug.Log($"Left input: ({leftInput.x}, {leftInput.y})\t\tRight input: ({rightInput.x}, {rightInput.y})");
        }
    }

    private void OnDestroy()
    {
        _schedulableAction?.Stop();
        EventManager.Instance.UnObserve(EventManager.CHANNEL_MSG, this);
    }

    public void OnEvent(string ev, object arg)
    {
        if (EventManager.CHANNEL_MSG != ev) return;

        SerializableMessage msg = (arg as MsgFromChannel)?.deserializedMsg;

        if (null == msg || "Control" != msg.type) return;

        if ("Start" == msg.data)
        {
            Debug.Log("Received start control command from the drone");
            // hint the user to get ready
            EventManager.Instance.Notify(EventManager.GUIDE_INFO,
                "Get ready. Please stay still and don't move the thumb sticks on the controllers");
            _schedulableAction?.Stop();

            _schedulableAction = new SimpleDelayAction<OperationDetector>(this, 1000L, () =>
            {
                EventManager.Instance.Notify(EventManager.GUIDE_INFO, "Go");
                _schedulableAction?.Stop();

                _schedulableAction = new MovementDetectorAction(this, 100);
                StartCoroutine(_schedulableAction.Start());
            });
            StartCoroutine(_schedulableAction.Start());
        }
        else if ("Stop" == msg.data)
        {
            _schedulableAction?.Stop();
            _schedulableAction = null;
            EventManager.Instance.Notify(EventManager.GUIDE_INFO, "Control abort");
        }
    }
}

class MovementDetectorAction : PeriodicalAction<OperationDetector>
{
    private readonly WebRtcManager _webRtcManager;

    private Vector3? _lastPosition = null;
    private Vector3? _lastRotation = null;
    
    public MovementDetectorAction(OperationDetector host, long interval) : base(host, interval)
    {
        _webRtcManager = host.GetComponentInParent<WebRtcManager>();
    }

    private void InitializeBenchmark()
    {
        _lastPosition = Host.gameObject.transform.position;
        _lastRotation = Host.gameObject.transform.rotation.eulerAngles;
    }

    public override void OnAction()
    {
        if (null == _lastPosition)
        {
            InitializeBenchmark();
            
            // initialize the benchmark
        }
        
        // read left thumb stick
        Vector2 leftThumbStickValue = Host.m_LeftThumbStickReader.ReadValue();
        Vector2 rightThumbStickValue = Host.m_RightThumbStickReader.ReadValue();
        
        _webRtcManager.Send("", "ControlStatus");
    }
}