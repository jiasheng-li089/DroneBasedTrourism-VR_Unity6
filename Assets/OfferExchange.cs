using System;
using System.Threading.Tasks;
using System.Text;
using UnityEngine;

using NativeWebSocket;
using System.Collections.Generic;
using System.Collections;
using Newtonsoft.Json;

public class SimpleJanusResp
{
    public string janus;

    public Dictionary<string, object> data;

}

public class PluginData
{
    public string plugin;

    public Dictionary<string, object> data;
}

public class Error
{
    public int code;

    public string reason;
}

public class JSEPData
{
    public string type;

    public string sdp;
}

public class JanusPluginResponse
{
    public string janus;

    public string transaction;

    public long session_id;

    public long sender;

    public PluginData plugindata;

    public Error error;

    public JSEPData jsep;
}

public class TaskResult<T>
{
    public T Result;

    public Exception Exception;
}

public class EndPoint
{
    public string SessionId = null;

    public string HandleId = null;

    public bool IsValid()
    {
        return !string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(HandleId);
    }
}


public interface IOfferExchanger
{
    IEnumerator ExchangeOffer(string localOffer, Action<TaskResult<string>> callBack);

    IEnumerator FetchRemoteOffer(object data, Action<TaskResult<string>> callBack);

    IEnumerator UpdateLocalOffer(string localAnswer, Action<TaskResult<object>> callBack);

    void StopPublishing();

    void StopSubscribing();

    void Destroy();
}

public class VideoRoomOfferExchanger : IOfferExchanger
{

    private const int ROOM_ID = 1234;

    protected const string ROOM_PIN = "adminpwd";

    private const long DATA_PUBLISHER_ID = 987654321;

    private const string DATA_PUBLISHER_DISPLAY = "Headset";

    private const long VIDEO_PUBLISHER_ID = 123456789;

    private const string KEY_SESSION_ID = "session_id";

    private const string KEY_HANDLE_ID = "handle_id";

    private const string KEY_TRANSACTION_ID = "transaction";

    private const string SUCCESS = "success";

    private const string KEY_ID = "id";

    private const string KEY_JANUS = "janus";
    private const string KEY_PLUGIN = "plugin";
    protected const string VIDEO_ROOM_PLUGIN = "janus.plugin.videoroom";

    private const string ACTION_EVENT = "event";

    private const string WEBSOCKET_URL = "ws://10.112.53.217:8188";

    private const bool DEBUG_WEBSOCKET_MSG = false;

    public const string EVENT_VIDEO_PUBLISHER_ONLINE = "video_publisher_online";

    public const string EVENT_VIDEO_PUBLISHER_OFFLINE = "video_publisher_offline";

    private Action<string, object> _statusCallBack;

    protected WebSocket _webSocket;

    private EndPoint _publicationEndPoint = new();

    protected EndPoint _subscriptionEndPoint = new();

    private Dictionary<string, TaskResult<string>> _waitingJobs = new();

    public VideoRoomOfferExchanger(Action<string, object> statusCallback)
    {
        _statusCallBack = statusCallback;
    }

    protected string _CreateNewTransactionId()
    {
        return Guid.NewGuid().ToString();
    }

    protected void _AttachIdsToReq(Dictionary<string, object> req, string transactionId, bool forPublication)
    {
        if (!req.ContainsKey(KEY_JANUS)) req[KEY_JANUS] = "message";

        EndPoint endPoint = forPublication ? _publicationEndPoint : _subscriptionEndPoint;

        if (!string.IsNullOrEmpty(endPoint.SessionId) && !req.ContainsKey(KEY_SESSION_ID))
            req[KEY_SESSION_ID] = long.Parse(endPoint.SessionId);

        if (!string.IsNullOrEmpty(endPoint.HandleId) && !req.ContainsKey(KEY_HANDLE_ID))
            req[KEY_HANDLE_ID] = long.Parse(endPoint.HandleId);

        if (!req.ContainsKey(KEY_TRANSACTION_ID))
            req[KEY_TRANSACTION_ID] = transactionId;
    }

