using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using Object = System.Object;

public class OperationDetector : MonoBehaviour, IOnEventListener
{


    [SerializeField]
    public XRInputValueReader<Vector2> m_LeftThumbStickReader = new XRInputValueReader<Vector2>("Thumbstick");

    [SerializeField]
    public XRInputValueReader<Vector2> m_RightThumbStickReader = new XRInputValueReader<Vector2>("Thumbstick");

    private ISchedulableAction<OperationDetector> _schedulableAction;

    private ISchedulableAction<OperationDetector> _uiAction = null;

    private long logTime = 0L;

    private void Awake()
    {
        // while only subscribing the video, disable this script 
        gameObject.GetComponent<OperationDetector>().enabled = !ConfigManager.ONLY_VIDEO;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var lastPosition = transform.position;
        var initialRotation = transform.rotation.eulerAngles;

        Debug.Log("Initial Position: " + lastPosition + ", Initial Rotation: " + initialRotation);
        EventManager.Instance.Observe(EventManager.CHANNEL_MSG, this);
    }

    private void Update()
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - logTime >= 0.5)
        {
            var rotation = transform.rotation.eulerAngles;
            var position = transform.position;
            Debug.Log($"Current rotation angles are x: {rotation.x}, y: {rotation.y}, z: {rotation.z}");
            Debug.Log($"Current position are x: {position.x}, y: {position.y}, z: {position.z}");
            if (!ConfigManager.SAMPLE_IN_COROUTINE)
            {
                _uiAction?.OnAction();
            }
            logTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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

        SerializableMessage msg = ((arg as WebRtcEvent)?.data as MsgFromChannel)?.deserializedMsg;

        if (null == msg || "Control" != msg.type) return;

        if ("Start" == msg.data)
        {
            Debug.Log("Received start control command from the drone");
            // hint the user to get ready
            EventManager.Instance.Notify(EventManager.GUIDE_INFO,
                "Get ready. Please stay still and don't move the thumb sticks on the controllers");
            _schedulableAction?.Stop();

            _schedulableAction = new SimpleDelayAction<OperationDetector>(this, ConfigManager.PERIOD_FOR_USER_TO_PREPARE, () =>
            {
                EventManager.Instance.Notify(EventManager.GUIDE_INFO, "Go");

                var tmp = new MovementDetectorAction(this, ConfigManager.SAMPLING_INTERVAL_IN_MILLISECONDS);
                if (!_schedulableAction.IsFinished())
                {
                    if (ConfigManager.SAMPLE_IN_COROUTINE)
                    {
                        StartCoroutine(tmp.Start());
                        _schedulableAction?.Stop();
                        _schedulableAction = tmp;
                    }
                    else
                    {
                        _uiAction = tmp;
                        _schedulableAction?.Stop();
                        _schedulableAction = null;
                    }
                }
            });
            StartCoroutine(_schedulableAction.Start());
        }
        else if ("Stop" == msg.data)
        {
            _schedulableAction?.Stop();
            _schedulableAction = null;
            
            _uiAction?.Stop();
            _uiAction = null;
            
            EventManager.Instance.Notify(EventManager.GUIDE_INFO, "Control abort");
        }
    }
}

class ControlStatusData
{
    private readonly Vector3 _benchmarkPosition;
    private readonly Vector3 _benchmarkRotation;

    private Vector3 _lastPosition;
    private Vector3 _lastRotation;

    private Vector3 _currentPosition;
    private Vector3 _currentRotation;

    private Vector2 _leftThumbStickValue;
    private Vector2 _rightThumbStickValue;

    private long _sampleTimestamp = 0;
    
    private readonly long _benchmarkSampleTimestamp = 0;
    
    private float _rotationVelocity;
    private float _smoothHeadRotationY = 0f;

    private float _xVelocity;
    private float _smoothX;

    private float _yVelocity;
    private float _smoothY;

    private float _zVelocity;
    private float _smoothZ;

    public ControlStatusData(Vector3 benchmarkPosition, Vector3 benchmarkRotation)
    {
        this._benchmarkPosition = benchmarkPosition;
        this._benchmarkRotation = benchmarkRotation;
        _benchmarkSampleTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _sampleTimestamp = _benchmarkSampleTimestamp;

        _currentPosition = _lastPosition = benchmarkPosition;
        _currentRotation = _lastRotation = benchmarkRotation;
        
        _rotationVelocity = 0f;
        _smoothHeadRotationY = 0f;
    }

