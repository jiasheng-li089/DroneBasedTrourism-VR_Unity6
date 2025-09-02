using UnityEngine;
using UnityEngine.UI;

public class StatusInformationRender : MonoBehaviour, IOnEventListener
{

    [SerializeField]
    private Text statusText;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private void Awake()
    {
        // while only subscribing the video stream, don't show this component,
        // this component is only used to provide guide information for user.
        gameObject.SetActive(!ConfigManager.ONLY_VIDEO);
    }

    void Start()
    {
        statusText = transform.Find("status").GetComponent<Text>();
        EventManager.Instance.Observe(EventManager.GUIDE_INFO, this);
    }

    private void OnDestroy()
    {
        EventManager.Instance.UnObserve(EventManager.GUIDE_INFO, this);
    }

    public void OnEvent(string ev, object arg)
    {
        if (EventManager.GUIDE_INFO == ev && arg is string s)
        {
            statusText.text = s;
        }
    }
}
