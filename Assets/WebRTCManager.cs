using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.WebRTC;
using UnityEngine;

[Serializable]
public class SerializableMessage
{
    public string type;

    public string data;

    public string channel;

    public string from;
}

public class MsgFromChannel
{
    public string message;

    public string channel;

    public SerializableMessage deserializedMsg;

}

public class WebRtcEvent
{
    public string identity;
    public object data;

    public WebRtcEvent(string identity, object data)
    {
        this.identity = identity;
        this.data = data;
    }
}

public interface ITaskExecutor
{

    void Execute(Action action);

    void StartCoroutine(IEnumerator enumerator);

}

public abstract class BaseWebRtcConnection
{
    public const string TYPE_VIDEO = "video";
    public const string TYPE_AUDIO = "audio";
    public const string TYPE_DATA = "data";

    private const bool DEBUG_DATA_CHANNEL = true;

    private const string iceServerUrl = "stun:stun.l.google.com:1930";

    protected string _identity;
    protected HashSet<string> _supportedTypes;
    protected IOfferExchanger _offerExchanger;
    protected ITaskExecutor _executor;


    // webrtc related
    protected RTCPeerConnection _connection;
    private RTCDataChannel _channel;

    private RTCConfiguration GetConfiguration()
    {
        RTCConfiguration configuration = default;
        configuration.iceServers = new RTCIceServer[]
        {
            new() { urls = new string[] { iceServerUrl }}
        };
        return configuration;
    }

    private void initializeDataChannel(RTCDataChannel channel)
    {
        Debug.Log("DataChannel received");
        _channel = channel;

        Debug.Log($"OnDataChannel ({_identity}) -> {channel.Label}, Id: {channel.Id}");
        channel.OnMessage = message => _executor.Execute(() =>
        {
            var msg = Encoding.UTF8.GetString(message);
            SerializableMessage deserializedMsg = JsonConvert.DeserializeObject<SerializableMessage>(msg);
            if (DEBUG_DATA_CHANNEL)
            {
                Debug.Log($"Received message from ({_identity}) {channel.Label} : {msg}");
            }
            EventManager.Instance.Notify(EventManager.CHANNEL_MSG,
                new WebRtcEvent(_identity, new MsgFromChannel
                {
                    channel = channel.Label,
                    message = msg,
                    deserializedMsg = deserializedMsg
                }));
        });
        channel.OnClose = () => _executor.Execute(() =>
        {
            Debug.Log("The channel (" + channel.Label + ") is closed");
            _channel = null;
        });
        channel.OnError = (error) => _executor.Execute(() =>
        {
            Debug.Log($"Got an error on the channel ({_identity}) ({channel.Label}) : {error.message}");
        });
        channel.OnOpen = () => _executor.Execute(() =>
        {
            Debug.Log($"Data channel open ({_identity}): {channel.Label}");
        });
    }

    public BaseWebRtcConnection(string identity, HashSet<string> supportedTypes,
        IOfferExchanger offerExchanger, ITaskExecutor executor)
    {
        _identity = identity;
        _supportedTypes = supportedTypes;
        _offerExchanger = offerExchanger;
        _executor = executor;
    }

    public IEnumerator GetConnectionStats(Action<IDictionary<string, RTCStats>> callback)
    {
        if (null != _connection && _connection.ConnectionState != RTCPeerConnectionState.Closed)
        {
            var op = _connection.GetStats();
            yield return op;

            if (op.IsDone)
            {
                callback.Invoke(op.Value.Stats);
                op.Value.Dispose();
            }
        }
        callback.Invoke(null);
        yield return null;
    }

    public bool Send(string data)
    {
        if (null == _connection || _connection.ConnectionState != RTCPeerConnectionState.Connected) return false;

        // use fixed data channel to send data
        if (null == _channel)
        {
            _channel = _connection.CreateDataChannel("HeadsetFeedback", new RTCDataChannelInit { ordered = true, protocol = "json" });
        }
        if (_channel.ReadyState == RTCDataChannelState.Open)
        {
            if (DEBUG_DATA_CHANNEL) Debug.Log($"Send message to peer through data channel: \n{data}");
            _channel.Send(data);
            return true;
        }
        return false;
    }

    protected void _Connect()
    {
        RTCConfiguration configuration = GetConfiguration();

        _connection = new RTCPeerConnection(ref configuration);

        if (_supportedTypes.Contains(TYPE_DATA))
        {
            // create the test channel to make sure the offer contains the information to support data channels
            _channel = _connection.CreateDataChannel("HeadsetFeedback", new RTCDataChannelInit { ordered = true, protocol = "json" });

            _connection.OnDataChannel = (channel) =>
            {
                initializeDataChannel(channel);
            };
        }

        _connection.OnIceGatheringStateChange = (state) =>
        {
            Debug.Log($"OnIceGatheringStateChange ({_identity}): {state}");
        };
        _connection.OnConnectionStateChange = (state) =>
        {
            Debug.Log($"OnConnectionStateChange ({_identity}): {state}");
        };
        _connection.OnNegotiationNeeded = () =>
        {
            Debug.Log($"OnNegotiationNeeded ({_identity})");
        };
        _connection.OnIceConnectionChange = (state) =>
        {
            Debug.Log($"OnIceConnectionChange ({_identity}): {state}");
        };

        EventManager.Instance.Notify(EventManager.CONNECTION,
            new WebRtcEvent(_identity, _connection));
    }