    protected string _GetErrorFromResp(string json)
    {
        var resp = JsonConvert.DeserializeObject<JanusPluginResponse>(json);

        string error = resp?.plugindata?.data?.GetValueOrDefault("error", null)?.ToString();
        if (!string.IsNullOrEmpty(error))
        {
            return error;
        }

        return resp?.plugindata?.data?.GetValueOrDefault("reason", null)?.ToString();
    }

    protected Task _SendMessageToServer(string transactionId, object req)
    {
        _waitingJobs[transactionId] = null;

        var text = JsonConvert.SerializeObject(req, Formatting.Indented);
        if (DEBUG_WEBSOCKET_MSG) Debug.Log($"Send message to websocket server: \n {text}");
        Task task = _webSocket?.SendText(text);
        return task;
    }

    protected async Task _MakeSureWebSocket()
    {
        if (null == _webSocket)
        {
            _webSocket = new WebSocket(WEBSOCKET_URL, "janus-protocol");

            _webSocket.OnOpen += () =>
            {
                Debug.Log("Connection open!");
                var task = Task.Run(() => keepAliveTask());
#if !UNITY_WEBGL || UNITY_EDITOR
                task = Task.Run(() => _DispatchMessage());
#endif
            };
            _webSocket.OnError += (e) =>
            {
                Debug.Log($"Got an error from websocket: {e}");
            };
            _webSocket.OnClose += (e) =>
            {
                Debug.Log($"Websocket is closed: {e}");
            };

            _webSocket.OnMessage += (data) =>
            {
                var msg = Encoding.UTF8.GetString(data);
                if (DEBUG_WEBSOCKET_MSG) Debug.Log($"Received message from the web socket: \n{msg}");

                _HandleMessage(msg);
            };
            var task = Task.Run(async () =>
            {
                Debug.Log("Start to connect to server through websocket.");
                await _webSocket.Connect();
                Debug.Log("Connect to server through websocket finished.");
            });
            await task;
        }
    }

    private async void _DispatchMessage()
    {
        while (_webSocket.State != WebSocketState.Closed)
        {
            _webSocket.DispatchMessageQueue();
            await Task.Delay(DEBUG_WEBSOCKET_MSG ? 100 : 1);
        }
    }


    private async void keepAliveTask()
    {
        Debug.Log("Start the task to keep websocket alive.");
        while (_webSocket.State != WebSocketState.Closed)
        {
            await Task.Delay(5000);

            var keepAliveReq = new Dictionary<string, object>();
            keepAliveReq["janus"] = "keepalive";

            if (!string.IsNullOrEmpty(_publicationEndPoint.SessionId))
            {
                keepAliveReq["transaction"] = _CreateNewTransactionId();
                keepAliveReq[KEY_SESSION_ID] = long.Parse(_publicationEndPoint.SessionId);

                var keepAliveText = JsonConvert.SerializeObject(keepAliveReq, Formatting.Indented);
                if (DEBUG_WEBSOCKET_MSG) Debug.Log($"Sending keep alive message: \n{keepAliveText}");
                await _webSocket.SendText(keepAliveText);
            }

            if (!string.IsNullOrEmpty(_subscriptionEndPoint.SessionId))
            {
                keepAliveReq["transaction"] = _CreateNewTransactionId();
                keepAliveReq[KEY_SESSION_ID] = long.Parse(_subscriptionEndPoint.SessionId);

                var keepAliveText = JsonConvert.SerializeObject(keepAliveReq, Formatting.Indented);
                if (DEBUG_WEBSOCKET_MSG) Debug.Log($"Sending keep alive message: \n{keepAliveText}");
                await _webSocket.SendText(keepAliveText);
            }
        }
    }

    protected WaitUntil _Reply(string transactionId)
    {
        return new WaitUntil(() => null != _waitingJobs[transactionId]);
    }

    protected TaskResult<string> _GetResponse(string transactionId)
    {
        var result = _waitingJobs[transactionId];
        _waitingJobs.Remove(transactionId);
        return result;
    }