    public void UpdateLocationAndRotation(Vector3 position, Vector3 rotation)
    {
        _lastPosition = _currentPosition;
        _lastRotation = _currentRotation;

        _currentPosition = position;
        _currentRotation = rotation;

        _smoothHeadRotationY += SmoothDataChange(_smoothHeadRotationY, _currentRotation.y - _benchmarkRotation.y, ref _rotationVelocity, true);
        _currentRotation.y = (_smoothHeadRotationY + _benchmarkRotation.y + 360) % 360f;
        
        _smoothX += SmoothDataChange(_smoothX, _currentPosition.x - _benchmarkPosition.x, ref _xVelocity, false);
        _currentPosition.x = _smoothX;

        _smoothY += SmoothDataChange(_smoothY, _currentPosition.y - _benchmarkPosition.y, ref _yVelocity, false);
        _currentPosition.y = _smoothY;

        _smoothZ += SmoothDataChange(_smoothZ, _currentPosition.z - _benchmarkPosition.z, ref _zVelocity, false);
        _currentPosition.z = _smoothZ;

        _sampleTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void UpdateThumbStickValues(Vector2 leftThumbStickValue, Vector2 rightThumbStickValue)
    {
        _leftThumbStickValue = leftThumbStickValue;
        _rightThumbStickValue = rightThumbStickValue;

        _sampleTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private Object VectorToDict(Object vector)
    {
        if (vector is Vector3 tmp)
        {
            return new Dictionary<string, object>
            {
                {
                    "x", tmp.x
                },
                {
                    "y", tmp.y
                },
                {
                    "z", tmp.z
                }
            };
        }
        else if (vector is Vector2 t)
        {
            return new Dictionary<string, object>
            {
                {
                    "x", t.x
                },
                {
                    "y", t.y
                }
            };
        }

        return null;
    }

    public Object ToDictionary()
    {
        return new Dictionary<string, object>
        {
            {"benchmarkPosition", VectorToDict(_benchmarkPosition)},
            {"benchmarkRotation", VectorToDict(_benchmarkRotation)},
            {"lastPosition", VectorToDict(_lastPosition)},
            {"lastRotation", VectorToDict(_lastRotation)},
            {"currentPosition", VectorToDict(_currentPosition)},
            {"currentRotation", VectorToDict(_currentRotation)},
            {"sampleTimestamp", _sampleTimestamp},
            {"benchmarkSampleTimestamp", _benchmarkSampleTimestamp},
            {"leftThumbStickValue", VectorToDict(_leftThumbStickValue)},
            {"rightThumbStickValue", VectorToDict(_rightThumbStickValue)}
        };
    }

    private float SmoothDataChange(float currentValue, float targetValue, ref float velocity, bool isAngle)
    {
        if (isAngle)
        {
            float delta = Mathf.DeltaAngle(currentValue, targetValue);
            return Mathf.SmoothDampAngle(0, delta, ref velocity, 0.01f);
        }
        else
        {
            float delta = targetValue - currentValue;
            return Mathf.SmoothDamp(0, delta, ref velocity, 0.01f);
        }
    }
}

class MovementDetectorAction : PeriodicalAction<OperationDetector>
{
    private readonly WebRtcManager _webRtcManager;

    private ControlStatusData _statusData;


    public MovementDetectorAction(OperationDetector host, long interval) : base(host, interval)
    {
        _webRtcManager = host.GetComponentInParent<WebRtcManager>();
    }

    private void InitializeBenchmark()
    {
        // TODO right here, show I use the position or localPosition???
        _statusData = new ControlStatusData(Host.gameObject.transform.position, Host.gameObject.transform.eulerAngles);
    }

    public override void OnAction()
    {
        if (null == _statusData)
        {
            InitializeBenchmark();
        }
        else
        {
            _statusData.UpdateLocationAndRotation(Host.gameObject.transform.position,
                Host.gameObject.transform.eulerAngles);
            _statusData.UpdateThumbStickValues(Host.m_LeftThumbStickReader.ReadValue(),
                Host.m_RightThumbStickReader.ReadValue());
        }

        _webRtcManager.Send(JsonConvert.SerializeObject(_statusData.ToDictionary()), "ControlStatus");
    }
}