    public void Disconnect()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _channel = null;
        _connection = null;
    }

    public abstract IEnumerator Connect();
}

public class PublicationConnection : BaseWebRtcConnection
{
    public PublicationConnection(string identity,
        HashSet<string> supportedTypes, IOfferExchanger offerExchanger,
        ITaskExecutor executor) : base(identity, supportedTypes, offerExchanger,
        executor)
    {
    }

    public override IEnumerator Connect()
    {
        _Connect();

        var createLocalOfferOps = _connection.CreateOffer();
        yield return createLocalOfferOps;

        if (createLocalOfferOps.IsError)
        {
            Debug.LogError($"Failed to create local offer for publication: \n{createLocalOfferOps.Error.message}");
            yield break;
        }

        RTCSessionDescription localOffer = new()
        {
            type = createLocalOfferOps.Desc.type,
            sdp = createLocalOfferOps.Desc.sdp
        };

        var setLocalOfferOps = _connection.SetLocalDescription(ref localOffer);
        yield return setLocalOfferOps;
        if (setLocalOfferOps.IsError)
        {
            Debug.LogError($"Failed to set local offer for publication: {setLocalOfferOps.Error.message}");
            yield break;
        }
        Debug.Log($"Set local offer successfully: \n{localOffer.sdp}");

        bool retry = true;
        TaskResult<string> result = null;
        do
        {
            result = null;
            Debug.Log("Start to exchange offers with remote server!!!!");
            yield return _offerExchanger.ExchangeOffer(localOffer.sdp, r => result = r);

            if (null == result.Result)
            {
                Debug.Log($"Exchange SDP error, retry 3 seconds later: \n{result?.Exception}");
                yield return new WaitForSeconds(3);
            }
            else
            {
                retry = false;
            }
        } while (retry);

        Debug.Log($"Start to set remote answer for publication: \n${result.Result}");
        RTCSessionDescription desc = new()
        {
            type = RTCSdpType.Answer,
            sdp = result.Result
        };
        var setRemoteAnswerOps = _connection.SetRemoteDescription(ref desc);
        yield return setRemoteAnswerOps;

        if (setRemoteAnswerOps.IsError)
        {
            Debug.LogError($"Failed to set remote answer for publication: {setRemoteAnswerOps.Error.message}");
            yield break;
        }
    }
}

public class SubscriptionConnection : BaseWebRtcConnection
{
    private object _data;
    public SubscriptionConnection(string identity,
        HashSet<string> supportedTypes, IOfferExchanger offerExchanger,
        ITaskExecutor executor, object data) : base(identity, supportedTypes, offerExchanger,
        executor)
    {
        _data = data;
    }

    public override IEnumerator Connect()
    {
        _Connect();

        Task<string> task;

        bool retry = true;

        TaskResult<string> result;
        do
        {
            result = null;
            Debug.Log("Start to exchange offers for subscription!!!");
            yield return _offerExchanger.FetchRemoteOffer(_data, r => result = r);

            if (null != result.Exception)
            {
                Debug.Log($"Exchange SDP error, retry 3 seconds later: \n{result?.Exception}");
                yield return new WaitForSeconds(3);
            }
            else
            {
                retry = false;
            }
        } while (retry);

        Debug.Log($"Fetch remote offer successfully: \n{result.Result}");

        Debug.Log("Start to set remote offer for subscription");
        RTCSessionDescription desc = new()
        {
            type = RTCSdpType.Offer,
            sdp = result.Result,
        };

        var setRemoteOfferOps = _connection.SetRemoteDescription(ref desc);
        yield return setRemoteOfferOps;

        if (setRemoteOfferOps.IsError)
        {
            Debug.LogError($"Failed to set remote offer for subscription: {setRemoteOfferOps.Error.message}");
            yield break;
        }

        var createLocalOfferOps = _connection.CreateAnswer();
        yield return createLocalOfferOps;

        if (createLocalOfferOps.IsError)
        {
            Debug.LogError($"Failed to create local answer for subscription: {createLocalOfferOps.Error.message}");
            yield break;
        }

        Debug.Log($"Start to set local answer: \n{createLocalOfferOps.Desc.sdp}");
        RTCSessionDescription answer = new()
        {
            type = createLocalOfferOps.Desc.type,
            sdp = createLocalOfferOps.Desc.sdp
        };
        var setLocalOfferOps = _connection.SetLocalDescription(ref answer);
        yield return setLocalOfferOps;
        if (setLocalOfferOps.IsError)
        {
            Debug.LogError($"Failed to set local answer for subscription: {setLocalOfferOps.Error.message}");
            yield break;
        }

        TaskResult<object> updateLocalOfferResult = null;
        yield return _offerExchanger.UpdateLocalOffer(createLocalOfferOps.Desc.sdp, r => updateLocalOfferResult = r);

        Debug.Log(null == updateLocalOfferResult?.Exception
            ? "Upload local answer to remote server successfully"
            : $"Failed to upload local answer to remote server: {result.Exception}");
    }
}

