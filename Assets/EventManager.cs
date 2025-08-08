using UnityEngine;
using System.Collections.Generic;

public interface IOnEventListener
{
    void OnEvent(string ev, object arg);
}

public class EventManager : MonoBehaviour
{

    public const string CONNECTION = "connection";

    public const string CHANNEL_OPEN = "channel_open";

    public const string CHANNEL_CLOSE = "channel_close";

    public const string CHANNEL_MSG = "channel_msg";

    public const string DISCONNECTION = "disconnection";

    public static EventManager Instance { get; private set; }

    private readonly Dictionary<string, List<IOnEventListener>> eventAndAction = new();

    public void Observe(string key, IOnEventListener callback) 
    {
        List<IOnEventListener> action;
        if (eventAndAction.ContainsKey(key))
        {
            action = eventAndAction[key];
        }
        else
        {
            action = new List<IOnEventListener>();
            eventAndAction.TryAdd(key, action);
        }
        action.Add(callback);
    }

    public void UnObserve(string key, IOnEventListener callback)
    {
        if (!eventAndAction.ContainsKey(key))
        {
            return;
        }
        var actions = eventAndAction[key];
        actions.Remove(callback);
    }

    public void Notify(string key, object result)
    {
        if (eventAndAction.ContainsKey(key)) {
            var actions = eventAndAction[key];
            foreach(var action in actions)
            {
                action.OnEvent(key, result);
            }
        }
    }

    void Awake()
    {
        Debug.Log("Application Start");
        if (null == Instance)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }  
        else
        {
            Destroy(gameObject);
        }
    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
