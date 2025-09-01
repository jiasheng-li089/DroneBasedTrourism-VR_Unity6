using UnityEngine;
using System;
using UnityEngine.UIElements;
using Unity.WebRTC;
using System.Collections.Generic;
using UnityEngine.UI;

public class DebugInformationRender : MonoBehaviour, IOnEventListener
{
    private Text _videoLatency;
    private Text _renderingFrequency;
    private Text _dataLatency;

    private long _lastTimeReceivedPong = 0L;

    private Text _trackFrequency;

    private Text _frameRate;

    private Text _controlFrequency;

    private long _lastUpdateTimestamp = 0L;

    private long _lastTrackTimestamp = 0L;

    private const long REFRESH_INTERVAL = 1000L;

    private WebRtcManager _webRtcManager;

    private void Awake()
    {
        gameObject.SetActive(ConfigManager.DEBUG);
    }

    // Start is called before the first frame update
    void Start()
    {
        _webRtcManager = GetComponentInParent<WebRtcManager>();

        _videoLatency = transform.Find("video_latency").GetComponent<Text>();
        _dataLatency = transform.Find("data_latency").GetComponent<Text>();
        _frameRate = transform.Find("video_frame_rate").GetComponent<Text>();
        _renderingFrequency = transform.Find("rendering_frequency").GetComponent<Text>();
        _trackFrequency = transform.Find("tracking_frequency").GetComponent<Text>();
        _controlFrequency = transform.Find("control_frequency").GetComponent<Text>();

        var baseLineText = _videoLatency;
        var textList = new[]
        {
            _dataLatency,
            _frameRate,
            _renderingFrequency,
            _trackFrequency,
            _controlFrequency,
        };

        foreach (var tmpText in textList)
        {
            var baseLinePosition = baseLineText.transform.localPosition;
            tmpText.transform.localPosition = new Vector3(baseLinePosition.x,
                baseLinePosition.y - baseLineText.GetComponent<RectTransform>().rect.height,
                baseLinePosition.z);
            baseLineText = tmpText;
        }

        if (null != _dataLatency)
        {
            Debug.Log($"Data latency label: {_dataLatency}");
        }

        EventManager.Instance.Observe(EventManager.CHANNEL_MSG, this);
    }

    // Update is called once per frame
    void Update()
    {
        long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (currentTimestamp - _lastUpdateTimestamp >= REFRESH_INTERVAL && _renderingFrequency)
        {
            _renderingFrequency.text = $"Rendering Frequency: {(int)(1.0f / Time.smoothDeltaTime)}";
            _lastUpdateTimestamp = currentTimestamp;

            SendPingToTestDataLatency();

            UpdateVideoFrameRate();

            long trackDelta = currentTimestamp - _lastTrackTimestamp;
            _trackFrequency.text = $"Tracking Frequency: {(int)(1000f / trackDelta)}";
        }

        _lastTrackTimestamp = currentTimestamp;
    }


    void OnDestroy()
    {
        EventManager.Instance.UnObserve(EventManager.CHANNEL_MSG, this);
    }

    public void OnEvent(string ev, object msg)
    {
        if (EventManager.CHANNEL_MSG == ev)
        {
            WebRtcEvent webRtcEvent = msg as WebRtcEvent;
            MsgFromChannel message = webRtcEvent.data as MsgFromChannel;
            if (webRtcEvent.identity == WebRtcManager.DATA_RECEIVER)
            {
                SerializableMessage serializableMessage = message.deserializedMsg;
                if ("Pong" == serializableMessage.type)
                {
                    long timestamp = Convert.ToInt64(serializableMessage.data);
                    if (timestamp > _lastTimeReceivedPong)
                    {
                        _lastTimeReceivedPong = timestamp;
                        long dataLatency = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
                        _dataLatency.text = $"Data Latency: {dataLatency / 2}";
                    }
                }
            }
        }
    }

    private void SendPingToTestDataLatency()
    {
        _webRtcManager.Send($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", "Ping");
    }

    private Action<IDictionary<string, RTCStats>> _reportCallBack = null;

    private void UpdateVideoFrameRate()
    {
        if (null == _reportCallBack)
        {
            _reportCallBack = (values) =>
            {
                if (null == values) return;
                foreach (var stat in values)
                {
                    if (stat.Value.Type == RTCStatsType.InboundRtp && stat.Value is RTCInboundRTPStreamStats inbound)
                    {
                        _frameRate.text = $"Video Frame Rate: {inbound.framesPerSecond}";
                    }
                }
            };
        }

        _webRtcManager.GetConnectionStats(_reportCallBack);
    }
}