public class WebRTCManager : MonoBehaviour, ITaskExecutor, IOnEventListener
{

    public const string VIDEO_RECEIVER = "videoReceiver";

    public const string DATA_RECEIVER = "dataReceiver";

    public const string DATA_SENDER = "dataSender";

    private Dictionary<string, BaseWebRtcConnection> _connections = new();

    private Queue<Action> _mainThreadQueue = new();

    private IOfferExchanger _videoRoomOfferExchanger;

    private IOfferExchanger _streamingOfferExchanger;

    public void OnEvent(string ev, object data)
    {
        if (EventManager.CHANNEL_MSG == ev)
        {
            var evData = data as WebRtcEvent;
            if (null != evData && evData.identity == DATA_RECEIVER)
            {
                MsgFromChannel msg = evData.data as MsgFromChannel;
                if ("Ping" == msg?.deserializedMsg?.type)
                {
                    Send(msg.deserializedMsg.data, "Pong");
                }
            }
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        _videoRoomOfferExchanger = new VideoRoomOfferExchanger((e, data) =>
        {
            if (VideoRoomOfferExchanger.EVENT_VIDEO_PUBLISHER_OFFLINE == e)
            {
                if (_connections.ContainsKey(DATA_RECEIVER))
                {
                    _connections[DATA_RECEIVER]?.Disconnect();
                    _connections.Remove(DATA_RECEIVER);
                    _videoRoomOfferExchanger?.StopSubscribing();
                }
            }
            else
            {
                // online
                var whepSupportTypes = new HashSet<string>
                {
                    BaseWebRtcConnection.TYPE_DATA,
                };
                var connection = new SubscriptionConnection(DATA_RECEIVER, whepSupportTypes, _videoRoomOfferExchanger, this, data);
                _connections.Add(DATA_RECEIVER, connection);
                Execute(() => StartCoroutine(connection.Connect()));
            }
        });

        var whipSupportTypes = new HashSet<string>
        {
            BaseWebRtcConnection.TYPE_DATA,
        };
        BaseWebRtcConnection connection = new PublicationConnection(DATA_SENDER, whipSupportTypes, _videoRoomOfferExchanger, this);
        _connections.Add(DATA_SENDER, connection);

        _streamingOfferExchanger = new StreamingOfferExchanger();
        connection = new SubscriptionConnection(VIDEO_RECEIVER, new HashSet<string>
        {
            BaseWebRtcConnection.TYPE_VIDEO,
            BaseWebRtcConnection.TYPE_AUDIO
        }, _streamingOfferExchanger, this, null);
        _connections.Add(VIDEO_RECEIVER, connection);

        // WebRTC.ConfigureNativeLogging(true, NativeLoggingSeverity.Error);
        StartCoroutine(WebRTC.Update());
        foreach (var conn in _connections)
        {
            StartCoroutine(conn.Value.Connect());
        }

        EventManager.Instance.Observe(EventManager.CHANNEL_MSG, this);
    }

    public void Send(string data, string type)
    {
        if (!_connections.ContainsKey(DATA_SENDER)) return;


        var channel = "HeadsetPosFeedBack";
        _connections[DATA_SENDER]?.Send(
            $"{{\"data\": \"{data}\", \"channel\": \"{channel}\", \"type\": \"{type}\", \"from\": \"Headset\"}}");
    }

    public void GetConnectionStats(Action<IDictionary<string, RTCStats>> callback)
    {
        if (_connections.ContainsKey(VIDEO_RECEIVER))
        {
            StartCoroutine(_connections[VIDEO_RECEIVER].GetConnectionStats(callback));
        }
        else
        {
            callback.Invoke(null);
        }
    }

    public new void StartCoroutine(IEnumerator enumerator)
    {
        base.StartCoroutine(enumerator);
    }

    public void Execute(Action action)
    {
        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(action);
        }
    }

    // Update is called once per frame
    void Update()
    {
        while (_mainThreadQueue.Count > 0)
        {
            Action action;
            lock (_mainThreadQueue)
            {
                action = _mainThreadQueue.Dequeue();
            }
            action?.Invoke();
        }
    }

    async void OnDestroy()
    {
        EventManager.Instance.UnObserve(EventManager.CHANNEL_MSG, this);
        foreach (var connection in _connections)
        {
            connection.Value.Disconnect();
        }
        await Task.Run(() =>
        {
            _videoRoomOfferExchanger?.Destroy();
            _streamingOfferExchanger?.Destroy();
        });
    }
}
