using UnityEngine;
using Unity.WebRTC;

public class ApplyVideoTexture : MonoBehaviour, IOnEventListener
{

    private MediaStream mediaStream = new();

    private Renderer planeRender;

    private AudioSource audioSource;

    public void OnEvent(string ev, object con)
    {
        WebRtcEvent webRtcEvent = (WebRtcEvent) con;
        RTCPeerConnection connection = (RTCPeerConnection)webRtcEvent.data;
        if (WebRtcManager.VIDEO_RECEIVER != webRtcEvent.identity)
        {
            return;
        }

        planeRender.material.SetTexture("_MainTex", null);

        mediaStream = new MediaStream();

        connection.OnTrack = e =>
        {
            Debug.Log($"OnTrack - Track ID: {e.Track.Id}, Track type: {e.Track.Kind}");

            mediaStream.AddTrack(e.Track);
        };
        mediaStream.OnAddTrack = e =>
        {
            if (e.Track is VideoStreamTrack videoTrack)
            {
                Debug.Log("Add a video stream track.");
                videoTrack.OnVideoReceived += (tex) =>
                {
                    Debug.Log($"Received the video data from webrtc: {tex.width} x {tex.height}");
                    planeRender.material.SetTexture("_MainTex", tex);
                    // rawImage.texture = tex;
                };
            }
            else if (e.Track is AudioStreamTrack audioTrack)
            {
                Debug.Log("Add a audio stream track.");
                audioSource.SetTrack(audioTrack);
                audioSource.loop = true;
                audioSource.Play();
            }
        };
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        planeRender = GetComponent<Renderer>();
        audioSource = gameObject.AddComponent<AudioSource>();

        if (null == EventManager.Instance)
        {
            Debug.Log("The instance is null");
        }
        EventManager.Instance.Observe(EventManager.CONNECTION, this);
    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnDestroy()
    {
        mediaStream?.Dispose();
        EventManager.Instance.UnObserve(EventManager.CONNECTION, this);
    }

}
