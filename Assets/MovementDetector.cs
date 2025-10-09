using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using Object = System.Object;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;


public class OperationDetector : MonoBehaviour, IOnEventListener
{


    [SerializeField]
    public XRInputValueReader<Vector2> m_LeftThumbStickReader;

    [SerializeField]
    public XRInputValueReader<Vector2> m_RightThumbStickReader;

    private ISchedulableAction<OperationDetector> _schedulableAction;

    private ISchedulableAction<OperationDetector> _uiAction = null;

    [SerializeField]
    public Text _infoTxt;

    [SerializeField]
    public Text _debugTxt;

    private long logTime = 0L;
    
    private static string TAG = "OperationDetector";

    private Vector3? _benchmarkRotation = null;
    private Vector3? _benchmarkPosition = null;

    private void Awake()
    {
        // while only subscribing the video, disable this script 
        gameObject.GetComponent<OperationDetector>().enabled = !ConfigManager.ONLY_VIDEO;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        EventManager.Instance.Observe(EventManager.CHANNEL_MSG, this);

        if (ConfigManager.MOCK)
        {
            StartTrackingAndSendData();
        }
    }

    public void Update()
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - logTime >= 0.5)
        {
            var rotation = transform.rotation.eulerAngles;
            var position = transform.position;

            if (null == _benchmarkRotation)
            {
                _benchmarkRotation = rotation;
                _benchmarkPosition = position;
                return;
            }
            Vector3 benchmarkPosition = (Vector3)_benchmarkPosition;
            Vector3 benchmarkRotation = (Vector3)_benchmarkRotation; 
            
            float benchmarkAngle = (360 - benchmarkRotation.y) % 360 * Mathf.Deg2Rad;
            float xOffset = position.x - benchmarkPosition.x;
            float zOffset = position.z - benchmarkPosition.z;
        
            float x = xOffset * Mathf.Cos(benchmarkAngle) - zOffset * Mathf.Sin(benchmarkAngle);
            float z = xOffset * Mathf.Sin(benchmarkAngle) + zOffset * Mathf.Cos(benchmarkAngle);
            if (benchmarkRotation.y is >= 45 and <= 135 or >= 225 and <= 315)
            {
                x = -x;
                z = -z;
            }
            Vector3 newPosition = new Vector3(x, position.y, z);
            Vector3 newRotation = new Vector3(rotation.x, (rotation.y - benchmarkRotation.y + 360) % 360, rotation.z); 
            
            if (null == _schedulableAction && ConfigManager.DEBUG)
            {
                UpdatePositionAndRotationInfo(newPosition, newRotation);
                UpdateRealPositionAndRotationInfo(position, rotation, m_LeftThumbStickReader.ReadValue(), m_RightThumbStickReader.ReadValue());
            }
            if (!ConfigManager.SAMPLE_IN_COROUTINE)
            {
                _uiAction?.OnAction();
            }
            logTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    public void UpdatePositionAndRotationInfo(Vector3 position, Vector3 rotation)
    {
        _infoTxt.text = $"P({position.x:F2}, {position.z:F2})\tR({rotation.y:F2})";
    }

    public void UpdateRealPositionAndRotationInfo(Vector3 position, Vector3 rotation,
        Vector2 leftThumbStickValue, Vector2 rightThumbStickValue)
    {
        _debugTxt.text = $"P({position.x:F2}, {position.z:F2}) R({rotation.y:F2}) L({leftThumbStickValue.x:F2}, {leftThumbStickValue.y:F2}), R({rightThumbStickValue.x:F2}, {rightThumbStickValue.y:F2})";
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
            StartTrackingAndSendData();
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

    private void StartTrackingAndSendData()
    {
        Debug.Log("Received start control command from the drone");
        // hint the user to get ready
        EventManager.Instance.Notify(EventManager.GUIDE_INFO,
            "Get ready. Please stay still and don't move the thumb sticks on the controllers");
        _schedulableAction?.Stop();

        _schedulableAction = new SimpleDelayAction<OperationDetector>(this, ConfigManager.PERIOD_FOR_USER_TO_PREPARE, () =>
        {
            // show tips telling user ready to go
            EventManager.Instance.Notify(EventManager.GUIDE_INFO, "Go");

            StartCoroutine(new SimpleDelayAction<OperationDetector>(this, ConfigManager.PERIOD_FOR_USER_TO_PREPARE + 2000, () =>
            {
                // hide the tips
                EventManager.Instance.Notify(EventManager.GUIDE_INFO, "");
            }).Start());
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
}

class ControlStatusData
{
    // these two fields reflect the real position and orientation of the headset for benchmark
    private Vector3? _benchmarkPosition;
    private Vector3? _benchmarkRotation;

    // these four fields are relative to the benchmark
    private Vector3 _lastPosition;
    private Vector3 _lastRotation;

    private Vector3 _currentPosition;
    private Vector3 _currentRotation;

    private Vector2 _leftThumbStickValue;
    private Vector2 _rightThumbStickValue;

    private long _sampleTimestamp = 0;
    
    private long _benchmarkSampleTimestamp = 0;
    
    // the following fields are only used for smoothing calculation, won't be serialized to the json
    private float _rotationVelocity;
    private float _smoothHeadRotationY = 0f;
    private float _xVelocity;
    private float _smoothX;
    private float _yVelocity;
    private float _smoothY;
    private float _zVelocity;
    private float _smoothZ;

    private static string TAG = "ControlStatusData";

    public void UpdateLocationAndRotation(Vector3 position, Vector3 rotation)
    {
        if (null == _benchmarkRotation || null == _benchmarkPosition)
        {
            _benchmarkRotation = rotation;
            _benchmarkPosition = position;

            _benchmarkSampleTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _sampleTimestamp = _benchmarkSampleTimestamp;

            _currentPosition = new Vector3();
            _currentRotation = new Vector3();
            _lastPosition = _currentPosition;
            _lastRotation = _currentRotation;
            
            return;
        }

        
        Vector3 benchmarkPosition = (Vector3)_benchmarkPosition;
        Vector3 benchmarkRotation = (Vector3)_benchmarkRotation; 
            
        float benchmarkAngle = (360 - benchmarkRotation.y) % 360 * Mathf.Deg2Rad;
        float xOffset = position.x - benchmarkPosition.x;
        float zOffset = position.z - benchmarkPosition.z;
        
        float x = xOffset * Mathf.Cos(benchmarkAngle) - zOffset * Mathf.Sin(benchmarkAngle);
        float z = xOffset * Mathf.Sin(benchmarkAngle) + zOffset * Mathf.Cos(benchmarkAngle);
        if (benchmarkRotation.y is >= 45 and <= 135 or >= 225 and <= 315)
        {
            x = -x;
            z = -z;
        }

        Vector3 newPosition = new Vector3(x, position.y, z);
        Vector3 newRotation = new Vector3(rotation.x, (rotation.y - benchmarkRotation.y + 360) % 360, rotation.z);
        
        SmoothMotion(newPosition, newRotation);
    }

    public Vector3 GetCurrentPosition()
    {
        return _currentPosition;
    }

    public Vector3 GetCurrentRotation()
    {
        return _currentRotation;
    }

    /**
     * Smooth the motion, the position and the rotation are relative ones, no need to minus benchmark anymore
     */
    private void SmoothMotion(Vector3 position, Vector3 rotation)
    {
        _lastPosition = _currentPosition;
        _lastRotation = _currentRotation;

        _currentPosition = position;
        _currentRotation = rotation;

        _smoothHeadRotationY += SmoothDataChange(_smoothHeadRotationY, _currentRotation.y, ref _rotationVelocity, true);
        _currentRotation.y = (_smoothHeadRotationY + 360) % 360f;
        
        _smoothX += SmoothDataChange(_smoothX, _currentPosition.x, ref _xVelocity, false);
        _currentPosition.x = _smoothX;

        // _smoothY += SmoothDataChange(_smoothY, _currentPosition.y, ref _yVelocity, false, 0.0001f);
        // _currentPosition.y = _smoothY;

        _smoothZ += SmoothDataChange(_smoothZ, _currentPosition.z, ref _zVelocity, false, 0.0001f);
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
            {"benchmarkPosition", VectorToDict(new Vector3())},
            {"benchmarkRotation", VectorToDict(new Vector3())},
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

    private float SmoothDataChange(float currentValue, float targetValue, ref float velocity, bool isAngle, float smoothTime = 0.01f)
    {
        if (isAngle)
        {
            float delta = Mathf.DeltaAngle(currentValue, targetValue);
            return Mathf.SmoothDampAngle(0, delta, ref velocity, smoothTime);
        }
        else
        {
            float delta = targetValue - currentValue;
            return Mathf.SmoothDamp(0, delta, ref velocity, smoothTime);
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
        _statusData = new ControlStatusData();
        _statusData.UpdateLocationAndRotation(Host.gameObject.transform.position, Host.gameObject.transform.eulerAngles);
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

        string serialMsg = JsonConvert.SerializeObject(_statusData.ToDictionary());
        _webRtcManager.Send(serialMsg, "ControlStatus");
        if (ConfigManager.DEBUG)
        {
            Host.UpdateRealPositionAndRotationInfo(_statusData.GetCurrentPosition(),
                _statusData.GetCurrentRotation(), Host.m_LeftThumbStickReader.ReadValue(),
                Host.m_RightThumbStickReader.ReadValue());
        }
    }
}