    private void _HandleMessage(string msg)
    {
        var result = FromJson(msg);

        string action = result.GetValueOrDefault(KEY_JANUS, null)?.ToString();
        if ("ack" == action) return;

        if (!string.IsNullOrEmpty(result.GetValueOrDefault(KEY_TRANSACTION_ID, null)?.ToString()))
        {
            string transaction = result[KEY_TRANSACTION_ID].ToString();
            TaskResult<string> taskResult = new() { Result = msg };
            _waitingJobs[transaction] = taskResult;
        }


        if (ACTION_EVENT == action)
        {
            var pluginResp = JsonConvert.DeserializeObject<JanusPluginResponse>(msg);
            var data = pluginResp?.plugindata?.data;
            try
            {
                if (VIDEO_ROOM_PLUGIN == pluginResp?.plugindata?.plugin
                        && ACTION_EVENT == data?.GetValueOrDefault("videoroom", null)?.ToString()
                        && ROOM_ID == int.Parse(data?.GetValueOrDefault("room", null)?.ToString()))
                {
                    var key_unpublished = "unpublished";
                    if (data?.ContainsKey(key_unpublished) == true
                            && VIDEO_PUBLISHER_ID == long.Parse(data[key_unpublished]?.ToString()))
                    {
                        _statusCallBack.Invoke(EVENT_VIDEO_PUBLISHER_OFFLINE, null);
                    }
                    else
                    {
                        _HandleIfVideoPublisherIsOnline(data);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log($"Got an error while parsing the message: \n{e}");
            }
        }
    }

    private void _HandleIfVideoPublisherIsOnline(Dictionary<string, object> data)
    {
        if (null == data) return;

        string dataString = JsonConvert.SerializeObject(data);
        data = FromJson(dataString);

        var publishers = data?.GetValueOrDefault("publishers", null);
        if (null != publishers && publishers is ICollection<Dictionary<string, object>>)
        {
            var tmp = publishers as ICollection<Dictionary<string, object>>;
            foreach (Dictionary<string, object> publisher in tmp)
            {
                try
                {
                    Dictionary<string, object> tmpPublisher = FromJson(JsonConvert.SerializeObject(publisher));
                    if (VIDEO_PUBLISHER_ID == long.Parse(tmpPublisher[KEY_ID]?.ToString()))
                    {
                        var streams = tmpPublisher.GetValueOrDefault("streams", null);
                        if (streams is ICollection<Dictionary<string, object>>)
                        {
                            var tmpStreams = streams as ICollection<Dictionary<string, object>>;
                            foreach (var tmpItem in tmpStreams)
                            {
                                tmpItem["feed"] = VIDEO_PUBLISHER_ID;
                            }
                        }
                        _statusCallBack.Invoke(EVENT_VIDEO_PUBLISHER_ONLINE, streams);
                    }
                }
                catch (Exception e)
                {
                    Debug.Log($"Got an error while parsing the publiher information: \n{e}");
                }
            }
        }
    }

    protected async Task _MakeSureSessionAndHandleId(bool forPublication, string plugin)
    {
        string sessionId = forPublication ? _publicationEndPoint.SessionId : _subscriptionEndPoint.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            var transactionId = _CreateNewTransactionId();
            var createSessionReq = new Dictionary<string, object>()
                {
                    { KEY_JANUS, "create"},
                };
            _AttachIdsToReq(createSessionReq, transactionId, forPublication);
            await _SendMessageToServer(transactionId, createSessionReq);

            while (null == _waitingJobs[transactionId])
            {
                await Task.Delay(100);
            }

            var result = _GetResponse(transactionId);

            if (null == result.Result)
            {
                throw null == result.Exception ? new Exception("Failed to create session because of empty response") : result.Exception;
            }
            var resp = JsonConvert.DeserializeObject<SimpleJanusResp>(result.Result);
            if (SUCCESS != resp?.janus
                || string.IsNullOrEmpty(resp?.data?.GetValueOrDefault(KEY_ID, null)?.ToString()))
            {
                throw new Exception($"Failed to create session: {resp?.janus}");
            }

            if (forPublication)
            {
                _publicationEndPoint.SessionId = resp.data[KEY_ID].ToString();
            }
            else
            {
                _subscriptionEndPoint.SessionId = resp.data[KEY_ID].ToString();
            }
        }

        string handleId = forPublication ? _publicationEndPoint.HandleId : _subscriptionEndPoint.HandleId;

        if (string.IsNullOrEmpty(handleId))
        {
            var transactionId = _CreateNewTransactionId();
            var attachReq = new Dictionary<string, object>()
            {
                { KEY_JANUS, "attach"},
                { KEY_PLUGIN, plugin},
            };
            _AttachIdsToReq(attachReq, transactionId, forPublication);
            await _SendMessageToServer(transactionId, attachReq);

            while (null == _waitingJobs[transactionId])
            {
                await Task.Delay(100);
            }
            var result = _GetResponse(transactionId);

            if (null == result.Result)
            {
                throw null == result.Exception ? new Exception("Failed to attach session to video room because of empty response") : result.Exception;
            }
            var resp = JsonConvert.DeserializeObject<SimpleJanusResp>(result.Result);
            if (SUCCESS != resp?.janus
                || string.IsNullOrEmpty(resp?.data?.GetValueOrDefault(KEY_ID, null)?.ToString()))
            {
                throw new Exception($"Failed to attach session to video room: {resp?.janus}");
            }
            if (forPublication)
            {
                _publicationEndPoint.HandleId = resp.data[KEY_ID].ToString();
            }
            else
            {
                _subscriptionEndPoint.HandleId = resp.data[KEY_ID].ToString();
            }
        }
    }

    protected virtual string LeaveRoomCommand()
    {
        return "leave";
    }

    private void _StopConnection(bool publish)
    {
        EndPoint endPoint = publish ? _publicationEndPoint : _subscriptionEndPoint;
        if (endPoint.IsValid())
        {
            var transactionId = _CreateNewTransactionId();
            var leaveReq = new Dictionary<string, object>()
            {
                { KEY_JANUS, LeaveRoomCommand() }
            };
            _AttachIdsToReq(leaveReq, transactionId, publish);
            try
            {
                Task.WaitAll(new[] { _SendMessageToServer(transactionId, leaveReq) });
            }
            catch (Exception e)
            {
                Debug.Log($"Failed to leave the video room for {(publish ? "publication" : "subscription")}: \n{e}");

            }
        }
        if (!string.IsNullOrEmpty(endPoint.HandleId))
        {
            var transactionId = _CreateNewTransactionId();
            var detachReq = new Dictionary<string, object>()
        {
            {KEY_JANUS, "detach"}
        };
            _AttachIdsToReq(detachReq, transactionId, publish);

            try
            {
                Task.WaitAll(new[] { _SendMessageToServer(transactionId, detachReq) });
            }
            catch (Exception e)
            {
                Debug.Log($"Failed to destroy handle id for {(publish ? "publication" : "subscription")}: \n{e}");
            }
        }
        endPoint.HandleId = null;
        if (!string.IsNullOrEmpty(endPoint.SessionId))
        {
            var transactionId = _CreateNewTransactionId();
            var destroyReq = new Dictionary<string, object>()
            {
                { KEY_JANUS, "destroy"}
            };
            _AttachIdsToReq(destroyReq, transactionId, publish);

            try
            {
                Task.WaitAll(new[] { _SendMessageToServer(transactionId, destroyReq) });
            }
            catch (Exception e)
            {
                Debug.Log($"Failed to destroy session id for {(publish ? "publication" : "subscription")}: \n{e}");
            }
        }
        endPoint.SessionId = null;
    }

    private static Dictionary<string, object> _ParseToNestedDictionary(Dictionary<string, object> rawDict)
    {
        var dict = new Dictionary<string, object>();

        foreach (var kvp in rawDict)
        {
            if (kvp.Value is Newtonsoft.Json.Linq.JObject jObj)
            {
                dict[kvp.Key] = _ParseToNestedDictionary(jObj.ToObject<Dictionary<string, object>>());
            }
            else if (kvp.Value is Newtonsoft.Json.Linq.JArray jArray)
            {
                dict[kvp.Key] = jArray.ToObject<List<Dictionary<string, object>>>();
            }
            else
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return dict;
    }

    public static Dictionary<string, object> FromJson(string json)
    {
        var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        return _ParseToNestedDictionary(result);
    }

    public void Destroy()
    {
        // destroy the publishing and subscribing session and handle id first
        StopPublishing();
        StopSubscribing();
        Task.WaitAll(new[] { _webSocket?.Close() });
    }

    public IEnumerator ExchangeOffer(string localOffer, Action<TaskResult<string>> action)
    {
        Action<string, Exception> handleError = (error, ex) =>
        {
            Debug.LogError($"{error}: {ex}");
            action.Invoke(new() { Exception = ex });
        };
        Task task;
        // make sure the web socket
        if (null == _webSocket)
        {
            task = Task.Run(async () => await _MakeSureWebSocket());

            yield return new WaitUntil(() => null != _webSocket && _webSocket.State == WebSocketState.Open);
        }

        // make sure the session id and handle id
        if (!_publicationEndPoint.IsValid())
        {
            task = Task.Run(async () => await _MakeSureSessionAndHandleId(true, VIDEO_ROOM_PLUGIN));
            yield return new WaitUntil(() => task.IsCompleted || task.IsCanceled);

            if (!task.IsCompletedSuccessfully)
            {
                handleError("Unable to generate session or handle for publication", task.Exception);
                yield break;
            }
        }

        // start publishing data, using the legacy way
        var transactionId = _CreateNewTransactionId();
        Dictionary<string, object> joinAndConfigureReq = new()
        {
            { "jsep", new Dictionary<string, string> () {
                {"type", "offer"},
                { "sdp", localOffer}
            }},
            { "body", new Dictionary<string, object> () {
                {"request", "joinandconfigure" },
                {"room", ROOM_ID},
                {"pin", ROOM_PIN},
                {"ptype", "publisher"},
                {"id", DATA_PUBLISHER_ID},
                {"display", DATA_PUBLISHER_DISPLAY},
                { "streams", new Dictionary<string, object>[] {
                    new() {
                        { "mid", 0 },
                        { "keyframe", true },
                        { "send", true},
                     }
                 } }
            }}
        };
        _AttachIdsToReq(joinAndConfigureReq, transactionId, true);

        try
        {
            task = _SendMessageToServer(transactionId, joinAndConfigureReq);
            Task.WaitAll(new[] { task });
        }
        catch (Exception e)
        {
            handleError("Unable to send message to server to join the video room", e);
            yield break;
        }

        yield return _Reply(transactionId);

        var result = _GetResponse(transactionId);

        // check if the result is correct
        if (null == result.Result)
        {
            var exception = result.Exception ?? new Exception("Empty response");
            handleError("Unable to join the video room as the data publisher", exception);
            yield break;
        }

        var error = _GetErrorFromResp(result.Result);

        if (!string.IsNullOrEmpty(error))
        {
            handleError("Unable to join the video room as the data publisher", new(error));
            yield break;
        }

        var joinResult = result;

        // setting up forwarding
        transactionId = _CreateNewTransactionId();
        var forwardingReq = new Dictionary<string, object>()
        {
            { "body", new Dictionary<string, object>() {
                {"request", "rtp_forward"},
                {"room", ROOM_ID },
                {"secret", ROOM_PIN},
                {"publisher_id", DATA_PUBLISHER_ID},
                {"host", "127.0.0.1"},
                {"data_port", 5006},
                {"host_family", "ipv4"}
            } }
        };
        _AttachIdsToReq(forwardingReq, transactionId, true);

        try
        {
            task = _SendMessageToServer(transactionId, forwardingReq);
            Task.WaitAll(new[] { task });
        }
        catch (Exception e)
        {
            handleError("Unable to send message to server to forward data", e);
            yield break;
        }

        yield return _Reply(transactionId);

        result = _GetResponse(transactionId);

        // check if the result is correct
        if (null == result.Result)
        {
            var exception = result.Exception ?? new Exception("Empty response");
            handleError("Failed to request server to forward data", exception);
            yield break;
        }

        var resp = JsonConvert.DeserializeObject<JanusPluginResponse>(joinResult.Result);

        _HandleIfVideoPublisherIsOnline(resp?.plugindata?.data);

        action?.Invoke(new() { Result = resp?.jsep.sdp });
    }

    public virtual IEnumerator FetchRemoteOffer(object data, Action<TaskResult<string>> action)
    {
        Action<string, Exception> handleError = (error, ex) =>
        {
            Debug.LogError($"{error}: {ex}");
            action.Invoke(new() { Exception = ex });
        };

        Task task;
        // make sure the web socket
        if (null == _webSocket)
        {
            task = Task.Run(async () => await _MakeSureWebSocket());

            yield return new WaitUntil(() => _webSocket is { State: WebSocketState.Open });
        }

        // make sure the session id and handle id
        if (!_subscriptionEndPoint.IsValid())
        {
            task = Task.Run(async () => await _MakeSureSessionAndHandleId(false, VIDEO_ROOM_PLUGIN));
            yield return new WaitUntil(() => task.IsCompleted || task.IsCanceled);

            if (!task.IsCompletedSuccessfully)
            {
                handleError("Unable to generate session or handle for subscription", task.Exception);
                yield break;
            }
        }

        // start subscribing data, using the legacy way
        var transactionId = _CreateNewTransactionId();
        var body = new Dictionary<string, object> {
            {"request", "join" },
            {"ptype", "subscriber" },
            {"room", ROOM_ID },
            {"pin", ROOM_PIN },
            {"feed", VIDEO_PUBLISHER_ID },
            {"audoupdate", false },
        };
        if (null != data)
        {
            body.Remove("feed");
            body["streams"] = data;
        }
        Dictionary<string, object> joinReq = new()
        {
            {"body", body},
        };
        _AttachIdsToReq(joinReq, transactionId, false);
        try
        {
            task = _SendMessageToServer(transactionId, joinReq);
            Task.WaitAll(new[] { task });
        }
        catch (Exception e)
        {
            handleError("Failed to send join command to video room", e);
            yield break;
        }

        yield return _Reply(transactionId);

        var result = _GetResponse(transactionId);

        if (null == result.Result)
        {
            var exception = null == result.Exception ? new Exception("Empty response") : result.Exception;
            handleError("Unable to join the video room as the video subscriber", exception);
            yield break;
        }

        var error = _GetErrorFromResp(result.Result);
        if (!string.IsNullOrEmpty(error))
        {
            handleError("Unable to join the video room as the video subscriber", new(error));
            yield break;
        }

        var resp = JsonConvert.DeserializeObject<JanusPluginResponse>(result.Result);

        action.Invoke(new() { Result = resp.jsep.sdp });
    }


    public virtual IEnumerator UpdateLocalOffer(string localAnswer, Action<TaskResult<object>> callBack)
    {
        Action<string, Exception> handleError = (error, ex) =>
        {
            Debug.LogError($"{error}: {ex}");
            callBack.Invoke(new() { Exception = ex });
        };
        var transactionId = _CreateNewTransactionId();
        Dictionary<string, object> startReq = new()
        {
            { "body", new Dictionary<string, object>() {
                { "request", "start" }
            }},
            { "jsep", new Dictionary<string, object>() {
                {"type", "answer"},
                { "sdp", localAnswer }
            } }
        };
        _AttachIdsToReq(startReq, transactionId, false);

        try
        {
            var task = _SendMessageToServer(transactionId, startReq);
            Task.WaitAll(new[] { task });
        }
        catch (Exception e)
        {
            handleError("Failed to send local answer to server for subscription", e);
            yield break;
        }

        yield return _Reply(transactionId);

        var result = _GetResponse(transactionId);
        var pluginResp = JsonConvert.DeserializeObject<JanusPluginResponse>(result.Result);

        var error = _GetErrorFromResp(result.Result);
        if (!string.IsNullOrEmpty(error)
            || "ok" != pluginResp?.plugindata?.data?.GetValueOrDefault("started", null)?.ToString())
        {
            handleError("Failed to send local answer to server for subscription",
                new(string.IsNullOrEmpty(error) ? "Unhandled situation" : error));
            yield break;
        }

        callBack?.Invoke(new());
    }

    public void StopPublishing()
    {
        _StopConnection(true);
    }

    public void StopSubscribing()
    {
        _StopConnection(false);
    }
}

public class StreamingOfferExchanger : VideoRoomOfferExchanger
{
    public StreamingOfferExchanger() : base(null)
    {
    }

    public override IEnumerator FetchRemoteOffer(object data, Action<TaskResult<string>> action)
    {
        Action<string, Exception> handleError = (error, ex) =>
        {
            Debug.LogError($"{error}: {ex}");
            action.Invoke(new() { Exception = ex });
        };

        Task task;
        // make sure the web socket
        if (null == _webSocket)
        {
            task = Task.Run(async () => await _MakeSureWebSocket());

            yield return new WaitUntil(() => null != _webSocket && _webSocket.State == WebSocketState.Open);
        }

        // make sure the session id and handle id
        if (!_subscriptionEndPoint.IsValid())
        {
            task = Task.Run(async () => await _MakeSureSessionAndHandleId(false, "janus.plugin.streaming"));
            yield return new WaitUntil(() => task.IsCompleted || task.IsCanceled);

            if (!task.IsCompletedSuccessfully)
            {
                handleError("Unable to generate session or handle for subscription", task.Exception);
                yield break;
            }
        }

        // start subscribing data, using the legacy way
        var transactionId = _CreateNewTransactionId();
        var body = new Dictionary<string, object> {
            {"request", "watch" },
            {"id", 1 },
            {"pin", ROOM_PIN },
        };
        Dictionary<string, object> joinReq = new()
        {
            {"body", body},
        };
        _AttachIdsToReq(joinReq, transactionId, false);
        try
        {
            task = _SendMessageToServer(transactionId, joinReq);
            Task.WaitAll(new[] { task });
        }
        catch (Exception e)
        {
            handleError("Failed to send join command to video room", e);
            yield break;
        }

        yield return _Reply(transactionId);

        var result = _GetResponse(transactionId);

        if (null == result.Result)
        {
            var exception = null == result.Exception ? new Exception("Empty response") : result.Exception;
            handleError("Unable to join the streaming room to subscribe the video and audio", exception);
            yield break;
        }

        var error = _GetErrorFromResp(result.Result);
        if (!string.IsNullOrEmpty(error))
        {
            handleError("Unable to join the streaming room to subscribe the video and audio", new(error));
            yield break;
        }

        var resp = JsonConvert.DeserializeObject<JanusPluginResponse>(result.Result);

        action.Invoke(new() { Result = resp.jsep.sdp });
    }

    public override IEnumerator UpdateLocalOffer(string localAnswer, Action<TaskResult<object>> callBack)
    {
        Action<string, Exception> handleError = (error, ex) =>
        {
            Debug.LogError($"{error}: {ex}");
            callBack.Invoke(new() { Exception = ex });
        };
        var transactionId = _CreateNewTransactionId();
        Dictionary<string, object> startReq = new()
        {
            { "body", new Dictionary<string, object>() {
                { "request", "start" }
            }},
            { "jsep", new Dictionary<string, object>() {
                {"type", "answer"},
                { "sdp", localAnswer }
            } }
        };
        _AttachIdsToReq(startReq, transactionId, false);

        try
        {
            var task = _SendMessageToServer(transactionId, startReq);
            Task.WaitAll(new[] { task });
        }
        catch (Exception e)
        {
            handleError("Failed to send local answer to server for subscription", e);
            yield break;
        }

        yield return _Reply(transactionId);

        var result = _GetResponse(transactionId);
        var pluginResp = JsonConvert.DeserializeObject<JanusPluginResponse>(result.Result);

        var error = _GetErrorFromResp(result.Result);
        if (!string.IsNullOrEmpty(error))
        {
            handleError("Failed to send local answer to server for subscription", new(error));
            yield break;
        }
        var tmp = pluginResp?.plugindata?.data?.GetValueOrDefault("result", null);
        if (null != tmp && "starting" == FromJson(JsonConvert.SerializeObject(tmp))?.GetValueOrDefault("status")?.ToString())
        {
            callBack?.Invoke(new());
            yield break;
        }

        handleError("Failed to send local answer to server for subscription", new("Unhanled situation"));
        yield break;
    }

    protected override string LeaveRoomCommand()
    {
        return "stop";
    }
}